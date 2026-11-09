using System.Text.RegularExpressions;

namespace NamakeKaigi.Services;

/// <summary>
/// Claudeの外部連携(MCPサーバー。Gmail・カレンダーなど)の一覧を `claude mcp list` で取り、
/// 「何でもできる」のときに、ONにしていない連携を止める引数を組み立てる。
/// 連携は人によって増減するため、名前を直書きせず、毎回CLIから取る。
/// </summary>
public sealed class ConnectorCatalog
{
    // 一覧の行の形: 「claude.ai Gmail: https://… - ✔ Connected」(名前は、最初の「: 」の前)
    private static readonly Regex ListLine = new(@"^(?<name>[^:]+?): .+ - .+$", RegexOptions.Compiled);
    private static readonly Regex NotToolChar = new(@"[^A-Za-z0-9_-]", RegexOptions.Compiled);

    private readonly IProcessRunner _runner;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<string>? _cached;
    private DateTime _cachedAt;

    /// <summary>一覧の取り方を決めて作る。</summary>
    /// <param name="runner">プロセス実行</param>
    public ConnectorCatalog(IProcessRunner runner) => _runner = runner;

    /// <summary>`claude mcp list` の出力から、連携の名前を取り出す。</summary>
    /// <param name="lines">出力の行</param>
    /// <returns>連携の名前(出てきた順)</returns>
    public static IReadOnlyList<string> ParseList(IEnumerable<string> lines) =>
        lines.Select(l => ListLine.Match(l.Trim()))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>連携の名前を、道具の名前の頭(例: 「claude.ai Gmail」→「mcp__claude_ai_Gmail」)にする。</summary>
    /// <param name="name">連携の名前</param>
    /// <returns>連携の道具をまとめて指す名前</returns>
    public static string ToolPrefix(string name) => "mcp__" + NotToolChar.Replace(name.Trim(), "_");

    /// <summary>
    /// 「何でもできる」のときに足す、連携の引数を組み立てる。
    /// ONが1つもない、または一覧が分からないときは、連携をすべて読み込まない(--strict-mcp-config)。
    /// それ以外は、一覧のうちONにしていないものを --disallowedTools で止める(新しく増えた連携は、OFFのまま)。
    /// </summary>
    /// <param name="enabled">ONにする連携の名前</param>
    /// <param name="known">いまある連携の名前(取れなかったらnull)</param>
    /// <returns>足す引数</returns>
    public static IReadOnlyList<string> BuildArgs(IReadOnlyList<string>? enabled, IReadOnlyList<string>? known)
    {
        var on = (enabled ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        if (on.Count == 0 || known is null) return new[] { "--strict-mcp-config" };

        var off = known.Where(n => !on.Contains(n)).Select(ToolPrefix).ToList();
        if (off.Count == 0) return Array.Empty<string>();
        return new[] { "--disallowedTools" }.Concat(off).ToList();
    }

    /// <summary>いまある連携の一覧を取る。前に取ってから cacheSec 秒以内なら、その結果を使う。</summary>
    /// <param name="executable">claude の実行ファイル</param>
    /// <param name="workDir">作業フォルダ(フォルダごとの連携も含めるため)</param>
    /// <param name="timeout">取るときの制限時間</param>
    /// <param name="cacheSec">結果を使い回す秒数(0なら毎回取る)</param>
    /// <param name="ct">キャンセル</param>
    /// <returns>連携の名前。取れなかったらnull</returns>
    public async Task<IReadOnlyList<string>?> LoadAsync(string executable, string workDir, TimeSpan timeout,
        int cacheSec, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is not null && (DateTime.UtcNow - _cachedAt).TotalSeconds < cacheSec) return _cached;

            var lines = new List<string>();
            try
            {
                await _runner.RunLinesAsync(executable, new[] { "mcp", "list" }, workDir, timeout, lines.Add, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return null; // 取れない・時間切れのときは、呼ぶ側で連携をすべて止める
            }
            _cached = ParseList(lines);
            _cachedAt = DateTime.UtcNow;
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }
}
