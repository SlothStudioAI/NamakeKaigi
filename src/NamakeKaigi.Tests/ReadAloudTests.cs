using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>返答の読み上げ(WBS 11.35)の試験。声そのものは鳴らさず、読み上げる文と、いつ読み・止めるかを試験する。</summary>
public class ReadAloudTests
{
    /// <summary>読み上げの代わりに、頼まれた文を記録するだけの偽物。</summary>
    private sealed class FakeSpeech : ISpeechService
    {
        public bool IsAvailable { get; set; } = true;
        public string UnavailableReason { get; set; } = "";
        public bool IsSpeaking => Spoken.Count > 0;
        public event Action? SpeakingChanged;
        public List<string> Spoken { get; } = new();
        public int StopCount { get; private set; }
        public void Enqueue(string text) { Spoken.Add(text); SpeakingChanged?.Invoke(); }
        public void Stop() { StopCount++; Spoken.Clear(); SpeakingChanged?.Invoke(); }
    }

    private static ChatProgress Done(string id, string text) => new(id, ChatProgressKind.Done, text);

    private static MainViewModel Create(FakeSpeech speech, VoiceSettings? voice = null, FakeSession? session = null)
    {
        session ??= new FakeSession { Script = _ => Array.Empty<ChatProgress>() };
        return new MainViewModel(() => session, speech: speech, voice: voice);
    }

    [Fact]
    public void 先頭に話し手の名前を言い_記号を読まない文になる()
    {
        var text = SpeechTextFormatter.Format("## 結論\n**大事な**点は `dotnet test` です。\n- 一つ目\n- 二つ目", "クローディ", 400);

        Assert.Equal("クローディです。結論。大事な点は dotnet test です。一つ目。二つ目。", text);
    }

    [Fact]
    public void コードと表は_あるとだけ言う()
    {
        var text = SpeechTextFormatter.Format("見て\n```\nvar a = 1;\nvar b = 2;\n```\n| a | b |\n|---|---|\n| 1 | 2 |\n以上", "ジェミリー", 400);

        Assert.Equal("ジェミリーです。見て。コードがあります。表があります。以上。", text);
    }

    [Fact]
    public void URLはリンク_パスはファイル名_画像は画像がある_リンク記法は文字だけ()
    {
        var text = SpeechTextFormatter.Format(
            "https://example.com/a を見て。C:\\work\\資料\\報告.png と ![図](C:/x.png) と [公式](https://a.b/c)", "チャッピー", 400);

        Assert.Equal("チャッピーです。リンク を見て。報告.png と 画像があります と 公式。", text);
    }

    [Fact]
    public void 長い返答は_文の切れ目で切って_続きは画面で見てください_と言う()
    {
        var text = SpeechTextFormatter.Format("あいうえお。かきくけこ。さしすせそ。たちつてと。", "クローディ", 12);

        Assert.Equal("クローディです。あいうえお。かきくけこ。" + SpeechTextFormatter.Continuation, text);
        Assert.Equal("クローディです。あいうえお。かきくけこ。さしすせそ。たちつてと。",
            SpeechTextFormatter.Format("あいうえお。かきくけこ。さしすせそ。たちつてと。", "クローディ", 0)); // 0なら全部読む
    }

    [Theory]
    [InlineData("")]
    [InlineData("---")]
    [InlineData("```\nコードだけ\n```x")]
    public void 読むものがなければ_空文字になる(string markdown)
    {
        var text = SpeechTextFormatter.Format(markdown, "クローディ", 400);

        if (markdown.StartsWith("```")) Assert.Contains("コードがあります", text);
        else Assert.Equal("", text);
    }

    [Fact]
    public void 声は_名前の指定があればそれ_なければ日本語の最初_日本語がなければなし()
    {
        var voices = new[] { ("Microsoft Zira Desktop", "en-US"), ("Microsoft Haruka Desktop", "ja-JP"), ("Microsoft Ichiro", "ja-JP") };

        Assert.Equal("Microsoft Haruka Desktop", SystemSpeechService.PickVoice(voices, null));
        Assert.Equal("Microsoft Ichiro", SystemSpeechService.PickVoice(voices, "microsoft ichiro"));
        Assert.Equal("Microsoft Haruka Desktop", SystemSpeechService.PickVoice(voices, "ない声")); // 指定の声がなければ、日本語の最初
        Assert.Null(SystemSpeechService.PickVoice(new[] { ("Microsoft Zira Desktop", "en-US") }, null));
    }

    [Fact]
    public void スイッチがONなら_返答が完成したときに読む_途中の文字とエラーとあなたの発言は読まない()
    {
        var speech = new FakeSpeech();
        var session = new FakeSession();
        var vm = Create(speech, session: session);
        vm.ReadAloudOn = true;

        vm.InputText = "全員、こんにちは";
        _ = vm.SendAsync();
        session.Push(new ChatProgress(Participants.ClaudeId, ChatProgressKind.Delta, "途中の文字"));
        Assert.Empty(speech.Spoken); // 途中は読まない

        session.Push(Done(Participants.ClaudeId, "こんにちは。"));
        session.Push(new ChatProgress(Participants.GeminiId, ChatProgressKind.Error, "失敗しました"));
        session.Push(Done(Participants.GeminiId, "やあ"));

        Assert.Equal(new[] { "クローディです。こんにちは。", "ジェミリーです。やあ。" }, speech.Spoken); // エラーとあなたの発言は読まない
    }

    [Fact]
    public void スイッチがOFFなら読まない_ONからOFFにすると止める()
    {
        var speech = new FakeSpeech();
        var session = new FakeSession();
        var vm = Create(speech, session: session);

        vm.InputText = "クロ、こんにちは";
        _ = vm.SendAsync();
        session.Push(Done(Participants.ClaudeId, "返事"));
        Assert.Empty(speech.Spoken);

        vm.ReadAloudOn = true;
        var stopsBefore = speech.StopCount;
        vm.ReadAloudOn = false;
        Assert.Equal(stopsBefore + 1, speech.StopCount);
    }

    [Fact]
    public void 吹き出しの読み上げは_スイッチがOFFでも読む()
    {
        var speech = new FakeSpeech();
        var session = new FakeSession();
        var vm = Create(speech, session: session);
        vm.InputText = "クロ、こんにちは";
        _ = vm.SendAsync();
        session.Push(Done(Participants.ClaudeId, "返事です"));
        var reply = vm.Messages.Last(m => m.Speaker.Id == Participants.ClaudeId);

        Assert.True(reply.CanSpeak);
        vm.SpeakMessage(reply);

        Assert.Equal(new[] { "クローディです。返事です。" }, speech.Spoken);
    }

    [Fact]
    public void 新しい発言を送ると_読んでいるものを止める_止めるボタンでも止まる()
    {
        var speech = new FakeSpeech();
        var vm = Create(speech);
        vm.ReadAloudOn = true;
        speech.Enqueue("読んでいる最中");
        Assert.True(vm.IsSpeaking);

        vm.InputText = "次の発言";
        _ = vm.SendAsync();
        Assert.Empty(speech.Spoken);
        Assert.False(vm.IsSpeaking);

        speech.Enqueue("また読んでいる");
        vm.StopSpeakingCommand.Execute(null);
        Assert.Empty(speech.Spoken);
    }

    [Fact]
    public void 読み上げの機能がないとき_設定の行と吹き出しのスピーカーと止めるボタンを出さない()
    {
        var session = new FakeSession();
        var vm = new MainViewModel(() => session, speech: null); // voice.enabled が false のとき、アプリは部品を渡さない
        vm.InputText = "クロ、こんにちは";
        _ = vm.SendAsync();
        session.Push(Done(Participants.ClaudeId, "返事です"));
        var reply = vm.Messages.Last(m => m.Speaker.Id == Participants.ClaudeId);

        Assert.False(vm.ReadAloudEnabled);
        Assert.False(reply.CanSpeak);
        Assert.False(vm.IsSpeaking);
        vm.SpeakMessage(reply); // 呼ばれても、何も起きない
        vm.ReadAloudOn = true;
        Assert.False(vm.ReadAloudOn);
    }

    [Fact]
    public void 読み上げの機能があるとき_吹き出しのスピーカーを出す()
    {
        var session = new FakeSession();
        var vm = Create(new FakeSpeech(), session: session);
        vm.InputText = "クロ、こんにちは";
        _ = vm.SendAsync();
        session.Push(Done(Participants.ClaudeId, "返事です"));

        Assert.True(vm.ReadAloudEnabled);
        Assert.True(vm.Messages.Last(m => m.Speaker.Id == Participants.ClaudeId).CanSpeak);
    }

    [Fact]
    public void 読み上げの機能は_既定では使わない()
    {
        Assert.False(new VoiceSettings().Enabled);
        var loaded = SettingsLoader.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json"), "存在しないファイル.json");
        Assert.False((loaded.Voice ?? new VoiceSettings()).Enabled);
    }

    [Fact]
    public void 日本語の声がないときは_スイッチは使えず_ONにもならず_説明に直し方が出る()
    {
        var speech = new FakeSpeech { IsAvailable = false, UnavailableReason = SystemSpeechService.NoJapaneseVoiceReason };
        var vm = Create(speech, new VoiceSettings(AutoRead: true));

        Assert.False(vm.ReadAloudAvailable);
        Assert.False(vm.ReadAloudOn); // 起動時にONの設定でも、使えなければOFF
        Assert.False(vm.ToggleReadAloudCommand.CanExecute(null));
        vm.ReadAloudOn = true;
        Assert.False(vm.ReadAloudOn);
        Assert.Contains("日本語の声が見つかりません", vm.ReadAloudTooltip);
        Assert.Contains("使えません", vm.ReadAloudLabel);
    }

    [Fact]
    public void 設定で起動時からONにでき_読む長さの上限も設定から読む()
    {
        var speech = new FakeSpeech();
        var session = new FakeSession();
        var vm = Create(speech, new VoiceSettings(MaxChars: 5, AutoRead: true), session);
        Assert.True(vm.ReadAloudOn);

        vm.InputText = "クロ、長い話をして";
        _ = vm.SendAsync();
        session.Push(Done(Participants.ClaudeId, "あいうえおかきくけこ"));

        Assert.Equal("クローディです。あいうえお" + SpeechTextFormatter.Continuation, Assert.Single(speech.Spoken));
    }

    [Fact]
    public void 設定ファイルの_voice_を読める_書かなければ標準()
    {
        var json = """
        {"claude":{"executable":"claude","model":"sonnet","extraArgs":[]},
         "agy":{"executable":"agy","extraArgs":[],"noToolNote":""},
         "workDir":"C:/x","meetingsDir":"C:/y","maxInjectChars":4000,"timeoutSec":600,
         "voice":{"voiceName":"Microsoft Haruka Desktop","rate":2}}
        """;
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal("Microsoft Haruka Desktop", settings.Voice!.VoiceName);
        Assert.Equal(2, settings.Voice.Rate);
        Assert.Equal(400, settings.Voice.MaxChars); // 書かない項目は標準
        Assert.False(settings.Voice.AutoRead);
    }
}
