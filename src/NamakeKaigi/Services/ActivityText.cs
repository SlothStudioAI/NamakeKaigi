using System.IO;
using System.Text.Json;

namespace NamakeKaigi.Services;

/// <summary>AIが使っている道具(ファイルを読む等)を、人が読める「いまの作業」の文にする。</summary>
public static class ActivityText
{
    private const int MaxTargetChars = 30;

    // 対象(ファイル名・検索語・サブエージェント名など)が入っていそうな引数の名前(先に見つかったものを使う)
    private static readonly string[] TargetKeys =
    {
        "file_path", "path", "Path", "AbsolutePath", "TargetFile", "FilePath", "File",
        "pattern", "Pattern", "Query", "query", "url", "Url",
        "subagent_type", "TypeName", "Role", "description",
    };

    /// <summary>道具の名前と対象から、「いまの作業」の文を作る。</summary>
    /// <param name="toolName">道具の名前(Read、read_file など)</param>
    /// <param name="target">対象(ファイルのパス・検索語。なければnull)</param>
    /// <returns>例: 「AI_MEMORY.md を読んでいます」</returns>
    public static string Describe(string toolName, string? target)
    {
        var name = toolName.ToLowerInvariant();
        var shown = Shorten(target);
        if (HasAny(name, "agent", "task", "subagent"))
            return shown is null ? "サブエージェントを呼んでいます" : $"サブエージェント({shown})を呼んでいます";
        if (HasAny(name, "read", "view", "cat"))
            return shown is null ? "ファイルを読んでいます" : $"{shown} を読んでいます";
        if (HasAny(name, "edit", "write", "replace", "patch"))
            return shown is null ? "ファイルを書き換えています" : $"{shown} を書き換えています";
        if (HasAny(name, "grep", "glob", "find", "search", "list", "ls"))
            return shown is null ? "探しています" : $"{shown} を探しています";
        if (HasAny(name, "powershell", "bash", "shell", "run", "command"))
            return "コマンドを実行しています";
        return $"{toolName} を使っています";
    }

    // 「〜しています」を「〜しました」にするための語尾の対応(長いものを先に書く)
    private static readonly (string Present, string Past)[] Endings =
    {
        ("を呼んでいます", "を呼びました"),
        ("を読んでいます", "を読みました"),
        ("を書き換えています", "を書き換えました"),
        ("を探しています", "を探しました"),
        ("コマンドを実行しています", "コマンドを実行しました"),
        ("を使っています", "を使いました"),
        ("探しています", "探しました"),
    };

    /// <summary>「〜しています」の文を、終わったあとの「〜しました」にする。</summary>
    /// <param name="activity">Describe が作った文(空なら空のまま)</param>
    /// <returns>過去形の文</returns>
    public static string ToPast(string activity)
    {
        foreach (var (present, past) in Endings)
            if (activity.EndsWith(present, StringComparison.Ordinal))
                return activity[..^present.Length] + past;
        return activity;
    }

    /// <summary>道具の引数(JSONの入れ物)から、対象になりそうな文字列を取り出す。</summary>
    /// <param name="parameters">引数の入れ物(オブジェクトでなければnull扱い)</param>
    /// <returns>対象(見つからなければnull)</returns>
    public static string? FindTarget(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in TargetKeys)
            if (parameters.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        return null;
    }

    private static bool HasAny(string name, params string[] words) => words.Any(name.Contains);

    /// <summary>パスならファイル名だけにし、長ければ切る。</summary>
    private static string? Shorten(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var text = target.Contains('\\') || target.Contains('/') ? Path.GetFileName(target.TrimEnd('\\', '/')) : target;
        if (text.Length == 0) text = target;
        return text.Length > MaxTargetChars ? text[..MaxTargetChars] + "…" : text;
    }
}
