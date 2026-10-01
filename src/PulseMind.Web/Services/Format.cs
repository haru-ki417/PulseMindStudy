using System.Globalization;

namespace PulseMind.Web.Services;

/// <summary>画面に出す時間・日付の書き方をそろえる</summary>
public static class Format
{
    public static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");

    /// <summary>分 → 「2時間15分」「45分」「0分」</summary>
    public static string Minutes(double minutes)
    {
        int total = (int)Math.Round(Math.Max(0, minutes));
        int h = total / 60, m = total % 60;
        return h == 0 ? $"{m}分" : m == 0 ? $"{h}時間" : $"{h}時間{m}分";
    }

    /// <summary>分 → グラフ用の短い形「2.3h」「45m」</summary>
    public static string MinutesShort(double minutes) =>
        minutes >= 60 ? (minutes / 60).ToString("0.#", CultureInfo.InvariantCulture) + "h"
        : minutes >= 1 ? Math.Round(minutes).ToString(CultureInfo.InvariantCulture) + "m" : "";

    /// <summary>経過時間 → 「01:02:03」</summary>
    public static string Clock(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    /// <summary>「4月1日（水）」</summary>
    public static string Day(DateOnly date) => date.ToString("M月d日（ddd）", Japanese);

    /// <summary>「4/1」</summary>
    public static string ShortDay(DateOnly date) => date.ToString("M/d", Japanese);

    /// <summary>曜日の1文字「水」</summary>
    public static string Weekday(DateOnly date) => date.ToString("ddd", Japanese);

    public static string Time(DateTime local) => local.ToString("H:mm", Japanese);

    public static string Greeting(DateTime local) => local.Hour switch
    {
        >= 4 and < 11 => "おはようございます",
        >= 11 and < 18 => "こんにちは",
        _ => "こんばんは",
    };

    public static string Number(double value, string format = "N0") => value.ToString(format, Japanese);
}
