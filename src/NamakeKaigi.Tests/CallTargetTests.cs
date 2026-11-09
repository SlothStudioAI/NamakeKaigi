using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>発言の先頭の呼びかけ(「ジェミ、〜」)で宛先を切り替える機能(WBS 11.34)の試験。</summary>
public class CallTargetTests
{
    private static readonly CallNameSettings Names = CallNameSettings.Default;

    [Theory]
    [InlineData("ジェミ、これ見て", Participants.GeminiId, "これ見て")]
    [InlineData("クロ、続きお願い", Participants.ClaudeId, "続きお願い")]
    [InlineData("クローディ、続きお願い", Participants.ClaudeId, "続きお願い")]
    [InlineData("チャッピー、どう思う？", Participants.ChappyId, "どう思う？")]
    [InlineData("全員、意見ちょうだい", CallParser.EveryoneKey, "意見ちょうだい")]
    [InlineData("みんな 聞いて", CallParser.EveryoneKey, "聞いて")]
    [InlineData("ジェミ,英語で", Participants.GeminiId, "英語で")]
    [InlineData("ジェミ　全角空白で", Participants.GeminiId, "全角空白で")]
    [InlineData("  ジェミ、 前後に空白  ", Participants.GeminiId, "前後に空白")]
    public void 先頭の呼び名と区切りで宛先が決まり_呼びかけを除いた本文になる(string text, string key, string body)
    {
        var result = CallParser.Parse(text, Names);

        Assert.True(result.IsCalled);
        Assert.Equal(key, result.TargetKey);
        Assert.Equal(body, result.Body);
    }

    [Theory]
    [InlineData("ジェミリー、確認", Participants.GeminiId)]
    [InlineData("ゼミ、これ見て", Participants.GeminiId)]    // 音声入力の誤変換
    [InlineData("ジェミー、これ見て", Participants.GeminiId)] // 音声入力の誤変換
    public void 長い呼び名や誤変換の呼び名も_呼びかけとして扱う(string text, string key)
    {
        Assert.Equal(key, CallParser.Parse(text, Names).TargetKey);
    }

    [Theory]
    [InlineData("ジェミリーは天才だ")]            // 区切りがない(本文の中の名前)
    [InlineData("これはジェミ、の話")]              // 先頭ではない
    [InlineData("クロとジェミ、これ見て")]          // 2人を並べた呼びかけは扱わない
    [InlineData("クロスワードの話")]                // 「クロ」で始まるだけ
    [InlineData("ジェミ")]                          // 区切りも本文もない
    [InlineData("")]
    public void 呼びかけでない発言は_そのまま変えない(string text)
    {
        var result = CallParser.Parse(text, Names);

        Assert.False(result.IsCalled);
        Assert.Equal(text.Trim(), result.Body);
    }

    [Fact]
    public void 呼びかけだけで本文が空なら_本文は空になる()
    {
        var result = CallParser.Parse("ジェミ、", Names);

        Assert.Equal(Participants.GeminiId, result.TargetKey);
        Assert.Equal("", result.Body);
    }

    [Fact]
    public void 設定で呼び名を足せる_足さない宛先は標準のまま()
    {
        var names = new CallNameSettings(Claude: new[] { "黒" });

        Assert.Equal(Participants.ClaudeId, CallParser.Parse("黒、お願い", names).TargetKey);
        Assert.False(CallParser.Parse("クロ、お願い", names).IsCalled); // 設定した宛先は、標準の名前を置き換える
        Assert.Equal(Participants.GeminiId, CallParser.Parse("ジェミ、お願い", names).TargetKey);
    }

    [Fact]
    public void 送信すると_宛先が切り替わり_AIには呼びかけを除いた本文が届き_画面には言った言葉が残る()
    {
        var session = new FakeSession { Script = _ => new[] { new ChatProgress(Participants.GeminiId, ChatProgressKind.Done, "了解") } };
        var vm = new MainViewModel(() => session) { Target = ChatTarget.Claude };

        vm.InputText = "ジェミ、これ見て";
        vm.SendAsync().GetAwaiter().GetResult();

        Assert.Equal(ChatTarget.Gemini, vm.Target);
        var sent = Assert.Single(session.Sent);
        Assert.Equal("これ見て", sent.Text);
        Assert.Equal(new[] { Participants.GeminiId }, sent.Targets);
        Assert.Equal("ジェミ、これ見て", vm.Messages.First(m => m.Speaker.Id == Participants.UserId).Text);
        Assert.Equal("", vm.InputText);
    }

    [Fact]
    public void 呼びかけがなければ_宛先は今のまま()
    {
        var session = new FakeSession { Script = _ => Array.Empty<ChatProgress>() };
        var vm = new MainViewModel(() => session) { Target = ChatTarget.Claude };

        vm.InputText = "普通の発言です";
        vm.SendAsync().GetAwaiter().GetResult();

        Assert.Equal(ChatTarget.Claude, vm.Target);
        Assert.Equal("普通の発言です", Assert.Single(session.Sent).Text);
    }

    [Fact]
    public void 呼びかけだけなら_宛先を切り替えるだけで_何も送らない()
    {
        var session = new FakeSession { Script = _ => Array.Empty<ChatProgress>() };
        var vm = new MainViewModel(() => session) { Target = ChatTarget.Claude };

        vm.InputText = "ジェミ、";
        vm.SendAsync().GetAwaiter().GetResult();

        Assert.Equal(ChatTarget.Gemini, vm.Target);
        Assert.Empty(session.Sent);
        Assert.Equal("", vm.InputText);
    }

    [Fact]
    public void 全員と呼べば_全員宛てになる()
    {
        var session = new FakeSession { Script = _ => Array.Empty<ChatProgress>() };
        var vm = new MainViewModel(() => session) { Target = ChatTarget.Gemini };

        vm.InputText = "全員、意見ちょうだい";
        vm.SendAsync().GetAwaiter().GetResult();

        Assert.Equal(ChatTarget.Everyone, vm.Target);
        Assert.Equal("意見ちょうだい", Assert.Single(session.Sent).Text);
    }

    [Fact]
    public void 会議の設定の呼び名が使われる()
    {
        var session = new FakeSession
        {
            CallNames = new CallNameSettings(Gemini: new[] { "ジェム" }),
            Script = _ => Array.Empty<ChatProgress>(),
        };
        var vm = new MainViewModel(() => session) { Target = ChatTarget.Claude };

        vm.InputText = "ジェム、お願い";
        vm.SendAsync().GetAwaiter().GetResult();

        Assert.Equal(ChatTarget.Gemini, vm.Target);
    }

    [Fact]
    public void 設定ファイルの_callNames_を読める_書かなければ標準()
    {
        var json = """
        {"claude":{"executable":"claude","model":"sonnet","extraArgs":[]},
         "agy":{"executable":"agy","extraArgs":[],"noToolNote":""},
         "workDir":"C:/x","meetingsDir":"C:/y","maxInjectChars":4000,"timeoutSec":600,
         "callNames":{"gemini":["ジェム","ジェミ"]}}
        """;
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(new[] { "ジェム", "ジェミ" }, settings.CallNames!.GeminiNames);
        Assert.Equal(CallNameSettings.DefaultClaude, settings.CallNames.ClaudeNames);
    }
}
