namespace PulseMind.Core.Time;

/// <summary>
/// 利用者のタイムゾーンでの「日付」を扱う。
/// 例: 日本時間の 0:30 に終わった勉強は、UTC では前日の 15:30 だが、利用者にとっては「今日」の記録になる。
/// </summary>
public static class TimeZones
{
    private static readonly TimeZoneInfo Fallback = TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Tokyo", out var tokyo) ? tokyo : TimeZoneInfo.Utc;

    /// <summary>IANA 形式の ID（例: Asia/Tokyo）からタイムゾーンを得る。見つからなければ日本時間とする。</summary>
    public static TimeZoneInfo Resolve(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone) ? zone : Fallback;

    public static bool IsKnown(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) => DateOnly.FromDateTime(ToLocal(utc, zone));

    /// <summary>利用者のタイムゾーンでのある日の 0:00 を UTC で返す</summary>
    public static DateTime StartOfLocalDayUtc(DateOnly date, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // 夏時間の切り替えで 0:00 が存在しない地域では、存在する時刻まで進める
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(30);

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>ある日（利用者のタイムゾーン）を UTC の範囲 [開始, 終了) で返す</summary>
    public static (DateTime StartUtc, DateTime EndUtc) LocalDayRangeUtc(DateOnly date, TimeZoneInfo zone) =>
        (StartOfLocalDayUtc(date, zone), StartOfLocalDayUtc(date.AddDays(1), zone));

    /// <summary>[from, to] の日付（両端を含む）を UTC の範囲で返す</summary>
    public static (DateTime StartUtc, DateTime EndUtc) LocalRangeUtc(DateOnly from, DateOnly to, TimeZoneInfo zone) =>
        (StartOfLocalDayUtc(from, zone), StartOfLocalDayUtc(to.AddDays(1), zone));
}
