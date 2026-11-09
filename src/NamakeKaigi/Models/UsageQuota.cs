using System.Text.Json;

namespace NamakeKaigi.Models;

/// <summary>Claude CLIの rate_limit_event から読み取った使用量の枠(5時間・7日)。</summary>
/// <param name="FiveHourPercent">5時間枠の使用率(0〜100%)</param>
/// <param name="FiveHourReset">5時間枠のリセット時刻(ローカル時刻、不明ならnull)</param>
/// <param name="SevenDayPercent">7日枠の使用率(0〜100%)</param>
/// <param name="SevenDayReset">7日枠のリセット時刻(ローカル時刻、不明ならnull)</param>
public sealed record UsageQuota(
    int FiveHourPercent,
    DateTimeOffset? FiveHourReset,
    int SevenDayPercent,
    DateTimeOffset? SevenDayReset)
{
    /// <summary>どちらかの枠が80%以上ならtrue(警告色で強調する)。</summary>
    public bool IsHigh => FiveHourPercent >= 80 || SevenDayPercent >= 80;

    /// <summary>サイドバーのカードに出す1行の短い表示。</summary>
    public string ShortText => $"枠: 5時間 {FiveHourPercent}% · 7日 {SevenDayPercent}%";

    /// <summary>マウスを乗せたときに出す詳しい説明(リセット時刻つき)。</summary>
    public string TooltipText
    {
        get
        {
            var r5 = FiveHourReset?.ToLocalTime().ToString("HH:mm") ?? "不明";
            var r7 = SevenDayReset?.ToLocalTime().ToString("MM/dd HH:mm") ?? "不明";
            return $"Claude 使用量の枠 (アカウント全体の合算)\n" +
                   $"・5時間枠: {FiveHourPercent}% ({r5} リセット)\n" +
                   $"・7日枠: {SevenDayPercent}% ({r7} リセット)";
        }
    }

    /// <summary>通知用に1つの文字列へパックする。</summary>
    public string Encode() => $"{ShortText}\t{(IsHigh ? "1" : "0")}\t{TooltipText}";

    /// <summary>パックされた文字列から(短い表示, 80%以上か, ツールチップ文)を取り出す。</summary>
    public static (string ShortText, bool IsHigh, string Tooltip) Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return ("", false, "");
        var parts = encoded.Split('\t', 3);
        if (parts.Length < 3) return (encoded, false, encoded);
        return (parts[0], parts[1] == "1", parts[2]);
    }

    /// <summary>rate_limit_event のJSON要素から使用量の枠を取り出す(入っていなければnull)。</summary>
    /// <param name="root">JSONのルート要素</param>
    public static UsageQuota? TryParse(JsonElement root)
    {
        if (!root.TryGetProperty("rate_limit_info", out var info)) return null;
        if (!info.TryGetProperty("unifiedWindows", out var windows)) return null;

        var has5 = TryReadWindow(windows, "five_hour", out var pct5, out var reset5);
        var has7 = TryReadWindow(windows, "seven_day", out var pct7, out var reset7);
        if (!has5 && !has7) return null;

        return new UsageQuota(pct5, reset5, pct7, reset7);
    }

    private static bool TryReadWindow(JsonElement windows, string name, out int percent, out DateTimeOffset? reset)
    {
        percent = 0;
        reset = null;
        if (!windows.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return false;

        if (w.TryGetProperty("utilization", out var u) && u.TryGetDouble(out var ratio))
        {
            percent = (int)Math.Round(ratio * 100.0, MidpointRounding.AwayFromZero);
        }
        else
        {
            return false;
        }

        if (w.TryGetProperty("resetsAt", out var r) && r.TryGetInt64(out var unixSec) && unixSec > 0)
        {
            try { reset = DateTimeOffset.FromUnixTimeSeconds(unixSec).ToLocalTime(); }
            catch (ArgumentOutOfRangeException) { reset = null; }
        }
        return true;
    }
}
