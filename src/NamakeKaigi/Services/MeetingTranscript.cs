using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>会議の発言の一覧と、各AIが最後に見た位置を持つ(複数の処理から使われるので、ロックする)。</summary>
public sealed class MeetingTranscript
{
    private readonly List<ChatEntry> _entries = new();
    private readonly Dictionary<string, int> _seen = new();
    private readonly object _lock = new();

    /// <summary>発言を1件追加する。</summary>
    /// <param name="entry">発言</param>
    public void Add(ChatEntry entry)
    {
        lock (_lock) _entries.Add(entry);
    }

    /// <summary>いま記録されている発言一覧のコピーを返す。</summary>
    public List<ChatEntry> GetEntriesSnapshot()
    {
        lock (_lock) return _entries.ToList();
    }

    /// <summary>AIごとの既読位置のコピーを返す。</summary>
    public Dictionary<string, int> GetSeenSnapshot()
    {
        lock (_lock) return new Dictionary<string, int>(_seen);
    }

    /// <summary>保存されていた発言一覧と既読位置を復元する。</summary>
    /// <param name="entries">発言一覧</param>
    /// <param name="seen">AIごとの既読位置</param>
    public void Restore(IEnumerable<ChatEntry> entries, IReadOnlyDictionary<string, int>? seen = null)
    {
        lock (_lock)
        {
            _entries.Clear();
            _entries.AddRange(entries);
            _seen.Clear();
            if (seen is not null)
            {
                foreach (var (k, v) in seen) _seen[k] = Math.Clamp(v, 0, _entries.Count);
            }
        }
    }

    /// <summary>そのAIの既読位置を最初(0)に戻す(CLI側の会話が消えていて、過去のログから再開するときに使う)。</summary>
    /// <param name="agentId">AIの識別名</param>
    public void ResetSeen(string agentId)
    {
        lock (_lock) _seen[agentId] = 0;
    }

    /// <summary>そのAIがまだ見ていない発言(自分の発言は除く)を、上限つきの文章にして返す。既読にはしない。</summary>
    /// <remarks>
    /// 上限を超えるときは、短い発言は全文を残し、長い発言は先頭だけ残して「省略」の印を付ける
    /// (先頭には、添付のパスの行や質問があるため)。発言が多すぎて1件あたりの長さが足りないときは、古い発言から外す。
    /// </remarks>
    /// <param name="agentId">AIの識別名</param>
    /// <param name="maxChars">文章の上限(目安。省略の印を含めて収める)</param>
    /// <returns>(整形した未読発言, 既読にするときの位置)</returns>
    public (string Text, int UpTo) PeekUnseen(string agentId, int maxChars)
    {
        lock (_lock)
        {
            var from = _seen.GetValueOrDefault(agentId, 0);
            var items = _entries.Skip(from)
                .Where(e => e.SpeakerId != agentId)
                .Select(e => (Label: $"【{Participants.Get(e.SpeakerId).LogName}】", Body: e.Text.Trim()))
                .ToList();
            var text = Fit(items, maxChars);
            return (text, _entries.Count);
        }
    }

    /// <summary>省略したときに、発言の終わりに付ける印。</summary>
    private const string TruncatedNote = "…(長いため省略)";

    /// <summary>古い発言を外す前に、1件ぶんとして最低限残したい本文の長さ。</summary>
    private const int MinBodyShare = 200;

    /// <summary>発言の並びを、上限に収まる文章にする。</summary>
    /// <param name="items">(名札, 本文)の並び(古い順)</param>
    /// <param name="maxChars">文章の上限</param>
    /// <returns>整形した文章</returns>
    private static string Fit(List<(string Label, string Body)> items, int maxChars)
    {
        const string separator = "\n\n";
        var prefix = "";
        if (items.Sum(i => i.Label.Length + i.Body.Length) + Math.Max(0, items.Count - 1) * separator.Length > maxChars)
        {
            var droppedOld = false;
            while (items.Count > 1 && BodyBudget(items, maxChars) < items.Count * MinBodyShare)
            {
                items.RemoveAt(0);
                droppedOld = true;
            }
            if (droppedOld) prefix = "(古い発言は省略)" + separator;
            var cap = FindBodyCap(items.Select(i => i.Body.Length), Math.Max(BodyBudget(items, maxChars), 0));
            items = items.Select(i => i.Body.Length > cap ? (i.Label, CutHead(i.Body, cap) + TruncatedNote) : i).ToList();
        }
        return prefix + string.Join(separator, items.Select(i => i.Label + i.Body));
    }

    /// <summary>名札・区切り・省略の印を引いた、本文に使える長さ。</summary>
    private static int BodyBudget(List<(string Label, string Body)> items, int maxChars) =>
        maxChars - items.Sum(i => i.Label.Length) - Math.Max(0, items.Count - 1) * 2 - items.Count * TruncatedNote.Length;

    /// <summary>本文の合計が予算に収まるように、1件あたりの長さの上限を求める(短い発言は削らず、余りを長い発言で分ける)。</summary>
    private static int FindBodyCap(IEnumerable<int> lengths, int budget)
    {
        var sorted = lengths.OrderBy(x => x).ToList();
        var remaining = budget;
        for (var i = 0; i < sorted.Count; i++)
        {
            var share = remaining / (sorted.Count - i);
            if (sorted[i] > share) return share;
            remaining -= sorted[i];
        }
        return int.MaxValue;
    }

    /// <summary>本文の先頭から指定の長さを取り出す(サロゲートペアの途中では切らない)。</summary>
    private static string CutHead(string body, int length)
    {
        if (length <= 0) return "";
        if (length < body.Length && char.IsHighSurrogate(body[length - 1])) length--;
        return body[..length];
    }

    /// <summary>そのAIが、指定の位置まで見たことにする。</summary>
    /// <param name="agentId">AIの識別名</param>
    /// <param name="upTo">PeekUnseenが返した位置</param>
    public void MarkSeen(string agentId, int upTo)
    {
        lock (_lock) _seen[agentId] = Math.Max(upTo, _seen.GetValueOrDefault(agentId, 0));
    }
}
