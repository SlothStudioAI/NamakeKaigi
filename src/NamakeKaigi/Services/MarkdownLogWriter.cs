using System.IO;
using System.Text;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>
/// 会議ログ(Markdown)を追記で保存する。
/// 置き場所は「保存先/起動した時刻/会議名/」で、その中に log.md・meeting.json(状態)・attachments/(貼り付け画像)を置く。
/// フォルダとファイルは、最初の発言(または最初の貼り付け画像)のときに作る(発言のない会議は、何も残さない)。
/// 古い形式の会議フォルダ(保存先/日時/log.md)を開き直したときは、その場所のまま続けて書く。
/// </summary>
public sealed class MarkdownLogWriter
{
    /// <summary>名前のない会議のフォルダ名。</summary>
    public const string DefaultFolderName = "会議";

    private const string HeadingBase = "# 会議ログ";
    private const string TimestampFormat = "yyyyMMdd_HHmmss";
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly System.Text.RegularExpressions.Regex TitleRegex = new(
        @"^#\s*会議ログ\s*[:：]\s*(?<title>.+?)\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly object _lock = new();
    private readonly string _parentDir;   // 会議フォルダを置く場所(新しい会議なら、起動した時刻のフォルダ)
    private readonly bool _canRename;     // 名前を変えたとき、会議フォルダも付け替えてよいか(古い形式の日時フォルダは付け替えない)
    private string _folder;               // 会議フォルダ(まだ作っていなければ、予定のパス)
    private bool _created;                // 会議フォルダを作った(または、既存の会議を開き直した)ならtrue

    /// <summary>ログファイル(log.md)のパス(まだ作っていなければ、予定のパス)。</summary>
    public string Path => System.IO.Path.Combine(_folder, MeetingStore.LogFileName);

    /// <summary>状態ファイル(meeting.json)のパス。</summary>
    public string StatePath => System.IO.Path.Combine(_folder, MeetingStore.StateFileName);

    /// <summary>会議フォルダのパス(まだ作っていなければ、予定のパス)。</summary>
    public string FolderPath => _folder;

    /// <summary>会議の開始時刻。</summary>
    public DateTime StartedAt { get; }

    /// <summary>会議の名前(log.md の1行目に書いてある名前。なければnull)。</summary>
    public string? Title { get; private set; }

    /// <summary>新しい会議の置き場所を決める(フォルダは、最初の発言のときに作る)。</summary>
    /// <param name="sessionDir">会議フォルダを置く場所(保存先/起動した時刻)</param>
    /// <param name="startedAt">会議の開始時刻</param>
    /// <param name="title">会議の名前(省略・空なら「会議」)</param>
    public MarkdownLogWriter(string sessionDir, DateTime startedAt, string? title = null)
    {
        _parentDir = sessionDir;
        StartedAt = startedAt;
        _canRename = true;
        Title = Clean(title) is { Length: > 0 } t ? t : null;
        _folder = System.IO.Path.Combine(_parentDir, FolderNameFor(Title));
    }

    /// <summary>既存の会議フォルダを開き直して、その log.md に続けて追記できるようにする。</summary>
    /// <param name="existingFolderPath">既存の会議フォルダのパス(新しい形式は 保存先/起動時刻/会議名、古い形式は 保存先/日時)</param>
    /// <param name="startedAt">開始時刻</param>
    /// <param name="reopen">既存の会議を開き直す印</param>
    public MarkdownLogWriter(string existingFolderPath, DateTime startedAt, bool reopen)
    {
        _ = reopen;
        StartedAt = startedAt;
        _folder = existingFolderPath.TrimEnd('/', '\\');
        _parentDir = System.IO.Path.GetDirectoryName(_folder) ?? _folder;
        // 新しい形式(起動時刻のフォルダの中の、会議名のフォルダ)だけ、名前に合わせて付け替える
        _canRename = MeetingStore.IsStamp(System.IO.Path.GetFileName(_parentDir))
                     && !MeetingStore.IsStamp(System.IO.Path.GetFileName(_folder));
        _created = true;
        Directory.CreateDirectory(_folder);
        if (!File.Exists(Path))
        {
            File.WriteAllText(Path, HeadingBase + "\n", Utf8);
        }
        Title = ReadTitle(Path);
    }

    /// <summary>起動した時刻から、その起動で作る会議を置くフォルダ(保存先/yyyyMMdd_HHmmss)を求める。</summary>
    /// <param name="meetingsDir">会議ログの保存先</param>
    /// <param name="launchedAt">アプリを起動した時刻</param>
    /// <returns>起動した時刻のフォルダのパス</returns>
    public static string SessionDirFor(string meetingsDir, DateTime launchedAt) =>
        System.IO.Path.Combine(meetingsDir, launchedAt.ToString(TimestampFormat));

    /// <summary>
    /// 会議フォルダを用意する(まだなければ作る)。同じ名前のフォルダがすでにあれば「名前 (2)」のように番号を付ける。
    /// 貼り付け画像を保存する前にも呼ぶ(発言の前に画像を貼ったとき、正しいフォルダに入れるため)。
    /// </summary>
    /// <returns>会議フォルダのパス</returns>
    public string EnsureFolder()
    {
        lock (_lock)
        {
            if (!_created)
            {
                _folder = UniqueFolder(_parentDir, FolderNameFor(Title), except: null);
                Directory.CreateDirectory(_folder);
                _created = true;
            }
            return _folder;
        }
    }

    /// <summary>log.md の1行目から、会議の名前を読む(「# 会議ログ: 名前」の形。なければnull)。</summary>
    /// <param name="logPath">log.md のパス</param>
    /// <returns>会議の名前(なければnull)</returns>
    public static string? ReadTitle(string logPath)
    {
        try
        {
            if (!File.Exists(logPath)) return null;
            using var reader = new StreamReader(logPath, Utf8);
            var m = TitleRegex.Match(reader.ReadLine() ?? "");
            return m.Success ? m.Groups["title"].Value : null;
        }
        catch (IOException)
        {
            return null; // 開けないときは、名前なしとして扱う
        }
    }

    /// <summary>
    /// 会議の名前を書く。log.md の1行目を「# 会議ログ: 名前」にし、会議フォルダも「名前」に付け替える
    /// (log.md の中に書いた、古いフォルダのパス(添付など)も、新しいパスに書き換える)。
    /// まだフォルダを作っていなければ、予定の名前だけを変える。名前が空なら、名前なしの見出しと「会議」のフォルダ名に戻す。
    /// 付け替えられないとき(中のファイルがほかのアプリで開かれているなど)は、フォルダ名は今のまま(1行目の名前は書き換わる)。
    /// </summary>
    /// <param name="title">会議の名前(改行は空白にする。空・空白だけなら名前なし)</param>
    public void SetTitle(string? title)
    {
        var clean = Clean(title);
        lock (_lock)
        {
            Title = clean.Length == 0 ? null : clean;
            if (!_created)
            {
                _folder = System.IO.Path.Combine(_parentDir, FolderNameFor(Title)); // 予定の名前だけを変える
                return;
            }
            if (File.Exists(Path))
            {
                var text = File.ReadAllText(Path, Utf8);
                var lineEnd = text.IndexOf('\n');
                var first = (lineEnd < 0 ? text : text[..lineEnd]).TrimEnd('\r');
                // 1行目が見出しならそこだけ置き換え、そうでなければ(手で書き換えられたとき)見出しを先頭に足す
                var rest = first.StartsWith("#", StringComparison.Ordinal)
                    ? (lineEnd < 0 ? "" : text[lineEnd..])
                    : "\n" + text;
                File.WriteAllText(Path, Heading() + (rest.Length == 0 ? "\n" : rest), Utf8);
            }
            if (_canRename) TryRenameFolder();
        }
    }

    /// <summary>発言を1件追記する(最初の発言のときに、会議フォルダと log.md を作る)。</summary>
    /// <param name="entry">発言</param>
    public void Append(ChatEntry entry)
    {
        var name = Participants.Get(entry.SpeakerId).LogName;
        // ユーザーの発言には、宛先を添える(例: ユーザー → ジェミリー)
        var to = entry.TargetIds is { Count: > 0 }
            ? " → " + string.Join("・", entry.TargetIds.Select(id => Participants.Get(id).LogName))
            : "";
        var block = $"\n## {name}{to} ({entry.Time:HH:mm:ss})\n\n{entry.Text.Trim()}\n";
        lock (_lock)
        {
            EnsureFolder();
            if (!File.Exists(Path)) File.WriteAllText(Path, Heading() + "\n", Utf8);
            File.AppendAllText(Path, block, Utf8);
        }
    }

    /// <summary>ファイル名・フォルダ名に使えない文字を「_」に置き換え、末尾の空白とピリオドを落とす。</summary>
    /// <param name="title">会議の名前</param>
    /// <returns>フォルダ名に使える形の名前(すべて落ちたら空)</returns>
    public static string SafeFileName(string title)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = title.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim().TrimEnd('.', ' ');
    }

    /// <summary>会議フォルダを、いまの名前に付け替える。付け替えたら、log.md の中の古いフォルダのパスも書き換える。付け替えられなければ、今のまま。</summary>
    private void TryRenameFolder()
    {
        var parent = System.IO.Path.GetDirectoryName(_folder)!;
        var wanted = System.IO.Path.Combine(parent, FolderNameFor(Title));
        if (string.Equals(wanted, _folder, StringComparison.OrdinalIgnoreCase)) return;
        var target = UniqueFolder(parent, FolderNameFor(Title), except: _folder);
        if (string.Equals(target, _folder, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var old = _folder;
            Directory.Move(old, target);
            _folder = target;
            ReplacePathsInLog(old, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 中のファイルが開かれていて動かせない: フォルダ名は今のまま
        }
    }

    /// <summary>log.md の中に書いた古いフォルダのパスを、新しいパスに書き換える(区切りが \ と / の両方)。</summary>
    private void ReplacePathsInLog(string oldFolder, string newFolder)
    {
        if (!File.Exists(Path)) return;
        var text = File.ReadAllText(Path, Utf8);
        var replaced = text
            .Replace(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase)
            .Replace(oldFolder.Replace('\\', '/'), newFolder.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        if (!ReferenceEquals(text, replaced) && text != replaced) File.WriteAllText(Path, replaced, Utf8);
    }

    /// <summary>その場所で、まだ使われていないフォルダ名(「名前」→「名前 (2)」→「名前 (3)」…)のパスを返す。</summary>
    /// <param name="parent">置く場所</param>
    /// <param name="name">付けたい名前</param>
    /// <param name="except">自分自身のフォルダ(あれば、使われていても自分なので避けない)</param>
    private static string UniqueFolder(string parent, string name, string? except)
    {
        var candidate = System.IO.Path.Combine(parent, name);
        for (var i = 2; Directory.Exists(candidate) && !string.Equals(candidate, except, StringComparison.OrdinalIgnoreCase); i++)
        {
            candidate = System.IO.Path.Combine(parent, $"{name} ({i})");
        }
        return candidate;
    }

    /// <summary>名前から、会議フォルダの名前を作る(使えない文字は「_」。空なら「会議」)。</summary>
    private static string FolderNameFor(string? title)
    {
        var safe = SafeFileName(title ?? "");
        return safe.Length == 0 ? DefaultFolderName : safe;
    }

    /// <summary>改行を空白にし、前後の空白を落とす。</summary>
    private static string Clean(string? title) =>
        string.Join(" ", (title ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

    /// <summary>いまの名前の見出し(「# 会議ログ」または「# 会議ログ: 名前」)。</summary>
    private string Heading() => Title is null ? HeadingBase : $"{HeadingBase}: {Title}";
}
