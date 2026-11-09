using NamakeKaigi.Models;
using NamakeKaigi.ViewModels;

namespace NamakeKaigi.Tests;

/// <summary>発言の一覧4つと、その並べ方(MessageLists)の試験。セッションも画面もなしで試せる。</summary>
public class MessageListsTests
{
    private static MessageViewModel Message(Participant speaker, string text, bool streaming = false,
        IReadOnlyList<string>? targets = null) =>
        new(speaker, text, DateTime.Now, targetIds: targets) { IsStreaming = streaming };

    [Fact]
    public void AIの発言は_まとめて見る一覧と_その人の列だけに載る()
    {
        var lists = new MessageLists();
        var claude = Message(Participants.Claude, "こんにちは");

        lists.Insert(claude);

        Assert.Single(lists.All);
        Assert.Single(lists.Claude);
        Assert.Empty(lists.Gemini);
        Assert.Empty(lists.Chappy);
    }

    [Fact]
    public void あなたの発言は_宛先の列すべてに載る()
    {
        var lists = new MessageLists();
        var user = Message(Participants.User, "みんなへ", targets: new[] { Participants.ClaudeId, Participants.ChappyId });

        lists.Insert(user);

        Assert.Single(lists.All);
        Assert.Single(lists.Claude);
        Assert.Empty(lists.Gemini);
        Assert.Single(lists.Chappy);
    }

    [Fact]
    public void 流れている発言は末尾に置き_済んだ発言はその上に入る()
    {
        var lists = new MessageLists();
        var streaming = Message(Participants.Claude, "途中", streaming: true);
        lists.Insert(streaming);

        var done = Message(Participants.Gemini, "済み");
        lists.Insert(done);

        Assert.Equal(new[] { done, streaming }, lists.All);
    }

    [Fact]
    public void 流れ終わった発言は_まだ流れている発言より上へ動く()
    {
        var lists = new MessageLists();
        var first = Message(Participants.Claude, "先に始まった", streaming: true);
        var second = Message(Participants.Claude, "あとから始まった", streaming: true);
        lists.Insert(first);
        lists.Insert(second);

        second.IsStreaming = false;
        lists.MoveAboveStreaming(second);

        Assert.Equal(new[] { second, first }, lists.All);
        Assert.Equal(new[] { second, first }, lists.Claude);
    }

    [Fact]
    public void 外すと_すべての一覧から消える()
    {
        var lists = new MessageLists();
        var user = Message(Participants.User, "宛先2人", targets: new[] { Participants.ClaudeId, Participants.GeminiId });
        lists.Insert(user);

        lists.Remove(user);

        Assert.Empty(lists.All);
        Assert.Empty(lists.Claude);
        Assert.Empty(lists.Gemini);
    }

    [Fact]
    public void 空にすると_4つの一覧すべてが空になる()
    {
        var lists = new MessageLists();
        lists.Insert(Message(Participants.Claude, "a"));
        lists.Insert(Message(Participants.Gemini, "b"));
        lists.Insert(Message(Participants.Chappy, "c"));

        lists.Clear();

        Assert.Empty(lists.All);
        Assert.Empty(lists.Claude);
        Assert.Empty(lists.Gemini);
        Assert.Empty(lists.Chappy);
    }
}
