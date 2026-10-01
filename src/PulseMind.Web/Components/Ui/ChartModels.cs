namespace PulseMind.Web.Components.Ui;

/// <summary>棒グラフの1本</summary>
public sealed record BarItem(string Label, double Value, string Tooltip, bool Highlight = false, string? SubLabel = null);

/// <summary>心拍グラフの1点（利用者のタイムゾーンでの時刻）</summary>
public sealed record HeartRatePoint(DateTime Local, double Average, double Min, double Max);

/// <summary>「1日のリズム」に置く学習の1区間（利用者のタイムゾーンでの時刻）</summary>
public sealed record RhythmBlock(DateTime StartLocal, DateTime EndLocal, string Subject, bool Running = false);

/// <summary>週の振り返りの中身。DailyMinutes は月曜〜日曜の 7 日分</summary>
public sealed record WeeklyReview(
    DateOnly WeekStart, double[] DailyMinutes, double? PreviousTotalMinutes, IReadOnlyList<(string Subject, double Minutes)> Subjects,
    int StudyDays, int StreakDays, double? AverageSleepHours, int GoalMinutes);
