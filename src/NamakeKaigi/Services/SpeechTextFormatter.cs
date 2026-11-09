using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NamakeKaigi.Services;

/// <summary>返答のMarkdownを、声で読みやすい文にする(WBS 11.35)。コード・表・URL・記号は、読み上げ向けに言い換える。画面は知らない。</summary>
public static class SpeechTextFormatter
{
    /// <summary>長い返答を途中で切ったときに、最後に添える言葉。</summary>
    public const string Continuation = "続きは画面で見てください。";

    private static readonly Regex Image = new(@"!\[[^\]\r\n]*\]\([^)\r\n]*\)", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]\r\n]*)\]\([^)\r\n]*\)", RegexOptions.Compiled);
    private static readonly Regex Url = new(@"https?://[A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=%]+", RegexOptions.Compiled);
    private static readonly Regex WinPath = new(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/][^\s""'<>|*?`（）「」、。,;]*", RegexOptions.Compiled);
    private static readonly Regex Numbered = new(@"^\s*(\d{1,3})[.)]\s+", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^\s*[-*]\s+", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^\s*#{1,6}\s+", RegexOptions.Compiled);
    private static readonly Regex Quote = new(@"^\s*(>\s*)+", RegexOptions.Compiled);

    /// <summary>読み上げる文を作る。先頭に話し手の名前を添える。読むものがなければ空文字を返す。</summary>
    /// <param name="markdown">返答の本文(Markdown)</param>
    /// <param name="speakerName">話し手の表示名(先頭で「〇〇です。」と言う)</param>
    /// <param name="maxChars">本文を読む文字数の上限(超えたら途中で切る。0以下なら全部読む)</param>
    /// <returns>読み上げる文</returns>
    public static string Format(string markdown, string speakerName, int maxChars)
    {
        var body = ToSpokenBody(markdown ?? "");
        if (body.Length == 0) return "";

        if (maxChars > 0 && body.Length > maxChars)
        {
            var cut = body[..maxChars];
            var end = cut.LastIndexOfAny(['。', '！', '？']);
            body = (end >= maxChars / 2 ? cut[..(end + 1)] : cut) + Continuation;
        }
        return string.IsNullOrWhiteSpace(speakerName) ? body : $"{speakerName}です。{body}";
    }

    /// <summary>本文を、1行ずつ読み上げ向けに直してつなぐ。</summary>
    private static string ToSpokenBody(string markdown)
    {
        var sentences = new StringBuilder();
        var inCode = false;
        var inTable = false;

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (!inCode) sentences.Append("コードがあります。");
                inCode = !inCode;
                continue;
            }
            if (inCode) continue;

            if (line.StartsWith('|'))
            {
                if (!inTable) sentences.Append("表があります。");
                inTable = true;
                continue;
            }
            inTable = false;

            if (line.Length == 0 || IsHorizontalRule(line)) continue;

            line = Heading.Replace(line, "");
            line = Quote.Replace(line, "");
            line = Numbered.Replace(line, "$1、");
            line = Bullet.Replace(line, "");
            line = SpeakInline(line).Trim();
            if (line.Length == 0) continue;

            sentences.Append(line);
            if (!"。！？!?、.".Contains(line[^1])) sentences.Append('。');
        }
        return sentences.ToString();
    }

    /// <summary>1行の中の記号・画像・リンク・URL・パスを、読み上げ向けに言い換える。</summary>
    private static string SpeakInline(string line)
    {
        line = Image.Replace(line, "画像があります");
        line = Link.Replace(line, "$1");
        line = Url.Replace(line, "リンク");
        line = WinPath.Replace(line, m => NameOfPath(m.Value));
        return line.Replace("**", "").Replace("`", "").Replace("<u>", "").Replace("</u>", "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>パスから、読み上げるファイル名(またはフォルダ名)を取り出す。</summary>
    private static string NameOfPath(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : "フォルダ";
    }

    /// <summary>区切り線(---・***・___)の行か。</summary>
    private static bool IsHorizontalRule(string line)
    {
        var body = line.Replace(" ", "", StringComparison.Ordinal);
        return body.Length >= 3 && (body.All(c => c == '-') || body.All(c => c == '*') || body.All(c => c == '_'));
    }
}
