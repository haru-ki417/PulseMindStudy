using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;
using PulseMind.Core.Study;
using PulseMind.Core.Time;

namespace PulseMind.Core.Insights;

/// <summary>1日分の値（分析の材料）</summary>
public sealed record DayFacts(DateOnly Date, double StudyMinutes, double? Focus, double? SleepHours, int? Mood, int? Steps);

/// <summary>2つの項目の関係</summary>
public sealed record RelationInsight(
    string Key, string Title, string XLabel, string YLabel, string XUnit, string YUnit,
    IReadOnlyList<(double X, double Y)> Points, Correlation? Correlation);

/// <summary>時間帯ごとの集中度</summary>
public sealed record TimeOfDayFocus(string Label, int StartHour, int Sessions, double AverageFocus, double TotalMinutes);

public sealed record InsightReport(
    DateOnly From, DateOnly To, int DaysWithStudy, int DaysWithSleep,
    IReadOnlyList<RelationInsight> Relations, IReadOnlyList<TimeOfDayFocus> TimeOfDay, IReadOnlyList<DayFacts> Days)
{
    /// <summary>関係を調べるのに必要な最低日数。少ないと偶然の偏りを「傾向」と見誤りやすい。</summary>
    public const int MinimumDays = 14;
}

/// <summary>
/// 記録から「自分の傾向」を調べる。
/// 方針: 数が少ないうちは結論を出さない、相関と因果を区別する、はっきりしない傾向は「はっきりしない」と伝える。
/// </summary>
public sealed class InsightService(IDbContextFactory<PulseMindDbContext> dbFactory, StudySessionService sessions, TimeProvider time)
{
    /// <summary>時間帯の区切り（開始時刻）</summary>
    private static readonly (string Label, int Start, int End)[] Slots =
    [
        ("朝（5〜9時）", 5, 9), ("午前（9〜12時）", 9, 12), ("午後（12〜17時）", 12, 17),
        ("夜（17〜21時）", 17, 21), ("深夜（21〜1時）", 21, 25), ("未明（1〜5時）", 1, 5),
    ];

    /// <summary>時間帯ごとの集中度を出すのに必要な、集中度付きの学習の数</summary>
    public const int MinimumSessionsPerSlot = 3;

    public async Task<InsightReport> BuildAsync(string userId, TimeZoneInfo zone, int days = 60, CancellationToken cancellationToken = default)
    {
        var today = TimeZones.LocalDate(time.GetUtcNow().UtcDateTime, zone);
        var from = today.AddDays(-(days - 1));
        var (fromUtc, toUtc) = TimeZones.LocalRangeUtc(from, today, zone);

        var minutes = await sessions.DailyMinutesAsync(userId, from, today, zone, cancellationToken);
        var list = await sessions.ListAsync(userId, fromUtc, toUtc, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var records = await db.DailyRecords.AsNoTracking()
            .Where(r => r.UserId == userId && r.Date >= from && r.Date <= today)
            .ToDictionaryAsync(r => r.Date, cancellationToken);

        // 集中度は、その日の学習の長さで重み付けした平均
        var focusByDay = list
            .Where(s => s.Focus is not null && s.EndedAtUtc is not null)
            .GroupBy(s => TimeZones.LocalDate(s.StartedAtUtc, zone))
            .ToDictionary(g => g.Key, g =>
            {
                double w = g.Sum(s => s.Duration!.Value.TotalMinutes);
                return w <= 0 ? g.Average(s => (double)s.Focus!.Value) : g.Sum(s => s.Focus!.Value * s.Duration!.Value.TotalMinutes) / w;
            });

        // 今日はまだ途中なので、関係の計算には使わない
        var facts = minutes.Keys.Where(d => d < today).Select(d =>
        {
            records.TryGetValue(d, out var r);
            return new DayFacts(d, minutes[d], focusByDay.TryGetValue(d, out double f) ? f : null, r?.SleepHours, r?.Mood, r?.Steps);
        }).ToList();

        // 勉強しなかった日（0 分）も含める。除くと「眠れなかった日は勉強しなかった」という関係が見えなくなるため
        var relations = new List<RelationInsight>
        {
            Relation("sleep-study", "睡眠時間と学習時間", "睡眠", "学習", "時間", "分",
                facts.Where(d => d.SleepHours is not null).Select(d => (d.SleepHours!.Value, d.StudyMinutes))),
            Relation("sleep-focus", "睡眠時間と集中度", "睡眠", "集中度", "時間", "",
                facts.Where(d => d.SleepHours is not null && d.Focus is not null).Select(d => (d.SleepHours!.Value, d.Focus!.Value))),
            Relation("mood-study", "気分と学習時間", "気分", "学習", "", "分",
                facts.Where(d => d.Mood is not null).Select(d => ((double)d.Mood!.Value, d.StudyMinutes))),
            Relation("steps-focus", "歩数と集中度", "歩数", "集中度", "歩", "",
                facts.Where(d => d.Steps is not null && d.Focus is not null).Select(d => ((double)d.Steps!.Value, d.Focus!.Value))),
        };

        var timeOfDay = Slots.Select(slot =>
        {
            var inSlot = list.Where(s => s.Focus is not null && s.EndedAtUtc is not null && InSlot(TimeZones.ToLocal(s.StartedAtUtc, zone).Hour, slot.Start, slot.End)).ToList();
            return new TimeOfDayFocus(slot.Label, slot.Start, inSlot.Count,
                inSlot.Count == 0 ? 0 : inSlot.Average(s => (double)s.Focus!.Value),
                inSlot.Sum(s => s.Duration!.Value.TotalMinutes));
        }).Where(t => t.Sessions > 0).ToList();

        return new InsightReport(from, today, facts.Count(d => d.StudyMinutes > 0), facts.Count(d => d.SleepHours is not null),
            relations, timeOfDay, facts);
    }

    private static bool InSlot(int hour, int start, int end) => end <= 24 ? hour >= start && hour < end : hour >= start || hour < end - 24;

    private static RelationInsight Relation(string key, string title, string x, string y, string xUnit, string yUnit, IEnumerable<(double, double)> points)
    {
        var list = points.ToList();
        var correlation = list.Count >= InsightReport.MinimumDays ? Statistics.Spearman(list) : null;
        return new RelationInsight(key, title, x, y, xUnit, yUnit, list, correlation);
    }
}
