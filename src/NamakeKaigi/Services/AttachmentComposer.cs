using System.IO;
using System.Text;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>添付されたファイル・フォルダから、画面・ログ用の表示と、AIに渡す情報(--add-dir や中身)を作る。</summary>
public static class AttachmentComposer
{
    private const int MaxFilePreviewChars = 6000;
    private const int MaxDirectoryEntries = 40;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".json", ".cs", ".xaml", ".xml", ".yml", ".yaml", ".py", ".js", ".ts",
        ".html", ".css", ".sql", ".ps1", ".bat", ".cmd", ".sh", ".csv", ".ini", ".config", ".sln", ".csproj",
    };

    /// <summary>画面の吹き出しや会議ログ(log.md)に残す文字列を作る(絶対パスを明記する)。</summary>
    /// <param name="text">入力された本文</param>
    /// <param name="attachments">添付の一覧</param>
    /// <returns>画面・ログ用の本文</returns>
    public static string FormatForDisplayAndLog(string text, IReadOnlyList<AttachmentItem> attachments)
    {
        if (attachments.Count == 0) return text.Trim();
        var sb = new StringBuilder();
        foreach (var a in attachments)
        {
            sb.AppendLine($"[添付{(a.IsDirectory ? "フォルダ" : "ファイル")}: {a.FullPath}]");
        }
        if (!string.IsNullOrWhiteSpace(text))
        {
            sb.AppendLine();
            sb.Append(text.Trim());
        }
        return sb.ToString().Trim();
    }

    /// <summary>AIに渡す本文を作る。絶対パスに加え、小さなテキストファイルやフォルダ一覧はそのまま添えて確実に読めるようにする。</summary>
    /// <param name="text">入力された本文</param>
    /// <param name="attachments">添付の一覧</param>
    /// <returns>AIに送る本文</returns>
    public static string FormatForPrompt(string text, IReadOnlyList<AttachmentItem> attachments)
    {
        if (attachments.Count == 0) return text.Trim();
        var sb = new StringBuilder();
        foreach (var a in attachments)
        {
            sb.AppendLine($"[添付{(a.IsDirectory ? "フォルダ" : "ファイル")}: {a.FullPath}]");
            if (a.IsDirectory && Directory.Exists(a.FullPath))
            {
                try
                {
                    var entries = Directory.EnumerateFileSystemEntries(a.FullPath)
                        .Take(MaxDirectoryEntries + 1)
                        .Select(p => Path.GetFileName(p) + (Directory.Exists(p) ? "/" : ""))
                        .ToList();
                    if (entries.Count > 0)
                    {
                        var shown = entries.Take(MaxDirectoryEntries);
                        var more = entries.Count > MaxDirectoryEntries ? " ほか…" : "";
                        sb.AppendLine($"フォルダの中身: {string.Join(", ", shown)}{more}");
                    }
                }
                catch (Exception) { /* 読めないフォルダはパスだけ渡す */ }
            }
            else if (File.Exists(a.FullPath))
            {
                var ext = Path.GetExtension(a.FullPath);
                if (TextExtensions.Contains(ext))
                {
                    try
                    {
                        var content = File.ReadAllText(a.FullPath);
                        if (content.Length > MaxFilePreviewChars)
                            content = content[..MaxFilePreviewChars] + "\n…(長いため省略。続きが必要なら Read ツールで読んでください)";
                        sb.AppendLine("```");
                        sb.AppendLine(content.TrimEnd());
                        sb.AppendLine("```");
                    }
                    catch (Exception) { /* 読めない場合はパスだけ渡す */ }
                }
            }
        }

        sb.AppendLine();
        sb.Append(string.IsNullOrWhiteSpace(text) ? "添付されたファイル・フォルダを確認してください。" : text.Trim());
        return sb.ToString().Trim();
    }

    /// <summary>添付されたパスのうち、作業フォルダの外にあるものの親フォルダ一覧(--add-dir に渡す用)を取り出す。</summary>
    /// <param name="workDir">いまの作業フォルダ</param>
    /// <param name="attachments">添付の一覧</param>
    /// <returns>追加で許可するフォルダの並び(絶対パス、区切りは /)</returns>
    public static IReadOnlyList<string> ResolveExternalDirectories(string workDir, IReadOnlyList<AttachmentItem> attachments)
    {
        if (attachments.Count == 0) return Array.Empty<string>();
        var normalizedWorkDir = string.IsNullOrWhiteSpace(workDir)
            ? ""
            : Path.GetFullPath(workDir).Replace('\\', '/').TrimEnd('/') + "/";

        var dirs = new List<string>();
        foreach (var a in attachments)
        {
            var full = Path.GetFullPath(a.FullPath).Replace('\\', '/');
            if (normalizedWorkDir.Length > 0 &&
                (string.Equals(full.TrimEnd('/'), normalizedWorkDir.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
                 full.StartsWith(normalizedWorkDir, StringComparison.OrdinalIgnoreCase)))
            {
                continue; // 作業フォルダの中なら --add-dir は不要
            }

            var targetDir = a.IsDirectory ? full.TrimEnd('/') : Path.GetDirectoryName(full)?.Replace('\\', '/')?.TrimEnd('/');
            if (!string.IsNullOrEmpty(targetDir) && !dirs.Contains(targetDir, StringComparer.OrdinalIgnoreCase))
            {
                dirs.Add(targetDir);
            }
        }
        return dirs;
    }
}
