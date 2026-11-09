using System.IO;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>
/// 起動時に、各AIのCLIがこのPCにあるかを確かめる(入っていないAIは、カードを出さない)。
/// 設定の場所に見つからなければ、PATHから名前で探し直す。
/// </summary>
public static class CliLocator
{
    /// <summary>PATHから探し直すときの、AIごとのCLIの名前。</summary>
    private static readonly IReadOnlyDictionary<string, string> FallbackNames = new Dictionary<string, string>
    {
        [Participants.ClaudeId] = "claude",
        [Participants.GeminiId] = "agy",
        [Participants.ChappyId] = "codex",
    };

    /// <summary>CLIの実行ファイルを探す。</summary>
    /// <param name="configured">設定にある場所(フルパス、または名前だけ)</param>
    /// <param name="fallbackName">設定の場所になかったとき、PATHから探す名前(省略可)</param>
    /// <param name="exists">ファイルがあるかを確かめる処理(試験で差し替える。省略時は実際のファイル)</param>
    /// <param name="resolve">名前からPATHを探す処理(試験で差し替える。省略時は ProcessRunner.ResolveExecutable)</param>
    /// <returns>見つかった実行ファイルのパス。見つからなければnull</returns>
    public static string? Find(string configured, string? fallbackName = null,
        Func<string, bool>? exists = null, Func<string, string>? resolve = null)
    {
        exists ??= File.Exists;
        resolve ??= ProcessRunner.ResolveExecutable;
        foreach (var name in new[] { configured, fallbackName })
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var path = resolve(name);
            if (Path.IsPathRooted(path) && exists(path)) return path;
        }
        return null;
    }

    /// <summary>
    /// 各AIのCLIを探し、見つかったAIの識別名を返す。見つかった場所は、設定に書き戻す(設定の場所になく、PATHで見つかったとき)。
    /// </summary>
    /// <param name="settings">設定</param>
    /// <param name="exists">ファイルがあるかを確かめる処理(試験用)</param>
    /// <param name="resolve">名前からPATHを探す処理(試験用)</param>
    /// <returns>見つかった場所に直した設定 と、見つかったAIの識別名</returns>
    public static (AppSettings Settings, IReadOnlyList<string> Installed) Detect(AppSettings settings,
        Func<string, bool>? exists = null, Func<string, string>? resolve = null)
    {
        var installed = new List<string>();
        string? Locate(string id, string configured)
        {
            var found = Find(configured, FallbackNames[id], exists, resolve);
            if (found is not null) installed.Add(id);
            return found;
        }

        var codex = settings.Codex ?? new CodexSettings();
        var claudePath = Locate(Participants.ClaudeId, settings.Claude.Executable);
        var agyPath = Locate(Participants.GeminiId, settings.Agy.Executable);
        var codexPath = Locate(Participants.ChappyId, codex.Executable);
        var updated = settings with
        {
            Claude = settings.Claude with { Executable = claudePath ?? settings.Claude.Executable },
            Agy = settings.Agy with { Executable = agyPath ?? settings.Agy.Executable },
            Codex = codex with { Executable = codexPath ?? codex.Executable },
        };
        return (updated, installed);
    }
}
