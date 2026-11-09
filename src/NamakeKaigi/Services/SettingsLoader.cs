using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>
/// 設定ファイルを読み書きする。
/// settings.json(exe の隣)は配布する既定の値で、アプリからは書き込まない。
/// ユーザーが変えた設定は settings.local.json(%APPDATA%\NamakeKaigi)に書き、読むときは既定の上に重ねる。
/// </summary>
public static class SettingsLoader
{
    /// <summary>ユーザーの設定を置くフォルダの名前(%APPDATA% の下)。</summary>
    public const string AppDataFolderName = "NamakeKaigi";

    /// <summary>ユーザーの設定ファイルの名前。</summary>
    public const string LocalFileName = "settings.local.json";

    /// <summary>ユーザーの設定ファイルの標準の場所(版を入れ替えても残るよう、exe の隣ではなく %APPDATA% に置く)。</summary>
    /// <returns>settings.local.json のフルパス</returns>
    public static string DefaultLocalPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataFolderName, LocalFileName);

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>設定ファイルを読んで、設定の値にする。</summary>
    /// <param name="path">settings.json のパス</param>
    /// <returns>設定</returns>
    public static AppSettings Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, Options)
               ?? throw new InvalidDataException($"設定ファイルが空です: {path}");
    }

    /// <summary>既定の設定に、ユーザーの設定を重ねて読む(ユーザーの設定ファイルがなければ、既定のまま)。</summary>
    /// <param name="defaultsPath">settings.json(既定の値)のパス</param>
    /// <param name="localPath">settings.local.json(ユーザーの設定)のパス</param>
    /// <returns>設定</returns>
    public static AppSettings Load(string defaultsPath, string localPath)
    {
        var root = ParseObject(File.ReadAllText(defaultsPath), defaultsPath);
        if (File.Exists(localPath)) Merge(root, ParseObject(File.ReadAllText(localPath), localPath));
        return root.Deserialize<AppSettings>(Options)
               ?? throw new InvalidDataException($"設定ファイルが空です: {defaultsPath}");
    }

    /// <summary>会議ログの保存先が空のときに使う、exe の隣のフォルダ名。</summary>
    public const string DefaultMeetingsDir = "meetings";

    /// <summary>
    /// 設定の中のパスを、実際に使える形にする。%USERPROFILE% などの環境変数を展開し、
    /// 会議ログ・予約ファイルの相対パスは exe のフォルダから数える(会議ログが空なら exe の隣の meetings)。
    /// </summary>
    /// <param name="settings">読んだ設定</param>
    /// <param name="baseDir">相対パスの起点(exe のフォルダ)</param>
    /// <returns>パスを直した設定</returns>
    public static AppSettings ResolvePaths(AppSettings settings, string baseDir)
    {
        static string Expand(string? path) => Environment.ExpandEnvironmentVariables(path ?? "");
        string Full(string path)
        {
            var expanded = Expand(path);
            return Path.IsPathRooted(expanded) ? expanded : Path.GetFullPath(Path.Combine(baseDir, expanded));
        }

        return settings with
        {
            WorkDir = Expand(settings.WorkDir),
            MeetingsDir = Full(string.IsNullOrWhiteSpace(settings.MeetingsDir) ? DefaultMeetingsDir : settings.MeetingsDir),
            ScheduleFile = string.IsNullOrWhiteSpace(settings.ScheduleFile) ? null : Full(settings.ScheduleFile),
            Claude = settings.Claude with { Executable = Expand(settings.Claude.Executable) },
            Agy = settings.Agy with { Executable = Expand(settings.Agy.Executable) },
            Codex = settings.Codex is null ? null : settings.Codex with { Executable = Expand(settings.Codex.Executable) },
        };
    }

    /// <summary>
    /// JSONオブジェクトに、別のJSONオブジェクトを重ねる。オブジェクトどうしは中身ごとに重ね、それ以外(値・配列)は置き換える。
    /// </summary>
    /// <param name="target">重ねられる側(書き換わる)</param>
    /// <param name="overlay">上に重ねる側</param>
    public static void Merge(JsonObject target, JsonObject overlay)
    {
        foreach (var (key, value) in overlay.ToList())
        {
            if (value is JsonObject child && target[key] is JsonObject existing)
                Merge(existing, child);
            else
                target[key] = value?.DeepClone();
        }
    }

    /// <summary>JSONの文字列を、最上位のオブジェクトとして読む。</summary>
    /// <param name="json">JSONの文字列</param>
    /// <param name="path">エラーの説明に出すファイルのパス</param>
    /// <returns>最上位のオブジェクト</returns>
    private static JsonObject ParseObject(string json, string path)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                   ?? throw new InvalidDataException($"設定ファイルの形が正しくありません: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"設定ファイルを読めません({path}): {ex.Message}", ex);
        }
    }

    /// <summary>
    /// ユーザーの設定ファイルが壊れている(JSONとして読めない・空)ときに、別の名前に退避する(消さない)。
    /// 起動できなくなるのを防ぐため、起動時に、設定を読む前に呼ぶ。退避したあとは、既定の設定で起動できる。
    /// </summary>
    /// <param name="localPath">settings.local.json のパス</param>
    /// <param name="now">退避した時刻(退避先の名前に入れる)</param>
    /// <returns>退避した先のパス。壊れていない・ファイルがないときは null</returns>
    public static string? QuarantineBrokenLocal(string localPath, DateTime now)
    {
        if (!File.Exists(localPath)) return null;
        try
        {
            ParseObject(File.ReadAllText(localPath), localPath);
            return null;
        }
        catch (InvalidDataException)
        {
            var backup = $"{localPath}.broken-{now:yyyyMMdd-HHmmss}";
            File.Move(localPath, backup);
            return backup;
        }
    }

    /// <summary>設定ファイルの作業フォルダだけを書き換える(他の設定は、そのまま残す)。</summary>
    /// <param name="path">settings.json のパス</param>
    /// <param name="workDir">新しい作業フォルダ</param>
    public static void SaveWorkDir(string path, string workDir)
    {
        UpdateRoot(path, root =>
        {
            root["workDir"] = workDir.Replace('\\', '/'); // パスの区切りは / で書く
        });
    }

    /// <summary>設定ファイルのClaudeのハーネス(extraArgs / writerExtraArgs / commandExtraArgs)だけを書き換える。</summary>
    /// <param name="path">settings.json のパス</param>
    /// <param name="claude">新しいClaudeの設定</param>
    public static void SaveClaudeHarness(string path, ClaudeSettings claude)
    {
        UpdateSection(path, "claude", obj =>
        {
            obj["extraArgs"] = ToArray(claude.ExtraArgs);
            if (claude.WriterExtraArgs is not null) obj["writerExtraArgs"] = ToArray(claude.WriterExtraArgs);
            if (claude.CommandExtraArgs is not null) obj["commandExtraArgs"] = ToArray(claude.CommandExtraArgs);
            if (claude.EnabledConnectors is not null) obj["enabledConnectors"] = ToArray(claude.EnabledConnectors);
        });
    }

    /// <summary>設定ファイルのagy(Gemini)のハーネス(extraArgs / noToolNote / writerExtraArgs / writerWarning)だけを書き換える。</summary>
    /// <param name="path">settings.json のパス</param>
    /// <param name="agy">新しいagyの設定</param>
    public static void SaveAgyHarness(string path, AgySettings agy)
    {
        UpdateSection(path, "agy", obj =>
        {
            obj["extraArgs"] = ToArray(agy.ExtraArgs);
            obj["noToolNote"] = agy.NoToolNote;
            if (agy.WriterExtraArgs is not null) obj["writerExtraArgs"] = ToArray(agy.WriterExtraArgs);
            if (agy.WriterWarning is not null) obj["writerWarning"] = agy.WriterWarning;
        });
    }

    /// <summary>設定ファイルの指定AI(claude または agy)のモデルと労力(effort)だけを書き換える。</summary>
    /// <param name="path">settings.json のパス</param>
    /// <param name="agentSection">"claude" または "agy"</param>
    /// <param name="modelId">選ばれたモデルID</param>
    /// <param name="effortId">選ばれた労力ID</param>
    public static void SaveModelAndEffort(string path, string agentSection, string modelId, string effortId)
    {
        UpdateSection(path, agentSection, obj =>
        {
            obj["model"] = modelId;
            obj["effort"] = effortId;
        });
    }

    /// <summary>設定ファイルのCLIの制限時間(timeoutSec)だけを書き換える。</summary>
    /// <param name="path">settings.json のパス</param>
    /// <param name="timeoutSec">制限時間(秒)</param>
    public static void SaveTimeout(string path, int timeoutSec) =>
        UpdateRoot(path, root => root["timeoutSec"] = timeoutSec);

    /// <summary>設定ファイルの「全員」宛ての答える順番(allOrder)だけを書き換える。</summary>
    /// <param name="path">settings.json のパス</param>
    /// <param name="order">順番の識別名(AllOrders の値)</param>
    public static void SaveAllOrder(string path, string order) =>
        UpdateRoot(path, root => root["allOrder"] = order);

    /// <summary>設定ファイルの配色テーマ(theme)だけを書き換える。</summary>
    /// <param name="path">settings.json のパス</param>
    /// <param name="themeId">選ばれたテーマID</param>
    public static void SaveTheme(string path, string themeId) =>
        UpdateRoot(path, root => root["theme"] = themeId);

    /// <summary>設定ファイルの見た目・名前設定(appearance)だけを書き換える。</summary>
    /// <param name="path">settings.json のパス</param>
    /// <param name="appearance">新しい見た目・表示名設定</param>
    public static void SaveAppearance(string path, AppearanceSettings appearance)
    {
        UpdateRoot(path, root =>
        {
            root["appearance"] = new JsonObject
            {
                ["isBusinessMode"] = appearance.IsBusinessMode,
                ["userName"] = appearance.UserName ?? "",
                ["claudeName"] = appearance.ClaudeName ?? "",
                ["geminiName"] = appearance.GeminiName ?? "",
            };
        });
    }

    /// <summary>設定ファイルのリモートの合言葉(remote.token)だけを書き換える。</summary>
    /// <param name="path">settings.json のパス</param>
    /// <param name="token">新しい合言葉</param>
    public static void SaveRemoteToken(string path, string token) =>
        UpdateSection(path, "remote", obj => obj["token"] = token);

    /// <summary>「場所」の画面に出すパスを読む(既定の上にユーザーの設定を重ねた値。環境変数は展開しない)。</summary>
    /// <param name="defaultsPath">settings.json(既定の値)のパス</param>
    /// <param name="localPath">settings.local.json(ユーザーの設定)のパス</param>
    /// <returns>パスの一覧</returns>
    public static PathSettings LoadPaths(string defaultsPath, string localPath) => ToPaths(Load(defaultsPath, localPath));

    /// <summary>
    /// 「場所」の画面で変えたパスを、ユーザーの設定に書く。空、または既定と同じ値は、ユーザーの設定から消す(既定の値を使う)。
    /// </summary>
    /// <param name="defaultsPath">settings.json(既定の値)のパス</param>
    /// <param name="localPath">settings.local.json(ユーザーの設定)のパス</param>
    /// <param name="paths">新しいパスの一覧</param>
    public static void SavePaths(string defaultsPath, string localPath, PathSettings paths)
    {
        var defaults = ToPaths(Load(defaultsPath, localPath: ""));
        UpdateRoot(localPath, root =>
        {
            SetOrRemove(root, "workDir", paths.WorkDir, defaults.WorkDir);
            SetOrRemove(root, "meetingsDir", paths.MeetingsDir, defaults.MeetingsDir);
            SetOrRemove(root, "scheduleFile", paths.ScheduleFile, defaults.ScheduleFile);
            foreach (var (section, value, def) in new[]
            {
                ("claude", paths.ClaudeExecutable, defaults.ClaudeExecutable),
                ("agy", paths.AgyExecutable, defaults.AgyExecutable),
                ("codex", paths.CodexExecutable, defaults.CodexExecutable),
            })
            {
                var obj = root[section] as JsonObject ?? new JsonObject();
                SetOrRemove(obj, "executable", value, def);
                if (obj.Count > 0) root[section] = obj;
                else root.Remove(section);
            }
        });
    }

    /// <summary>設定から、「場所」の画面に出すパスだけを取り出す。</summary>
    /// <param name="settings">設定</param>
    /// <returns>パスの一覧</returns>
    private static PathSettings ToPaths(AppSettings settings) => new(
        WorkDir: settings.WorkDir ?? "",
        MeetingsDir: settings.MeetingsDir ?? "",
        ScheduleFile: settings.ScheduleFile ?? "",
        ClaudeExecutable: settings.Claude.Executable ?? "",
        AgyExecutable: settings.Agy.Executable ?? "",
        CodexExecutable: (settings.Codex ?? new CodexSettings()).Executable ?? "");

    /// <summary>値を書く。空、または既定と同じなら、項目を消す(既定の値に任せる)。パスの区切りは / で書く。</summary>
    /// <param name="obj">書き込むJSONオブジェクト</param>
    /// <param name="key">項目の名前</param>
    /// <param name="value">新しい値</param>
    /// <param name="defaultValue">既定の値</param>
    private static void SetOrRemove(JsonObject obj, string key, string value, string defaultValue)
    {
        var normalized = (value ?? "").Trim().Replace('\\', '/');
        if (normalized.Length == 0 || normalized == defaultValue.Replace('\\', '/')) obj.Remove(key);
        else obj[key] = normalized;
    }

    /// <summary>設定ファイルを読んで、渡された処理で書き換えて、書き戻す(ほかの設定は、そのまま残す。ファイルがなければ作る)。</summary>
    /// <param name="path">書き込む設定ファイル(ふつうは settings.local.json)のパス</param>
    /// <param name="update">最上位のJSONオブジェクトを書き換える処理</param>
    private static void UpdateRoot(string path, Action<JsonObject> update)
    {
        var root = File.Exists(path) ? ParseObject(File.ReadAllText(path), path) : new JsonObject();
        update(root);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        WriteRoot(path, root);
    }

    /// <summary>設定ファイルの1つの項目(claude・agy など)を、渡された処理で書き換える(項目がなければ作る)。</summary>
    /// <param name="path">settings.json のパス</param>
    /// <param name="section">項目の名前</param>
    /// <param name="update">その項目のJSONオブジェクトを書き換える処理</param>
    private static void UpdateSection(string path, string section, Action<JsonObject> update) =>
        UpdateRoot(path, root =>
        {
            var obj = root[section]?.AsObject() ?? new JsonObject();
            update(obj);
            root[section] = obj;
        });

    private static JsonArray ToArray(IReadOnlyList<string> items) =>
        new(items.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());

    /// <summary>設定ファイルを書く(書き込み中に落ちても元のファイルが壊れないよう、一時ファイルに書いてから置き換える)。</summary>
    private static void WriteRoot(string path, JsonObject root)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, root.ToJsonString(WriteOptions), new System.Text.UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}
