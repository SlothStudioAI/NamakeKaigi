using System.IO;
using System.Text.Json;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>予約の保存ファイル(schedule.json)と、実行の履歴(schedule-history.jsonl)を読み書きする。</summary>
public sealed class ScheduleStore
{
    // 手で直すことがあるので、末尾のカンマとコメントは許す(設定ファイル settings.json の読み込みと同じ)
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly JsonSerializerOptions LineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();

    /// <summary>保存先を決める。</summary>
    /// <param name="path">schedule.json のパス</param>
    public ScheduleStore(string path)
    {
        Path = path;
    }

    /// <summary>schedule.json のパス。</summary>
    public string Path { get; }

    /// <summary>予約を読む。ファイルがなければ、予約なしとして扱う。</summary>
    /// <returns>予約の保存内容</returns>
    /// <exception cref="InvalidDataException">ファイルが壊れているとき(気づけるよう、黙って空にしない)</exception>
    public ScheduleFile Load()
    {
        if (!File.Exists(Path)) return ScheduleFile.Empty;
        try
        {
            var file = JsonSerializer.Deserialize<ScheduleFile>(File.ReadAllText(Path), ReadOptions);
            return file is null ? ScheduleFile.Empty : file with { Jobs = file.Jobs ?? Array.Empty<ScheduledJob>() };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"予約のファイルを読めません({Path}): {ex.Message}", ex);
        }
    }

    /// <summary>予約を保存する(書き込み中に落ちても壊れないよう、一時ファイルに書いてから置き換える)。</summary>
    /// <param name="file">保存する内容</param>
    public void Save(ScheduleFile file)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, WriteOptions));
            File.Move(temp, Path, overwrite: true);
        }
    }

    /// <summary>実行の履歴のファイルパス(指定がなければ、schedule.json の隣)。</summary>
    /// <param name="file">予約の保存内容</param>
    /// <returns>履歴ファイルのパス</returns>
    public string HistoryPath(ScheduleFile file) =>
        string.IsNullOrWhiteSpace(file.HistoryFile)
            ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!, "schedule-history.jsonl")
            : file.HistoryFile!;

    /// <summary>実行の結果を、履歴に1行足す。</summary>
    /// <param name="historyPath">履歴ファイルのパス</param>
    /// <param name="record">足す結果</param>
    public void AppendHistory(string historyPath, ScheduleRunRecord record)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(historyPath))!);
            File.AppendAllText(historyPath, JsonSerializer.Serialize(record, LineOptions) + "\n");
        }
    }

    /// <summary>履歴の新しい側から、指定した件数を読む(新しい順)。</summary>
    /// <param name="historyPath">履歴ファイルのパス</param>
    /// <param name="count">読む件数</param>
    /// <returns>結果の一覧(読めない行は飛ばす)</returns>
    public IReadOnlyList<ScheduleRunRecord> ReadRecentHistory(string historyPath, int count)
    {
        if (!File.Exists(historyPath)) return Array.Empty<ScheduleRunRecord>();
        string[] lines;
        lock (_gate) lines = File.ReadAllLines(historyPath);
        var result = new List<ScheduleRunRecord>();
        for (var i = lines.Length - 1; i >= 0 && result.Count < count; i--)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            try
            {
                var record = JsonSerializer.Deserialize<ScheduleRunRecord>(lines[i], ReadOptions);
                if (record is not null) result.Add(record);
            }
            catch (JsonException)
            {
                // 壊れた行は飛ばす
            }
        }
        return result;
    }
}
