using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NamakeKaigi.Services;

/// <summary>
/// Markdown記法の判定と分解(文字列だけを扱い、画面の部品は作らない)。
/// 行が見出し・引用・箇条書き・表などかを調べ、深さ・番号・中身に分ける。
/// </summary>
public static class MarkdownSyntax
{
    /// <summary>[文字](リンク先) と ![説明](画像のパス) を見つける正規表現。</summary>
    public static readonly Regex LinkPattern = new(@"!?\[([^\]\r\n]*)\]\(([^)\r\n]+)\)", RegexOptions.Compiled);

    /// <summary>Markdown記法が含まれているかを判定する。</summary>
    public static bool ContainsMarkdownSyntax(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Contains("```", StringComparison.Ordinal) ||
            text.Contains("**", StringComparison.Ordinal) ||
            text.Contains('`') ||
            text.Contains("</u>", StringComparison.OrdinalIgnoreCase) ||
            LinkPattern.IsMatch(text) ||
            LinkDetector.FindLinks(text).Count > 0)
        {
            return true;
        }

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var t = raw.TrimStart();
            if (IsTableSeparator(t) ||
                IsHorizontalRule(t) ||
                TryParseHeading(t, out _, out _) ||
                TryParseQuote(raw, out _, out _) ||
                TryParseListItem(raw, out _, out _, out _))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>箇条書き(「- 」「* 」)と番号つき(「1. 」「1) 」)の行かを判定し、深さ・番号・中身に分ける。</summary>
    /// <param name="rawLine">1行(先頭の空白を含む)</param>
    /// <param name="level">深さ(先頭の空白2つで1段。最大3)</param>
    /// <param name="number">番号つきなら番号。箇条書きならnull</param>
    /// <param name="content">印のあとの中身</param>
    /// <returns>箇条書き・番号つきの行ならtrue</returns>
    public static bool TryParseListItem(string rawLine, out int level, out int? number, out string content)
    {
        level = 0;
        number = null;
        content = "";
        var indent = 0;
        while (indent < rawLine.Length && (rawLine[indent] == ' ' || rawLine[indent] == '\t')) indent++;
        var body = rawLine[indent..];
        var spaces = rawLine[..indent].Sum(ch => ch == '\t' ? 4 : 1);

        if (body.StartsWith("- ", StringComparison.Ordinal) || body.StartsWith("* ", StringComparison.Ordinal))
        {
            content = body[2..];
        }
        else
        {
            var digits = 0;
            while (digits < body.Length && char.IsAsciiDigit(body[digits])) digits++;
            if (digits == 0 || digits > 3 || body.Length < digits + 2 ||
                (body[digits] != '.' && body[digits] != ')') || body[digits + 1] != ' ')
            {
                return false;
            }
            number = int.Parse(body[..digits], CultureInfo.InvariantCulture);
            content = body[(digits + 2)..];
        }

        level = Math.Min(spaces / 2, 3);
        return true;
    }

    /// <summary>見出しの行(「#」〜「######」のあとに空白)かを判定し、深さと中身に分ける。</summary>
    /// <param name="line">先頭の空白を除いた1行</param>
    /// <param name="level">見出しの深さ(1〜6)</param>
    /// <param name="content">「#」と空白のあとの中身</param>
    /// <returns>見出しの行ならtrue</returns>
    public static bool TryParseHeading(string line, out int level, out string content)
    {
        level = 0;
        content = "";
        var hashes = 0;
        while (hashes < line.Length && line[hashes] == '#') hashes++;
        if (hashes == 0 || hashes > 6 || hashes >= line.Length || line[hashes] != ' ') return false;
        level = hashes;
        content = line[(hashes + 1)..];
        return true;
    }

    /// <summary>引用の行(先頭の空白のあとに「>」)かを判定し、字下げの深さと中身に分ける。「> > 」のように重なった印は1つにまとめる。</summary>
    /// <param name="rawLine">1行(先頭の空白を含む)</param>
    /// <param name="level">字下げの深さ(先頭の空白2つで1段。最大3)</param>
    /// <param name="content">「>」と空白のあとの中身</param>
    /// <returns>引用の行ならtrue</returns>
    public static bool TryParseQuote(string rawLine, out int level, out string content)
    {
        level = 0;
        content = "";
        var indent = 0;
        while (indent < rawLine.Length && (rawLine[indent] == ' ' || rawLine[indent] == '\t')) indent++;
        if (indent >= rawLine.Length || rawLine[indent] != '>') return false;

        var body = rawLine[indent..];
        while (body.StartsWith('>')) body = body[1..].TrimStart(' ');
        level = Math.Min(rawLine[..indent].Sum(ch => ch == '\t' ? 4 : 1) / 2, 3);
        content = body;
        return true;
    }

    /// <summary>表の見出しと本文を分ける行(「|---|:---:|」など)かを判定する。</summary>
    /// <param name="line">先頭の空白を除いた1行</param>
    /// <returns>区切りの行ならtrue</returns>
    public static bool IsTableSeparator(string line)
    {
        if (!line.StartsWith('|')) return false;
        var cells = SplitTableRow(line);
        return cells.Count > 0 && cells.All(c => c.Length > 0 && c.Contains('-') && c.All(ch => ch is '-' or ':' or ' '));
    }

    /// <summary>区切り線の行(「---」「***」「___」を3文字以上。間の空白は可)かを判定する。</summary>
    /// <param name="line">先頭の空白を除いた1行</param>
    /// <returns>区切り線ならtrue</returns>
    public static bool IsHorizontalRule(string line)
    {
        var body = line.Replace(" ", "", StringComparison.Ordinal).TrimEnd();
        return body.Length >= 3 && (body.All(ch => ch == '-') || body.All(ch => ch == '*') || body.All(ch => ch == '_'));
    }

    /// <summary>表の1行をセルに分ける。両端の「|」は除き、「\|」はセルの中の文字「|」として扱う。</summary>
    /// <param name="line">先頭の空白を除いた1行</param>
    /// <returns>前後の空白を除いたセルの文字列</returns>
    public static IReadOnlyList<string> SplitTableRow(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var body = line.Trim();
        if (body.StartsWith('|')) body = body[1..];
        // 末尾の「|」は区切りとして外す(「\|」で終わるときは文字なので残す)
        if (body.EndsWith('|') && !body.EndsWith("\\|", StringComparison.Ordinal)) body = body[..^1];

        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '\\' && i + 1 < body.Length && body[i + 1] == '|')
            {
                current.Append('|');
                i++;
            }
            else if (body[i] == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(body[i]);
            }
        }
        cells.Add(current.ToString().Trim());
        return cells;
    }

    /// <summary>表の列幅を決めるための、文字列の見た目の幅(半角1・全角2)。太字とコードの記号は数えない。</summary>
    /// <param name="text">セルの文字列</param>
    /// <returns>半角換算の文字数</returns>
    public static int DisplayWidth(string text)
    {
        var plain = text.Replace("**", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal);
        return plain.Sum(ch => ch <= 'ÿ' || (ch >= '｡' && ch <= 'ﾟ') ? 1 : 2);
    }
}
