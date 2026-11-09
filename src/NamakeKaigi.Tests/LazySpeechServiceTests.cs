using NamakeKaigi.Models;
using NamakeKaigi.Services;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>読み上げの準備を使うときまで待つ包み(WBS 11.51)の試験。声そのものは鳴らさない。</summary>
public class LazySpeechServiceTests
{
    /// <summary>中身の代わりに、頼まれた文を記録するだけの偽物。</summary>
    private sealed class FakeInner : ISpeechService
    {
        public bool IsAvailable { get; set; } = true;
        public string UnavailableReason { get; set; } = "";
        public bool IsSpeaking => Spoken.Count > 0;
        public event Action? SpeakingChanged;
        public List<string> Spoken { get; } = new();
        public List<string> Speakers { get; } = new();
        public int StopCount { get; private set; }
        public void Enqueue(string text) { Spoken.Add(text); SpeakingChanged?.Invoke(); }
        public void Enqueue(string text, string speakerId) { Speakers.Add(speakerId); Enqueue(text); }
        public void Stop() { StopCount++; Spoken.Clear(); SpeakingChanged?.Invoke(); }
    }

    /// <summary>中身を作った回数を数える、作る関数。</summary>
    private sealed class Factory
    {
        public int Count { get; private set; }
        public FakeInner Inner { get; } = new();
        public Func<ISpeechService> Func => () => { Count++; return Inner; };
    }

    [Fact]
    public void 使うまでは中身を作らず_使えると見なす()
    {
        var factory = new Factory();
        var lazy = new LazySpeechService(factory.Func);

        Assert.Equal(0, factory.Count);
        Assert.False(lazy.IsCreated);
        Assert.True(lazy.IsAvailable);
        Assert.False(lazy.IsSpeaking);
        lazy.Stop(); // 作る前に止めても、中身は作らない
        Assert.Equal(0, factory.Count);
    }

    [Fact]
    public void 起動時にONの設定なら_すぐに中身を作る()
    {
        var factory = new Factory();
        var lazy = new LazySpeechService(factory.Func, eager: true);

        Assert.Equal(1, factory.Count);
        Assert.True(lazy.IsCreated);
    }

    [Fact]
    public void 準備を何度頼んでも_中身は1回しか作らない()
    {
        var factory = new Factory();
        var lazy = new LazySpeechService(factory.Func);

        lazy.Prepare();
        lazy.Prepare();
        lazy.Enqueue("こんにちは");

        Assert.Equal(1, factory.Count);
    }

    [Fact]
    public void 読み上げを頼むと_そこで中身を作って_話し手つきでも渡す()
    {
        var factory = new Factory();
        var lazy = new LazySpeechService(factory.Func);

        lazy.Enqueue("こんにちは", "claude");

        Assert.Equal(1, factory.Count);
        Assert.Equal(new[] { "こんにちは" }, factory.Inner.Spoken);
        Assert.Equal(new[] { "claude" }, factory.Inner.Speakers);
        Assert.True(lazy.IsSpeaking);
    }

    [Fact]
    public void 作ったあとは_止める頼みも中身に届く()
    {
        var factory = new Factory();
        var lazy = new LazySpeechService(factory.Func);
        lazy.Enqueue("こんにちは");

        lazy.Stop();

        Assert.Equal(1, factory.Inner.StopCount);
        Assert.False(lazy.IsSpeaking);
    }

    [Fact]
    public void 作って使えないと分かったら_状態の変化を知らせ_理由を返す()
    {
        var factory = new Factory();
        factory.Inner.IsAvailable = false;
        factory.Inner.UnavailableReason = "日本語の声が見つかりません。";
        var lazy = new LazySpeechService(factory.Func);
        var changed = 0;
        lazy.SpeakingChanged += () => changed++;

        lazy.Prepare();

        Assert.False(lazy.IsAvailable);
        Assert.Equal("日本語の声が見つかりません。", lazy.UnavailableReason);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void スイッチをONにして初めて準備し_使えなければONにならない()
    {
        var factory = new Factory();
        factory.Inner.IsAvailable = false;
        var changed = new List<string>();
        var controller = new ReadAloudController(new LazySpeechService(factory.Func), new VoiceSettings(), changed.Add);
        Assert.Equal(0, factory.Count); // 作っただけでは、準備しない

        controller.On = true;

        Assert.Equal(1, factory.Count);
        Assert.False(controller.On);
        Assert.False(controller.Available);
        Assert.Contains("ReadAloudLabel", changed); // 「使えません」の表示に直してもらう
    }

    [Fact]
    public void 使える声なら_スイッチをONにするとONになる()
    {
        var factory = new Factory();
        var controller = new ReadAloudController(new LazySpeechService(factory.Func), new VoiceSettings(), _ => { });

        controller.On = true;

        Assert.True(controller.On);
        Assert.Equal(1, factory.Count);
    }
}
