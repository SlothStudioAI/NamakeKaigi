using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using NamakeKaigi.Models;

namespace NamakeKaigi.Services;

/// <summary>リンクの種類。</summary>
public enum LinkKind
{
    /// <summary>リンクではない(存在しないパスなど)。</summary>
    None,
    /// <summary>http / https のURL。ブラウザで開く。</summary>
    Web,
    /// <summary>フォルダ。エクスプローラーで開く。</summary>
    Folder,
    /// <summary>ふつうのファイル。既定のアプリで開く。</summary>
    File,
    /// <summary>実行できるファイル。実行せず、エクスプローラーで場所を示すだけにする。</summary>
    RevealFile,
}

/// <summary>本文の中で見つけたリンク。</summary>
/// <param name="Start">本文の中の開始位置</param>
/// <param name="Length">本文の中の長さ</param>
/// <param name="Target">開く先(URL またはフルパス)</param>
/// <param name="Kind">種類</param>
public sealed record LinkMatch(int Start, int Length, string Target, LinkKind Kind);

/// <summary>返答の本文から、URL・ファイル・フォルダのパスを見つけて、種類を分ける(WBS 11.31・11.32)。画面は知らない。</summary>
public static class LinkDetector
{
    // 画像として吹き出しの中に見せる拡張子
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp"];

    // 開いたら実行されてしまうファイル。クリックでは実行せず、場所を示すだけにする
    private static readonly string[] ExecutableExtensions =
    [
        ".exe", ".bat", ".cmd", ".com", ".msi", ".msp", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".scr", ".lnk", ".reg", ".hta", ".jar", ".cpl", ".dll", ".pif", ".msc", ".url",
        ".application", ".appref-ms", ".gadget", ".inf",
        // スクリプト(関連付けがあると、クリックで実行される)
        ".py", ".pyw", ".pyz", ".rb", ".pl", ".php", ".sh", ".ps1xml", ".psd1", ".vb", ".mjs", ".cjs", ".chm", ".docm", ".xlsm", ".pptm",
    ];

    /// <summary>パスの実在を調べるときの待ち時間などの設定。設定ファイルから、起動時に入れる。</summary>
    public static LinkProbeSettings Probe { get; set; } = new();

    // 調べた候補の結果(開始時刻つき)と、時間切れになったドライブ(開始時刻つき)。画面のスレッドと待ち受けのスレッドから使われる
    private const int MaxCacheEntries = 2000;
    private static readonly ConcurrentDictionary<string, (long Tick, string? Result)> PrefixCache = new();
    private static readonly ConcurrentDictionary<char, long> SlowDrives = new();

    // URL(ASCIIだけ。日本語の文が続いても、そこまでで切れる)
    private static readonly Regex UrlPattern = new(
        @"https?://[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=%]+", RegexOptions.Compiled);

    // Windowsのパスの候補(空白・引用符・全角の括弧や句読点の手前まで)。実在する所までは、あとで縮めて決める
    private static readonly Regex PathPattern = new(
        @"(?<![A-Za-z0-9])[A-Za-z]:[\\/][^\s""'<>|*?`（）「」、。,;]*", RegexOptions.Compiled);

    // URLの終わりにくっついた句読点(文の区切り)
    private static readonly char[] UrlTrailingChars = ['.', ',', ';', ':', '!', '?', '\'', '"'];

    /// <summary>本文の中のリンクを、出てくる順に返す。パスは、実在するものだけ。</summary>
    /// <param name="text">本文</param>
    /// <returns>見つけたリンク(重ならない)</returns>
    public static IReadOnlyList<LinkMatch> FindLinks(string text)
    {
        var found = new List<LinkMatch>();
        if (string.IsNullOrEmpty(text)) return found;

        foreach (Match m in UrlPattern.Matches(text))
        {
            var url = TrimUrl(m.Value);
            if (url.Length > "https://".Length) found.Add(new LinkMatch(m.Index, url.Length, url, LinkKind.Web));
        }

        foreach (Match m in PathPattern.Matches(text))
        {
            if (found.Any(f => m.Index < f.Start + f.Length && f.Start < m.Index + m.Length)) continue;
            var existing = LongestExistingPrefix(m.Value);
            if (existing is null) continue;
            found.Add(new LinkMatch(m.Index, existing.Length, existing, Classify(existing)));
        }

        return found.OrderBy(f => f.Start).ToList();
    }

    /// <summary>1つの文字列(コードの中身・Markdownのリンク先)が、開けるリンクかを調べ、開く先を返す。</summary>
    /// <param name="text">URL またはパス(前後の空白・引用符は無視する)</param>
    /// <returns>開く先。開けないものはnull</returns>
    public static string? ResolveTarget(string text)
    {
        var target = (text ?? "").Trim().Trim('"', '\'', '<', '>').Trim();
        if (target.Length == 0) return null;
        return Classify(target) == LinkKind.None ? null : target;
    }

    /// <summary>開く先の種類を調べる。http(s)のURL・実在するフォルダ・実在するファイルだけがリンクになる。</summary>
    /// <param name="target">URL またはフルパス</param>
    /// <returns>種類(それ以外は None)</returns>
    public static LinkKind Classify(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return LinkKind.None;
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(target, UriKind.Absolute, out _) ? LinkKind.Web : LinkKind.None;
        }

        return ClassifyPath(target);
    }

    /// <summary>パスの実在とファイルの種類を調べる(ディスクに問い合わせる。止まりうるドライブへの対策は <see cref="ClassifyPath"/>)。</summary>
    /// <param name="target">フルパス</param>
    /// <returns>種類(それ以外は None)</returns>
    private static LinkKind ClassifyCore(string target)
    {
        try
        {
            if (!Path.IsPathRooted(target)) return LinkKind.None;
            if (Directory.Exists(target)) return LinkKind.Folder;
            if (!System.IO.File.Exists(target)) return LinkKind.None;
            // Windowsは、ファイル名の末尾の「.」と空白を無視して開く(run.exe. は run.exe を開く)。拡張子は、それを除いて見る
            var ext = Path.GetExtension(target.TrimEnd('.', ' '));
            return ExecutableExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) ? LinkKind.RevealFile : LinkKind.File;
        }
        catch (ArgumentException)
        {
            return LinkKind.None;
        }
        catch (NotSupportedException)
        {
            return LinkKind.None;
        }
    }

    /// <summary>ネットワークの共有(\\サーバー\共有、//サーバー/共有)か、デバイスのパス(\\?\ \\.\)かを判定する。</summary>
    /// <param name="target">パス</param>
    /// <returns>どちらかならtrue</returns>
    private static bool IsNetworkOrDevicePath(string target)
    {
        var t = target.TrimStart();
        return t.Length >= 2 && (t[0] is '\\' or '/') && (t[1] is '\\' or '/');
    }

    /// <summary>画像として見せる拡張子(png・jpg・jpeg・gif・bmp)のファイルかを判定する(実在は調べない)。</summary>
    /// <param name="path">ファイルのパス</param>
    /// <returns>画像の拡張子ならtrue</returns>
    public static bool IsImageFile(string path)
    {
        try
        {
            return ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>URLの終わりについた句読点と、対応のない閉じ括弧を外す。</summary>
    private static string TrimUrl(string url)
    {
        var s = url.TrimEnd(UrlTrailingChars);
        while (s.EndsWith(')') && s.Count(c => c == ')') > s.Count(c => c == '('))
        {
            s = s[..^1].TrimEnd(UrlTrailingChars);
        }
        return s;
    }

    /// <summary>候補の先頭から、実在するいちばん長い所までを返す(「…a.pngを開く」から「…a.png」を取り出す)。</summary>
    /// <param name="candidate">パスの候補</param>
    /// <returns>実在するパス。ドライブ直下だけのものと、見つからないものはnull</returns>
    private static string? LongestExistingPrefix(string candidate)
    {
        // 流れている返答は200msごとに作り直されるので、同じ候補は短い間だけ覚えておく
        var now = Environment.TickCount64;
        if (PrefixCache.TryGetValue(candidate, out var cached) && now - cached.Tick < Probe.CacheSec * 1000L) return cached.Result;

        string? result = null;
        for (var length = candidate.Length; length > 3; length--)
        {
            var s = candidate[..length];
            if (s.EndsWith('.') || s.EndsWith(' ')) continue;
            // 途中のフォルダまでで切らない(存在しないファイルのパスが、手前のフォルダへのリンクになるのを防ぐ)
            if (s.EndsWith('\\') || s.EndsWith('/')) continue;
            if (length < candidate.Length && candidate[length] is '\\' or '/') continue;
            if (Classify(s) != LinkKind.None) { result = s; break; }
        }

        if (PrefixCache.Count > MaxCacheEntries) PrefixCache.Clear();
        PrefixCache[candidate] = (now, result);
        return result;
    }

    /// <summary>
    /// ドライブ文字つきのパスの実在を調べる。ネットワーク・リムーバブルなどのドライブは、切断されていると
    /// 1回の問い合わせで数秒〜数十秒止まるので、待ち時間に上限を付ける(超えたドライブは、しばらく調べずにリンクにしない)。
    /// </summary>
    /// <param name="target">フルパス</param>
    /// <returns>種類(調べられなかったときは None)</returns>
    private static LinkKind ClassifyPath(string target)
    {
        // ネットワークの共有(\\サーバー\共有)と、デバイスのパス(\\?\ \\.\)は調べない。調べるだけで外のサーバーへ接続が飛ぶため
        if (IsNetworkOrDevicePath(target)) return LinkKind.None;
        if (target.Length < 2 || target[1] != ':' || !char.IsAsciiLetter(target[0]) || !IsMayBeSlowDrive(target[0]))
            return ClassifyCore(target);

        var drive = char.ToUpperInvariant(target[0]);
        var now = Environment.TickCount64;
        if (SlowDrives.TryGetValue(drive, out var since))
        {
            if (now - since < Probe.SlowDriveSec * 1000L) return LinkKind.None;
            SlowDrives.TryRemove(drive, out _);
        }

        var probe = Task.Run(() => ClassifyCore(target));
        try
        {
            if (probe.Wait(Math.Max(1, Probe.TimeoutMs))) return probe.Result;
        }
        catch (AggregateException)
        {
            return LinkKind.None;
        }
        SlowDrives[drive] = now;
        return LinkKind.None;
    }

    /// <summary>そのドライブ文字が、固定ディスク以外(ネットワーク・リムーバブル・光学など)で、問い合わせが止まりうるかを調べる。</summary>
    /// <param name="letter">ドライブ文字</param>
    /// <returns>止まりうるならtrue(割り当てのない文字は、すぐ「ない」と返るのでfalse)</returns>
    private static bool IsMayBeSlowDrive(char letter)
    {
        try
        {
            return new DriveInfo(letter.ToString()).DriveType is DriveType.Network or DriveType.Removable or DriveType.CDRom or DriveType.Unknown;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
