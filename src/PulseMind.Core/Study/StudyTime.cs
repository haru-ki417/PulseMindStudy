using PulseMind.Core.Time;

namespace PulseMind.Core.Study;

/// <summary>学習時間の集計</summary>
public static class StudyTime
{
    /// <summary>
    /// 学習を、利用者のタイムゾーンでの日付ごとの分数に分ける。
    /// 日付をまたいだ勉強（例: 23:30〜0:30）は、それぞれの日に 30 分ずつ数える。
    /// </summary>
    public static SortedDictionary<DateOnly, double> MinutesPerLocalDay(
        IEnumerable<(DateTime StartUtc, DateTime EndUtc)> sessions, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var result = new SortedDictionary<DateOnly, double>();

        foreach (var (startUtc, endUtc) in sessions)
        {
            if (endUtc <= startUtc) continue;

            DateTime cursor = startUtc;
            while (cursor < endUtc)
            {
                DateOnly day = TimeZones.LocalDate(cursor, zone);
                DateTime nextDayStart = TimeZones.StartOfLocalDayUtc(day.AddDays(1), zone);
                DateTime segmentEnd = nextDayStart < endUtc ? nextDayStart : endUtc;

                result[day] = result.GetValueOrDefault(day) + (segmentEnd - cursor).TotalMinutes;
                cursor = segmentEnd;
            }
        }

        return result;
    }

    /// <summary>
    /// 期間 [fromUtc, toUtc) に入っている部分だけを数える（期間の外にはみ出した部分は切り捨てる）。
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc)? Clip(DateTime startUtc, DateTime endUtc, DateTime fromUtc, DateTime toUtc)
    {
        DateTime s = startUtc > fromUtc ? startUtc : fromUtc;
        DateTime e = endUtc < toUtc ? endUtc : toUtc;
        return e > s ? (s, e) : null;
    }

    /// <summary>
    /// 何日続けて勉強しているか。今日まだ勉強していなくても、昨日まで続いていれば途切れていないとみなす。
    /// </summary>
    public static int Streak(IReadOnlyDictionary<DateOnly, double> minutesPerDay, DateOnly today, double minimumMinutes = 1)
    {
        ArgumentNullException.ThrowIfNull(minutesPerDay);
        var day = minutesPerDay.GetValueOrDefault(today) >= minimumMinutes ? today : today.AddDays(-1);
        int streak = 0;
        while (minutesPerDay.GetValueOrDefault(day) >= minimumMinutes)
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }
}
