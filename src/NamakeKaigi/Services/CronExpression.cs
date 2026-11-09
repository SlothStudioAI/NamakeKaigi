namespace NamakeKaigi.Services;

/// <summary>
/// 5項目のcron式(分 時 日 月 曜日)を読み、ある時刻に当たるか・次に当たる時刻はいつかを答える。
/// 使える書き方: * / 数 / 範囲(1-5) / 一覧(0,2) / 刻み(*&#47;15, 1-10/2)。曜日は 0〜6(0=日曜。7も日曜)。
/// 日と曜日の両方を指定したときは、どちらかに当たれば実行する(標準のcronと同じ)。時刻は、パソコンの時計(ローカル)で見る。
/// </summary>
public sealed class CronExpression
{
    private readonly bool[] _minutes;
    private readonly bool[] _hours;
    private readonly bool[] _days;
    private readonly bool[] _months;
    private readonly bool[] _weekdays;
    private readonly bool _dayAny;
    private readonly bool _weekdayAny;

    private CronExpression(string text, bool[] minutes, bool[] hours, bool[] days, bool[] months, bool[] weekdays,
        bool dayAny, bool weekdayAny)
    {
        Text = text;
        _minutes = minutes;
        _hours = hours;
        _days = days;
        _months = months;
        _weekdays = weekdays;
        _dayAny = dayAny;
        _weekdayAny = weekdayAny;
    }

    /// <summary>もとの式(整えた文字列)。</summary>
    public string Text { get; }

    /// <summary>cron式を読む。</summary>
    /// <param name="text">cron式(例: 0 0,2 * * *)。先頭に「CRON_TZ=…」があっても、無視する(時刻はこのパソコンの時計で見る)</param>
    /// <returns>読み取った式</returns>
    /// <exception cref="FormatException">書き方が正しくないとき</exception>
    public static CronExpression Parse(string text)
    {
        if (!TryParse(text, out var expr, out var error)) throw new FormatException(error);
        return expr!;
    }

    /// <summary>cron式を読む(失敗しても例外にしない)。</summary>
    /// <param name="text">cron式</param>
    /// <param name="expression">読み取れた式(失敗ならnull)</param>
    /// <param name="error">失敗の理由(成功なら空)</param>
    /// <returns>読み取れたらtrue</returns>
    public static bool TryParse(string? text, out CronExpression? expression, out string error)
    {
        expression = null;
        var parts = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("CRON_TZ=", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (parts.Length != 5)
        {
            error = $"cron式は「分 時 日 月 曜日」の5項目で書いてください(いまは{parts.Length}項目): {text}";
            return false;
        }
        try
        {
            var weekdays = ParseField(parts[4], 0, 7, "曜日");
            if (weekdays[7]) weekdays[0] = true; // 7も日曜
            expression = new CronExpression(string.Join(' ', parts),
                ParseField(parts[0], 0, 59, "分"), ParseField(parts[1], 0, 23, "時"),
                ParseField(parts[2], 1, 31, "日"), ParseField(parts[3], 1, 12, "月"), weekdays,
                dayAny: parts[2] == "*", weekdayAny: parts[4] == "*");
            error = "";
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>その時刻(分まで)が、式に当たるか。</summary>
    /// <param name="time">調べる時刻(秒以下は見ない)</param>
    /// <returns>当たればtrue</returns>
    public bool Matches(DateTime time)
    {
        return _minutes[time.Minute] && _hours[time.Hour] && _months[time.Month] && DayMatches(time);
    }

    /// <summary>指定した時刻より後で、最初に当たる時刻を返す。</summary>
    /// <param name="after">この時刻より後を探す(秒以下は切り捨てて、次の分から探す)</param>
    /// <param name="limitDays">探す日数の上限(これを超えたらnull。うるう日だけの式などのため)</param>
    /// <returns>次に当たる時刻(見つからなければnull)</returns>
    public DateTime? Next(DateTime after, int limitDays = 366 * 5)
    {
        var t = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, after.Kind).AddMinutes(1);
        var end = t.AddDays(limitDays);
        while (t < end)
        {
            if (!_months[t.Month]) { t = new DateTime(t.Year, t.Month, 1, 0, 0, 0, t.Kind).AddMonths(1); continue; }
            if (!DayMatches(t)) { t = t.Date.AddDays(1); continue; }
            if (!_hours[t.Hour]) { t = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Kind).AddHours(1); continue; }
            if (!_minutes[t.Minute]) { t = t.AddMinutes(1); continue; }
            return t;
        }
        return null;
    }

    /// <summary>日と曜日の条件に当たるか。両方を指定したときは「どちらか」、片方だけ(もう片方は *)なら、その片方で判断する。</summary>
    private bool DayMatches(DateTime t)
    {
        var dayHit = _days[t.Day];
        var weekdayHit = _weekdays[(int)t.DayOfWeek];
        if (_dayAny && _weekdayAny) return true;
        if (_dayAny) return weekdayHit;
        if (_weekdayAny) return dayHit;
        return dayHit || weekdayHit;
    }

    /// <summary>1項目(例: 0,2 / 1-5 / *&#47;15)を読み、使う数の印の並びにする。</summary>
    private static bool[] ParseField(string field, int min, int max, string label)
    {
        var set = new bool[Math.Max(max, 59) + 1]; // 添字で直接引けるよう、最大の項目(分=59)の大きさにそろえる
        foreach (var item in field.Split(','))
        {
            if (item.Length == 0) throw new FormatException($"{label}の書き方が正しくありません: {field}");
            var step = 1;
            var range = item;
            var slash = item.IndexOf('/');
            if (slash >= 0)
            {
                range = item[..slash];
                if (!int.TryParse(item[(slash + 1)..], out step) || step < 1)
                    throw new FormatException($"{label}の刻みが正しくありません: {item}");
            }
            int from, to;
            if (range == "*")
            {
                from = min;
                to = max;
            }
            else if (range.Contains('-'))
            {
                var ends = range.Split('-');
                if (ends.Length != 2 || !int.TryParse(ends[0], out from) || !int.TryParse(ends[1], out to))
                    throw new FormatException($"{label}の範囲が正しくありません: {item}");
            }
            else
            {
                if (!int.TryParse(range, out from)) throw new FormatException($"{label}は数字で書いてください: {item}");
                to = slash >= 0 ? max : from; // 「5/10」は 5 から最後まで10刻み
            }
            if (from < min || to > max || from > to)
                throw new FormatException($"{label}は{min}〜{max}の範囲で書いてください: {item}");
            for (var i = from; i <= to; i += step) set[i] = true;
        }
        return set;
    }
}
