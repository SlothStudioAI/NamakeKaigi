using System.Globalization;
using System.Text;

namespace NamakeKaigi.Services;

/// <summary>
/// TOML の、最初の表の見出し([...])より前にある「キー = 文字列」だけを読む小さな読み取り。
/// メンバー定義(Codex の .codex/agents/*.toml)の見出しを読むためのもので、TOML のすべてには対応しない。
/// </summary>
public static class TomlStrings
{
    private const string BasicMulti = "\"\"\"";
    private const string LiteralMulti = "'''";

    /// <summary>キーと文字列の値を読む。文字列でない値(数・真偽・配列など)は読み飛ばす。</summary>
    /// <param name="text">TOML の全文(改行は \n にそろえたもの)</param>
    /// <returns>キーと値(キーの大文字・小文字は区別しない)</returns>
    public static Dictionary<string, string> Read(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < text.Length)
        {
            var lineEnd = text.IndexOf('\n', i);
            if (lineEnd < 0) lineEnd = text.Length;
            var line = text[i..lineEnd].Trim();
            if (line.StartsWith('[')) break; // 表の中の項目は、見出しではない
            var eq = text.IndexOf('=', i, lineEnd - i);
            if (line.Length == 0 || line.StartsWith('#') || eq < 0)
            {
                i = lineEnd + 1;
                continue;
            }

            var key = text[i..eq].Trim().Trim('"', '\'');
            var start = eq + 1;
            while (start < text.Length && text[start] is ' ' or '\t') start++;
            var (value, next) = ReadString(text, start);
            if (value != null && key.Length > 0) fields[key] = value;

            if (next <= lineEnd + 1)
            {
                i = lineEnd + 1;
                continue;
            }
            // 複数行の文字列なら、閉じたところの行の終わりから続ける
            var after = text.IndexOf('\n', Math.Min(next, text.Length));
            i = after < 0 ? text.Length : after + 1;
        }
        return fields;
    }

    /// <summary>文字列を1つ読む("..."・'...'・"""..."""・'''...''' の4つの書き方)。</summary>
    /// <param name="text">TOML の全文</param>
    /// <param name="start">値の開始位置</param>
    /// <returns>読んだ値(文字列でなければ null)と、読み終えた次の位置</returns>
    private static (string? Value, int Next) ReadString(string text, int start)
    {
        if (start >= text.Length) return (null, start);
        foreach (var quote in new[] { BasicMulti, LiteralMulti })
        {
            if (string.CompareOrdinal(text, start, quote, 0, quote.Length) != 0) continue;
            var bodyStart = start + quote.Length;
            if (bodyStart < text.Length && text[bodyStart] == '\n') bodyStart++; // 開きの直後の改行は、値に含めない
            var end = FindClose(text, bodyStart, quote);
            var raw = text[bodyStart..end];
            return (quote == LiteralMulti ? raw : Unescape(raw, multiline: true), Math.Min(end + quote.Length, text.Length));
        }

        var q = text[start];
        if (q is not ('"' or '\'')) return (null, start);
        var j = start + 1;
        for (; j < text.Length && text[j] != q && text[j] != '\n'; j++)
        {
            if (q == '"' && text[j] == '\\' && j + 1 < text.Length && text[j + 1] != '\n') j++; // \" で閉じない
        }
        var body = text[(start + 1)..Math.Min(j, text.Length)];
        return (q == '"' ? Unescape(body, multiline: false) : body, j + 1);
    }

    /// <summary>複数行の文字列の閉じ(""" または ''')の位置を探す。基本の書き方では、\ の直後の引用符は閉じとみなさない。</summary>
    /// <param name="text">TOML の全文</param>
    /// <param name="from">探し始める位置</param>
    /// <param name="quote">閉じの引用符</param>
    /// <returns>閉じの位置(見つからなければ全文の終わり)</returns>
    private static int FindClose(string text, int from, string quote)
    {
        for (var k = from; k <= text.Length - quote.Length; k++)
        {
            if (quote == BasicMulti && text[k] == '\\')
            {
                k++;
                continue;
            }
            if (string.CompareOrdinal(text, k, quote, 0, quote.Length) == 0) return k;
        }
        return text.Length;
    }

    /// <summary>基本の文字列の、\ で始まる書き方(\n・\t・\"・\\・\uXXXX など)を元の文字に戻す。</summary>
    /// <param name="raw">引用符の中身</param>
    /// <param name="multiline">複数行の書き方か(行末の \ で、次の行とつなぐ)</param>
    /// <returns>戻した文字列</returns>
    private static string Unescape(string raw, bool multiline)
    {
        var sb = new StringBuilder(raw.Length);
        for (var k = 0; k < raw.Length; k++)
        {
            if (raw[k] != '\\' || k + 1 >= raw.Length)
            {
                sb.Append(raw[k]);
                continue;
            }
            var c = raw[++k];
            switch (c)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case 'u' or 'U':
                    var len = c == 'u' ? 4 : 8;
                    if (raw.Length - k - 1 >= len
                        && int.TryParse(raw.AsSpan(k + 1, len), NumberStyles.HexNumber, null, out var code)
                        && code is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF))
                    {
                        sb.Append(char.ConvertFromUtf32(code));
                        k += len;
                    }
                    else
                    {
                        sb.Append('\\').Append(c);
                    }
                    break;
                case '\n' when multiline:
                    // 行末の \ は、次の行の先頭の空白まで、まとめて消す
                    while (k + 1 < raw.Length && raw[k + 1] is ' ' or '\t' or '\n') k++;
                    break;
                case ' ' or '\t' when multiline && IsLineEndBackslash(raw, k):
                    // 行末の \ の後ろに空白があっても、行末とみなす
                    while (k + 1 < raw.Length && raw[k + 1] is ' ' or '\t' or '\n') k++;
                    break;
                default:
                    sb.Append('\\').Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>位置から行の終わりまでが空白だけで、その後ろに改行があるか(行末の \ の判定)。</summary>
    /// <param name="raw">引用符の中身</param>
    /// <param name="from">\ の直後の位置</param>
    /// <returns>行末の \ ならtrue</returns>
    private static bool IsLineEndBackslash(string raw, int from)
    {
        for (var k = from; k < raw.Length; k++)
        {
            if (raw[k] == '\n') return true;
            if (raw[k] is not (' ' or '\t')) return false;
        }
        return false;
    }
}
