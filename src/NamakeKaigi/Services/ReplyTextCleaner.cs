namespace NamakeKaigi.Services;

/// <summary>AIの返答の先頭に出ることがある「担当: …」の行(Sloth Studio 共通ルールの名残)を取り除く。</summary>
public static class ReplyTextCleaner
{
    /// <summary>返答の先頭行が「担当:」または「担当：」で始まる場合、その行と直後の空行を取り除く。</summary>
    /// <param name="text">元の返答文</param>
    /// <returns>整形後の返答文</returns>
    public static string StripLeadingRoleHeader(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var trimmedStart = text.TrimStart('\r', '\n', ' ', '\t');
        if (!trimmedStart.StartsWith("担当:", StringComparison.Ordinal) &&
            !trimmedStart.StartsWith("担当：", StringComparison.Ordinal))
        {
            return text;
        }

        var newlineIdx = trimmedStart.IndexOf('\n');
        if (newlineIdx < 0)
        {
            // まだ1行目の途中(ストリーミング中)なら、その行が終わるまで表示を控える
            return "";
        }

        return trimmedStart[(newlineIdx + 1)..].TrimStart('\r', '\n');
    }
}
