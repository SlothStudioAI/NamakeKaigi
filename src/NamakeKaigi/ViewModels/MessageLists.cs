using System.Collections.ObjectModel;
using NamakeKaigi.Models;

namespace NamakeKaigi.ViewModels;

/// <summary>
/// 発言の一覧4つ(まとめて見る一覧と、クローディ・ジェミリー・チャッピーの列)と、その並べ方。
/// 流れている最中の吹き出しは末尾に置き、済んだ吹き出しはそれより上に入れる(済んだ順に並ぶように)。
/// セッションや見た目は知らない(吹き出しへの見た目の反映などは、MainViewModel が行う)。
/// </summary>
public sealed class MessageLists
{
    /// <summary>全員の発言を時系列に並べた一覧(まとめて見る画面)。</summary>
    public ObservableCollection<MessageViewModel> All { get; } = new();

    /// <summary>クローディとのやりとりだけの一覧(別々に見る画面の左の列)。</summary>
    public ObservableCollection<MessageViewModel> Claude { get; } = new();

    /// <summary>ジェミリーとのやりとりだけの一覧(別々に見る画面の右の列)。</summary>
    public ObservableCollection<MessageViewModel> Gemini { get; } = new();

    /// <summary>チャッピーとのやりとりだけの一覧(別々に見る画面の3つ目の列)。</summary>
    public ObservableCollection<MessageViewModel> Chappy { get; } = new();

    /// <summary>吹き出しを、載るべき一覧すべてに入れる。返答途中なら末尾に、それ以外は返答途中の吹き出しの上に入れる。</summary>
    /// <param name="message">入れる吹き出し</param>
    public void Insert(MessageViewModel message)
    {
        foreach (var list in ListsOf(message)) InsertAboveStreaming(list, message);
    }

    /// <summary>吹き出しを、すべての一覧から外す。</summary>
    /// <param name="message">外す吹き出し</param>
    public void Remove(MessageViewModel message)
    {
        foreach (var list in ListsOf(message)) list.Remove(message);
    }

    /// <summary>返答が済んだ吹き出しを、まだ返答途中の吹き出しより上へ動かす(済んだ順に並ぶように)。</summary>
    /// <param name="message">動かす吹き出し</param>
    public void MoveAboveStreaming(MessageViewModel message)
    {
        foreach (var list in ListsOf(message))
        {
            var current = list.IndexOf(message);
            var firstStreaming = FirstStreamingIndex(list, except: message);
            if (current >= 0 && firstStreaming < current) list.Move(current, firstStreaming);
        }
    }

    /// <summary>4つの一覧をすべて空にする。</summary>
    public void Clear()
    {
        All.Clear();
        Claude.Clear();
        Gemini.Clear();
        Chappy.Clear();
    }

    /// <summary>その吹き出しが載る一覧(まとめて見る一覧と、別々に見る列)。</summary>
    private IEnumerable<ObservableCollection<MessageViewModel>> ListsOf(MessageViewModel message)
    {
        yield return All;
        if (message.IsUser)
        {
            foreach (var column in message.TargetIds.Select(Column).OfType<ObservableCollection<MessageViewModel>>()) yield return column;
        }
        else
        {
            if (Column(message.Speaker.Id) is { } column) yield return column;
        }
    }

    /// <summary>吹き出しを一覧に入れる。返答途中なら末尾に、それ以外は返答途中の吹き出しの上に入れる。</summary>
    private static void InsertAboveStreaming(ObservableCollection<MessageViewModel> list, MessageViewModel message)
    {
        if (message.IsStreaming) list.Add(message);
        else list.Insert(FirstStreamingIndex(list, except: null), message);
    }

    /// <summary>一覧の中で、最初の返答途中の吹き出しの位置(なければ末尾)。</summary>
    private static int FirstStreamingIndex(IList<MessageViewModel> list, MessageViewModel? except)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].IsStreaming && !ReferenceEquals(list[i], except)) return i;
        }
        return list.Count;
    }

    /// <summary>発言者の列(いない発言者ならnull)。</summary>
    private ObservableCollection<MessageViewModel>? Column(string agentId) =>
        agentId == Participants.ClaudeId ? Claude
        : agentId == Participants.GeminiId ? Gemini
        : agentId == Participants.ChappyId ? Chappy
        : null;
}
