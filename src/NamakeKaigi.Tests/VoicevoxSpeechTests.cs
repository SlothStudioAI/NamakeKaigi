using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.Services.Voicevox;

namespace NamakeKaigi.Tests;

/// <summary>VOICEVOXで読み上げる機能(WBS 11.36)の試験。本物のVOICEVOXは使わず、偽物の部品で動きを確かめる。</summary>
public class VoicevoxSpeechTests
{
    /// <summary>頼まれた文と声を記録して、決まった音声を返す(失敗させることもできる)偽のVOICEVOX。</summary>
    private sealed class FakeClient : IVoicevoxClient
    {
        public List<(string Text, int StyleId)> Requests { get; } = new();
        public bool Fail { get; set; }
        public Func<string, Task>? Gate { get; set; }

        public async Task<byte[]> SynthesizeAsync(string text, VoicevoxSpeaker speaker, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add((text, speaker.StyleId));
            if (Gate is not null) await Gate(text);
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new HttpRequestException("つながらない");
            return System.Text.Encoding.UTF8.GetBytes(text);
        }
    }

    /// <summary>鳴らした音声を記録する偽のプレーヤー(鳴らし終わるタイミングを、試験が決められる)。</summary>
    private sealed class FakePlayer : IWavPlayer
    {
        public List<string> Played { get; } = new();
        public int StopCount { get; private set; }
        public Func<Task>? Gate { get; set; }

        public async Task PlayAsync(byte[] wav, CancellationToken cancellationToken)
        {
            lock (Played) Played.Add(System.Text.Encoding.UTF8.GetString(wav));
            if (Gate is not null) await Gate();
            cancellationToken.ThrowIfCancellationRequested();
        }

        public void Stop() => StopCount++;
    }

    /// <summary>代わりの声(Windows標準)の偽物。</summary>
    private sealed class FakeFallback : ISpeechService
    {
        public bool IsAvailable { get; set; } = true;
        public string UnavailableReason => "";
        public bool IsSpeaking => false;
        public event Action? SpeakingChanged { add { } remove { } }
        public List<string> Spoken { get; } = new();
        public int StopCount { get; private set; }
        public void Enqueue(string text) => Spoken.Add(text);
        public void Stop() => StopCount++;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "時間内に条件が満たされなかった");
    }

    private static (VoicevoxSpeechService Service, FakeClient Client, FakePlayer Player, FakeFallback Fallback) Create(
        VoicevoxSettings? settings = null)
    {
        var client = new FakeClient();
        var player = new FakePlayer();
        var fallback = new FakeFallback();
        return (new VoicevoxSpeechService(settings ?? new VoicevoxSettings(), client, player, fallback), client, player, fallback);
    }

    [Theory]
    [InlineData("こんにちは。元気ですか？はい！", new[] { "こんにちは。", "元気ですか？", "はい！" })]
    [InlineData("一行目\n二行目。", new[] { "一行目", "二行目。" })]
    [InlineData("句点がない文", new[] { "句点がない文" })]
    [InlineData("  \n  ", new string[0])]
    public void 本文を文ごとに分ける(string text, string[] expected)
    {
        Assert.Equal(expected, VoicevoxSpeechService.SplitSentences(text));
    }

    [Fact]
    public void 長い文は読点で区切って音声にし_短い文と区切りのない文はそのまま()
    {
        const string longSentence = "あいうえおかきくけこ、さしすせそたちつてと、なにぬねのはひふへほ、まみむめもやゆよ。";

        // 目安15字: 10字の節を、15字に収まる間だけつなぐ(2つ目の節を足すと20字になるので、1節ずつ)
        Assert.Equal(new[] { "あいうえおかきくけこ、", "さしすせそたちつてと、", "なにぬねのはひふへほ、", "まみむめもやゆよ。" },
            VoicevoxSpeechService.SplitForSpeaking(longSentence, 15));
        // 目安25字: 2節ずつつながる
        Assert.Equal(new[] { "あいうえおかきくけこ、さしすせそたちつてと、", "なにぬねのはひふへほ、まみむめもやゆよ。" },
            VoicevoxSpeechService.SplitForSpeaking(longSentence, 25));
        // 0なら、文のまま。短い文・読点のない長い文も、そのまま
        Assert.Equal(new[] { longSentence }, VoicevoxSpeechService.SplitForSpeaking(longSentence, 0));
        Assert.Equal(new[] { "短い、文。" }, VoicevoxSpeechService.SplitForSpeaking("短い、文。", 25));
        const string noComma = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよ。";
        Assert.Equal(new[] { noComma }, VoicevoxSpeechService.SplitForSpeaking(noComma, 15));
        // 文の区切りは、先に効く
        Assert.Equal(new[] { "一つ目。", "二つ目。" }, VoicevoxSpeechService.SplitForSpeaking("一つ目。二つ目。", 25));
    }

    [Fact]
    public void 読むときも_長い文は区切って_最初の断片から鳴らす()
    {
        var (service, client, player, _) = Create(new VoicevoxSettings(MaxChunkChars: 12));

        service.Enqueue("あいうえおかきくけこ、さしすせそたちつてと、なにぬねの。", Participants.ClaudeId);

        WaitUntil(() => player.Played.Count == 3).GetAwaiter().GetResult();
        Assert.Equal(new[] { "あいうえおかきくけこ、", "さしすせそたちつてと、", "なにぬねの。" }, player.Played);
        Assert.Equal(3, client.Requests.Count);
    }

    [Fact]
    public void メンバーごとに動画と同じ声で読み_名前の前置きは要らない()
    {
        var (service, client, player, fallback) = Create();

        service.Enqueue("クロです。", Participants.ClaudeId);
        service.Enqueue("ジェミです。", Participants.GeminiId);
        service.Enqueue("チャッピーです。", Participants.ChappyId);

        WaitUntil(() => player.Played.Count == 3).GetAwaiter().GetResult();
        Assert.Equal(new[] { ("クロです。", 14), ("ジェミです。", 54), ("チャッピーです。", 11) }, client.Requests);
        Assert.Equal(new[] { "クロです。", "ジェミです。", "チャッピーです。" }, player.Played); // 順番に、重ならず
        Assert.Empty(fallback.Spoken);
        Assert.False(service.NeedsSpeakerNamePrefix);
        Assert.Equal("", service.Note);
    }

    [Fact]
    public void 設定の声が使われ_設定にない人は標準の声()
    {
        var settings = new VoicevoxSettings(Speakers: new Dictionary<string, VoicevoxSpeaker> { [Participants.ClaudeId] = new(3, 1.2, 0.9) });
        var (service, client, player, _) = Create(settings);

        service.Enqueue("あ。", Participants.ClaudeId);
        service.Enqueue("い。", Participants.GeminiId);

        WaitUntil(() => player.Played.Count == 2).GetAwaiter().GetResult();
        Assert.Equal(new[] { ("あ。", 3), ("い。", 54) }, client.Requests);
    }

    [Fact]
    public void 鳴らしている間に_次の文を先に作っておく()
    {
        var (service, client, player, _) = Create();
        var release = new TaskCompletionSource();
        player.Gate = () => release.Task; // 1文目を鳴らし終わらせない

        service.Enqueue("一つ目。二つ目。三つ目。", Participants.ClaudeId);

        WaitUntil(() => player.Played.Count == 1).GetAwaiter().GetResult();
        WaitUntil(() => client.Requests.Count == 2).GetAwaiter().GetResult(); // 1文目を鳴らしている間に、2文目を作った
        Assert.Equal(2, client.Requests.Count); // 3文目は、まだ(先読みは1文だけ)
        release.SetResult();
        WaitUntil(() => player.Played.Count == 3).GetAwaiter().GetResult();
    }

    [Fact]
    public void つながらないときは_代わりの声で名前を添えて読み_知らせが出る_つながれば戻る()
    {
        var (service, client, player, fallback) = Create();
        client.Fail = true;

        service.Enqueue("こんにちは。よろしく。", Participants.GeminiId);

        WaitUntil(() => fallback.Spoken.Count == 1).GetAwaiter().GetResult();
        Assert.Equal("ジェミリーです。こんにちは。よろしく。", fallback.Spoken[0]);
        Assert.Empty(player.Played);
        Assert.Contains("Windowsの声で読んでいます", service.Note);

        client.Fail = false;
        service.Enqueue("戻りました。", Participants.GeminiId);
        WaitUntil(() => player.Played.Count == 1).GetAwaiter().GetResult();
        Assert.Equal("", service.Note);
    }

    [Fact]
    public void 代わりの声も使えないときは_知らせに書く()
    {
        var (service, client, _, fallback) = Create();
        client.Fail = true;
        fallback.IsAvailable = false;

        service.Enqueue("読めない。", Participants.ClaudeId);

        WaitUntil(() => service.Note.Length > 0).GetAwaiter().GetResult();
        Assert.Contains("読めません", service.Note);
    }

    [Fact]
    public void 声が決まっていない人は_代わりの声で読む()
    {
        var (service, client, _, fallback) = Create();

        service.Enqueue("だれか。", "unknown");

        WaitUntil(() => fallback.Spoken.Count == 1).GetAwaiter().GetResult();
        Assert.Equal("だれか。", fallback.Spoken[0]);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public void 止めると_鳴らしているものと順番待ちが止まり_そのあと足したものは読める()
    {
        var (service, _, player, fallback) = Create();
        var release = new TaskCompletionSource();
        player.Gate = () => release.Task;

        service.Enqueue("一つ目。", Participants.ClaudeId);
        service.Enqueue("二つ目。", Participants.ClaudeId); // 順番待ち
        WaitUntil(() => player.Played.Count == 1).GetAwaiter().GetResult();
        Assert.True(service.IsSpeaking);

        service.Stop();
        release.SetResult();
        WaitUntil(() => !service.IsSpeaking).GetAwaiter().GetResult();
        Assert.Equal(1, player.StopCount);
        Assert.Equal(1, fallback.StopCount);
        Assert.Single(player.Played); // 順番待ちだった「二つ目」は鳴らない

        player.Gate = null;
        service.Enqueue("三つ目。", Participants.ClaudeId);
        WaitUntil(() => player.Played.Count == 2).GetAwaiter().GetResult();
        Assert.Equal("三つ目。", player.Played[1]);
    }

    [Fact]
    public async Task 想定外の例外が起きてもワーカーは止まらず次の発言を読み上げられる()
    {
        var client = new FakeClient();
        var player = new FakePlayer();
        var fallback = new FakeFallback();
        using var service = new VoicevoxSpeechService(new VoicevoxSettings(), client, player, fallback);

        // 1つ目の文で例外を発生させる
        player.Gate = () => throw new InvalidOperationException("破損したWAVデータ");

        service.Enqueue("一つ目。", Participants.ClaudeId);

        // 1つ目の処理が抜けるまで待つ
        await Task.Delay(150);

        // 2つ目は正常に再生できる
        player.Gate = null;
        service.Enqueue("二つ目。", Participants.ClaudeId);

        await WaitUntil(() => player.Played.Count == 2);
        Assert.Equal("二つ目。", player.Played.Last());
    }

    [Fact]
    public void 本物のクライアントは_音声の元に問い合わせて_声と抑揚を渡す()
    {
        var requests = new List<(string Url, string Body)>();
        var handler = new StubHandler(request =>
        {
            requests.Add((request.RequestUri!.PathAndQuery, request.Content?.ReadAsStringAsync().Result ?? ""));
            return request.RequestUri.AbsolutePath == "/audio_query"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"speedScale":1.0,"intonationScale":1.0,"x":5}""") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
        });
        using var client = new VoicevoxClient(new VoicevoxSettings("http://127.0.0.1:50021/"), handler);

        var wav = client.SynthesizeAsync("こんにちは", new VoicevoxSpeaker(14, 1.7, 1.1), CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(new byte[] { 1, 2, 3 }, wav);
        Assert.Equal("/audio_query?text=%E3%81%93%E3%82%93%E3%81%AB%E3%81%A1%E3%81%AF&speaker=14", requests[0].Url);
        Assert.Equal("/synthesis?speaker=14", requests[1].Url);
        var sent = JsonNode.Parse(requests[1].Body)!;
        Assert.Equal(1.7, (double)sent["intonationScale"]!);
        Assert.Equal(1.1, (double)sent["speedScale"]!);
        Assert.Equal(5, (int)sent["x"]!); // 元のパラメータは、そのまま残す
    }

    [Fact]
    public void 設定ファイルの_voice_engine_と_voicevox_を読める_書かなければ標準()
    {
        var json = """
        {"claude":{"executable":"claude","model":"sonnet","extraArgs":[]},
         "agy":{"executable":"agy","extraArgs":[],"noToolNote":""},
         "workDir":"C:/x","meetingsDir":"C:/y","maxInjectChars":4000,"timeoutSec":600,
         "voice":{"engine":"voicevox","voicevox":{"baseUrl":"http://127.0.0.1:50022","speakers":{"claude":{"styleId":3}}}}}
        """;
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal("voicevox", settings.Voice!.Engine);
        Assert.Equal("http://127.0.0.1:50022", settings.Voice.Voicevox!.BaseUrl);
        Assert.Equal(20, settings.Voice.Voicevox.TimeoutSec);
        Assert.Equal(new VoicevoxSpeaker(3), settings.Voice.Voicevox.SpeakerOf(Participants.ClaudeId)); // 抑揚の標準は1.7
        Assert.Equal(54, settings.Voice.Voicevox.SpeakerOf(Participants.GeminiId)!.StyleId);
        Assert.Null(new VoiceSettings().Engine);
    }

    /// <summary>通信の代わりに、決まった返事を返す偽物。</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
