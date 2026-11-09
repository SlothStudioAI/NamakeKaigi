using System.IO;
using System.Text.Json;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>
/// CLIから最後に取れたモデルの一覧(名前と表示名)を、ファイルに残す。
/// 起動直後や、回線が落ちて一覧が取れないときも、表示名つきの一覧で欄を作れるようにするため。
/// </summary>
public static class ModelListCache
{
    /// <summary>agy のモデル一覧を残すファイルの名前(%APPDATA%\NamakeKaigi の下)。</summary>
    public const string AgyFileName = "agy-models.json";

    /// <summary>Codex(チャッピー)のモデル一覧を残すファイルの名前(%APPDATA%\NamakeKaigi の下)。</summary>
    public const string CodexFileName = "codex-models.json";

    /// <summary>Codex(チャッピー)のモデル一覧を残す、標準の場所。</summary>
    /// <returns>ファイルのフルパス</returns>
    public static string DefaultCodexPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            SettingsLoader.AppDataFolderName, CodexFileName);

    /// <summary>agy のモデル一覧を残す、標準の場所。</summary>
    /// <returns>ファイルのフルパス</returns>
    public static string DefaultAgyPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            SettingsLoader.AppDataFolderName, AgyFileName);

    /// <summary>残してある一覧を読む。</summary>
    /// <param name="path">ファイルのパス(nullなら読まない)</param>
    /// <returns>一覧。ない・壊れているときは空</returns>
    public static IReadOnlyList<ModelOption> Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Array.Empty<ModelOption>();
        try
        {
            var list = JsonSerializer.Deserialize<List<ModelOption>>(File.ReadAllText(path));
            return list?.Where(o => !string.IsNullOrWhiteSpace(o.Id)).ToList() ?? (IReadOnlyList<ModelOption>)Array.Empty<ModelOption>();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return Array.Empty<ModelOption>(); // 読めなければ、設定にある一覧を使う
        }
    }

    /// <summary>一覧を残す。書けなくても会議は続けられるので、失敗は無視する。</summary>
    /// <param name="path">ファイルのパス(nullなら残さない)</param>
    /// <param name="options">一覧</param>
    public static void Save(string? path, IReadOnlyList<ModelOption> options)
    {
        if (string.IsNullOrEmpty(path) || options.Count == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 次に取れたときに、また書く
        }
    }
}
