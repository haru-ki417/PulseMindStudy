namespace PulseMind.Web.Services;

/// <summary>試験までの残り日数と、今のペースでの見込み</summary>
public sealed record Countdown(string ExamName, DateOnly ExamDate, int DaysLeft, double? ProjectedHours)
{
    /// <summary>
    /// 残り日数（今日を含めず、試験の日まで）。見込みは「直近14日の1日平均 × 残り日数」。
    /// 将来を言い当てるものではないので、画面では「このペースなら」と前置きして見せる。
    /// </summary>
    public static Countdown? Create(string? name, DateOnly? date, DateOnly today, double recentDailyAverageMinutes)
    {
        if (string.IsNullOrWhiteSpace(name) || date is not DateOnly d || d < today) return null;
        int days = d.DayNumber - today.DayNumber;
        double? projected = recentDailyAverageMinutes > 0 ? recentDailyAverageMinutes * days / 60 : null;
        return new Countdown(name, d, days, projected);
    }
}
