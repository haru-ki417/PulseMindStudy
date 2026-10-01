using PulseMind.Core.Insights;
using PulseMind.Core.Records;
using PulseMind.Core.Study;
using PulseMind.Core.Time;

namespace PulseMind.Tests;

public class StatisticsTests
{
    [Fact]
    public void 同じ値には順位の平均を付ける() =>
        Assert.Equal([1.0, 2.5, 2.5, 4.0], Statistics.Ranks([10, 20, 20, 30]));

    [Fact]
    public void 順位がそろって増えれば相関は1()
    {
        var c = Statistics.Spearman([(1, 10), (2, 40), (3, 41), (4, 90), (5, 100)]);
        Assert.NotNull(c);
        Assert.Equal(1, c.R, 6);
    }

    [Fact]
    public void 既知のデータで教科書どおりの値になる()
    {
        // 順位の差の二乗和 d² = 0+1+1+0+... を使う式 1 - 6Σd²/(n(n²-1)) と一致することを確かめる
        (double, double)[] data = [(1, 2), (2, 1), (3, 4), (4, 3), (5, 6), (6, 5), (7, 8), (8, 7)];
        var c = Statistics.Spearman(data)!;
        double expected = 1 - 6.0 * 8 / (8 * (64 - 1));
        Assert.Equal(expected, c.R, 6);
        Assert.True(c.Low < c.R && c.R < c.High);
    }

    [Fact]
    public void データが少ないと信頼区間が広く0をまたぐ()
    {
        var c = Statistics.Spearman([(1, 2), (2, 1), (3, 4), (4, 3), (5, 5)])!;
        Assert.True(c.R > 0.5);
        Assert.False(c.IsClear);
    }

    [Fact]
    public void 値がすべて同じなら計算しない()
    {
        Assert.Null(Statistics.Spearman([(1, 5), (2, 5), (3, 5), (4, 5)]));
        Assert.Null(Statistics.Spearman([(1, 5), (2, 6), (3, 7)]));
    }
}

public class InsightServiceTests
{
    private static readonly TimeZoneInfo Tokyo = TimeZones.Resolve("Asia/Tokyo");

    [Fact]
    public async Task 日数が足りないうちは関係を計算しない()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var service = new InsightService(db.Factory, new StudySessionService(db.Factory, db.Time), db.Time);

        var report = await service.BuildAsync(user, Tokyo, cancellationToken: TestContext.Current.CancellationToken);
        Assert.All(report.Relations, r => Assert.Null(r.Correlation));
    }

    [Fact]
    public async Task よく眠った日ほど長く勉強していれば正の傾向が出る()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var studies = new StudySessionService(db.Factory, db.Time);
        var records = new DailyRecordService(db.Factory, db.Time);
        var today = TimeZones.LocalDate(db.Time.GetUtcNow().UtcDateTime, Tokyo);

        for (int i = 1; i <= 20; i++)
        {
            var day = today.AddDays(-i);
            double sleep = 5 + (i % 5) * 0.6;
            await records.SaveAsync(user, new DailyRecordInput(day, sleep, null, 3, null), Tokyo, TestContext.Current.CancellationToken);
            var start = TimeZones.StartOfLocalDayUtc(day, Tokyo).AddHours(18);
            await studies.AddManualAsync(user, new ManualStudyInput(start, start.AddMinutes(30 + sleep * 15), "数学", Focus: 3), TestContext.Current.CancellationToken);
        }

        var report = await new InsightService(db.Factory, studies, db.Time).BuildAsync(user, Tokyo, cancellationToken: TestContext.Current.CancellationToken);
        var relation = report.Relations.Single(r => r.Key == "sleep-study");

        Assert.Equal(20, relation.Points.Count);
        Assert.NotNull(relation.Correlation);
        Assert.True(relation.Correlation.R > 0.9);
        Assert.True(relation.Correlation.IsClear);
        var evening = Assert.Single(report.TimeOfDay);
        Assert.Equal(17, evening.StartHour);
        Assert.Equal(20, evening.Sessions);
    }
}
