using System.IO;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>
/// フォルダの中から、AIごとのメンバー定義ファイルを拾い上げて、名簿にする。画面を知らない。
/// クロ(Claude)は <c>.claude/agents/*.md</c>、ジェミ(Gemini)は <c>.agents/skills/*/SKILL.md</c>、
/// チャッピー(Codex)は <c>.codex/agents/*.toml</c> が置き場所。
/// </summary>
public static class RosterScanner
{
    /// <summary>グループの識別名: クロ(Claude)。</summary>
    public const string ClaudeGroup = "claude";

    /// <summary>グループの識別名: ジェミ(Gemini)。</summary>
    public const string GeminiGroup = "gemini";

    /// <summary>グループの識別名: チャッピー(Codex)。</summary>
    public const string CodexGroup = "codex";

    /// <summary>個人用の置き場所(ユーザーのホームの下)のチーム名。</summary>
    public const string UserTeamName = "個人用";

    private const string SharedTeamName = "共通";

    // 個人用の定義は、作業フォルダに同じ識別名があれば、そちらを優先する(CLIも作業フォルダの定義を優先するため)
    private const int UserDepth = int.MaxValue;

    /// <summary>設定の起点フォルダ(と、個人用の置き場所)から、メンバーを拾い上げる。</summary>
    /// <param name="settings">名簿の設定</param>
    /// <param name="workDir">作業フォルダ(設定に起点がないとき、ここから探す)</param>
    /// <param name="userHome">ユーザーのホーム(個人用の置き場所を探すとき。null なら探さない)</param>
    /// <returns>メンバーの一覧(グループ・チーム・識別名の順)</returns>
    public static IReadOnlyList<RosterMember> Scan(RosterSettings settings, string workDir, string? userHome = null) =>
        Scan(settings.ResolveRoots(workDir), settings, settings.IncludeUserDirs ? userHome : null);

    /// <summary>指定したフォルダから、メンバーを拾い上げる。同じグループで同じ識別名のものは、いちばん浅い場所の1件にまとめる。</summary>
    /// <param name="roots">探し始めるフォルダ</param>
    /// <param name="settings">名簿の設定(除外フォルダ・深さ)</param>
    /// <param name="userHome">ユーザーのホーム(個人用の置き場所を探すとき。null なら探さない)</param>
    /// <returns>メンバーの一覧(グループ・チーム・識別名の順)</returns>
    public static IReadOnlyList<RosterMember> Scan(IReadOnlyList<string> roots, RosterSettings settings, string? userHome = null)
    {
        var exclude = new HashSet<string>(settings.ExcludeDirs ?? RosterSettings.DefaultExcludeDirs,
            StringComparer.OrdinalIgnoreCase);
        var found = new List<(RosterMember Member, int Depth)>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            Walk(root, root, 0, settings.MaxDepth, exclude, found);
        }
        if (!string.IsNullOrEmpty(userHome) && Directory.Exists(userHome))
        {
            // ホームの下は深く探さず、決まった3か所だけを見る
            AddClaudeMembers(Path.Combine(userHome, ".claude"), UserTeamName, UserDepth, found);
            AddGeminiMembers(Path.Combine(userHome, ".agents"), UserTeamName, UserDepth, found);
            AddCodexMembers(Path.Combine(userHome, ".codex"), UserTeamName, UserDepth, found);
        }

        // 公開用のコピーなどを重ねて数えないよう、同じグループ・同じ識別名は浅い場所の1件にする
        return found
            .GroupBy(f => (f.Member.Group, f.Member.Name))
            .Select(g => g.OrderBy(f => f.Depth).ThenBy(f => f.Member.SourcePath, StringComparer.Ordinal).First().Member)
            .OrderBy(m => m.Group, StringComparer.Ordinal)
            .ThenBy(m => m.Team, StringComparer.Ordinal)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>定義ファイルの文章から、メンバーを1人分読み取る。先頭の `---` で囲まれた部分が見出し、その後ろが行動ルール。</summary>
    /// <param name="text">定義ファイルの全文</param>
    /// <param name="group">所属グループの識別名</param>
    /// <param name="team">置かれているチームの場所</param>
    /// <param name="sourcePath">定義ファイルのフルパス</param>
    /// <param name="fallbackName">定義に name がないときの識別名</param>
    /// <returns>メンバー</returns>
    public static RosterMember ParseDefinition(string text, string group, string team, string sourcePath, string fallbackName)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bodyStart = 0;

        if (lines.Length > 0 && lines[0].TrimEnd('﻿').Trim() == "---")
        {
            var i = 1;
            for (; i < lines.Length && lines[i].Trim() != "---"; i++)
            {
                var line = lines[i];
                var colon = line.IndexOf(':');
                if (colon <= 0 || char.IsWhiteSpace(line[0])) continue; // 字下げの行(リストの中身など)は見出しの項目ではない

                var key = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (value is ">-" or ">" or "|" or "|-")
                {
                    // 複数行の書き方: 次の字下げの行を、1つの文章につなぐ
                    var parts = new List<string>();
                    while (i + 1 < lines.Length && (lines[i + 1].Length == 0 || char.IsWhiteSpace(lines[i + 1][0])))
                    {
                        i++;
                        if (lines[i].Trim().Length > 0) parts.Add(lines[i].Trim());
                    }
                    value = string.Join(" ", parts);
                }
                else
                {
                    value = value.Trim('"', '\'');
                }
                if (value.Length > 0) fields[key] = value;
            }
            bodyStart = Math.Min(i + 1, lines.Length);
        }

        var name = fields.GetValueOrDefault("name") ?? fallbackName;
        var rules = string.Join("\n", lines.Skip(bodyStart)).Trim();
        return new RosterMember(
            group, team, name,
            fields.GetValueOrDefault("displayName") ?? name,
            fields.GetValueOrDefault("description") ?? "",
            fields.GetValueOrDefault("model"),
            rules, sourcePath);
    }

    /// <summary>
    /// チャッピー(Codex)の定義ファイル(TOML)から、メンバーを1人分読み取る。
    /// 読むのは name・description・model・developer_instructions(行動ルール)だけ。表示名の項目はないので、識別名を表示名にする。
    /// </summary>
    /// <param name="text">定義ファイルの全文</param>
    /// <param name="team">置かれているチームの場所</param>
    /// <param name="sourcePath">定義ファイルのフルパス</param>
    /// <param name="fallbackName">定義に name がないときの識別名</param>
    /// <returns>メンバー</returns>
    public static RosterMember ParseCodexDefinition(string text, string team, string sourcePath, string fallbackName)
    {
        var fields = TomlStrings.Read(text.TrimStart('﻿').Replace("\r\n", "\n"));
        var name = fields.GetValueOrDefault("name") ?? fallbackName;
        var rules = (fields.GetValueOrDefault("developer_instructions") ?? "").Replace("\r", "").Trim();
        return new RosterMember(
            CodexGroup, team, name, name,
            fields.GetValueOrDefault("description") ?? "",
            fields.GetValueOrDefault("model"),
            rules, sourcePath);
    }

    private static void Walk(string root, string dir, int depth, int maxDepth, HashSet<string> exclude,
        List<(RosterMember Member, int Depth)> found)
    {
        var name = Path.GetFileName(dir);
        if (name == ".claude")
        {
            AddClaudeMembers(dir, TeamOf(root, dir), depth, found);
            return;
        }
        if (name == ".agents")
        {
            AddGeminiMembers(dir, TeamOf(root, dir), depth, found);
            return;
        }
        if (name == ".codex")
        {
            AddCodexMembers(dir, TeamOf(root, dir), depth, found);
            return;
        }
        if (depth >= maxDepth) return;

        List<string> children;
        try
        {
            children = Directory.EnumerateDirectories(dir).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // 読めないフォルダは飛ばす
        }
        foreach (var child in children)
        {
            if (exclude.Contains(Path.GetFileName(child))) continue;
            Walk(root, child, depth + 1, maxDepth, exclude, found);
        }
    }

    private static void AddClaudeMembers(string claudeDir, string team, int depth, List<(RosterMember, int)> found)
    {
        var agentsDir = Path.Combine(claudeDir, "agents");
        if (!Directory.Exists(agentsDir)) return;
        foreach (var file in SafeFiles(agentsDir, "*.md")) // 直下だけ(_archive などの中は拾わない)
        {
            TryAdd(file, ClaudeGroup, team, Path.GetFileNameWithoutExtension(file), depth, found);
        }
    }

    private static void AddGeminiMembers(string agentsDir, string team, int depth, List<(RosterMember, int)> found)
    {
        var skillsDir = Path.Combine(agentsDir, "skills");
        if (!Directory.Exists(skillsDir)) return;
        foreach (var skillDir in SafeDirs(skillsDir))
        {
            var file = Path.Combine(skillDir, "SKILL.md");
            if (File.Exists(file)) TryAdd(file, GeminiGroup, team, Path.GetFileName(skillDir), depth, found);
        }
    }

    private static void AddCodexMembers(string codexDir, string team, int depth, List<(RosterMember, int)> found)
    {
        var agentsDir = Path.Combine(codexDir, "agents");
        if (!Directory.Exists(agentsDir)) return;
        foreach (var file in SafeFiles(agentsDir, "*.toml")) // 直下だけ
        {
            try
            {
                found.Add((ParseCodexDefinition(File.ReadAllText(file), team, file, Path.GetFileNameWithoutExtension(file)), depth));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 読めない定義は載せない
            }
        }
    }

    private static void TryAdd(string file, string group, string team, string fallbackName, int depth,
        List<(RosterMember, int)> found)
    {
        try
        {
            found.Add((ParseDefinition(File.ReadAllText(file), group, team, file, fallbackName), depth));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 読めない定義は載せない
        }
    }

    /// <summary>定義が置かれているチーム(`.claude`・`.agents`・`.codex` の1つ上のフォルダ)を、起点からの相対パスで返す。</summary>
    private static string TeamOf(string root, string hiddenDir)
    {
        var parent = Path.GetDirectoryName(hiddenDir) ?? root;
        var relative = Path.GetRelativePath(root, parent);
        return relative == "." ? SharedTeamName : relative;
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
