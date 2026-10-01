using PulseMind.Core;
using PulseMind.Core.Records;
using PulseMind.Core.Time;

namespace PulseMind.Tests;

public class DailyRecordServiceTests
{
    private static readonly TimeZoneInfo Tokyo = TimeZones.Resolve("Asia/Tokyo");

    [Fact]
    public async Task 同じ日に2回保存すると上書きされる()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var service = new DailyRecordService(db.Factory, db.Time);
        var day = new DateOnly(2026, 4, 1);

        await service.SaveAsync(user, new DailyRecordInput(day, 6.5, 8000, 3, null), Tokyo, TestContext.Current.CancellationToken);
        await service.SaveAsync(user, new DailyRecordInput(day, 7, null, 4, " よく眠れた "), Tokyo, TestContext.Current.CancellationToken);

        var records = await service.ListAsync(user, day, day, TestContext.Current.CancellationToken);
        var record = Assert.Single(records);
        Assert.Equal(7, record.SleepHours);
        Assert.Null(record.Steps);
        Assert.Equal(4, record.Mood);
        Assert.Equal("よく眠れた", record.Note);
    }

    [Theory]
    [InlineData(25.0, null, null)]
    [InlineData(-1.0, null, null)]
    [InlineData(null, -5, null)]
    [InlineData(null, null, 6)]
    [InlineData(null, null, 0)]
    public void 範囲外の値は受け付けない(double? sleep, int? steps, int? mood)
    {
        var today = new DateOnly(2026, 4, 1);
        Assert.Throws<UserInputException>(() => DailyRecordService.Validate(new DailyRecordInput(today, sleep, steps, mood, null), today));
    }

    [Fact]
    public void 利用者のタイムゾーンで明日以降の日付は受け付けない()
    {
        var today = new DateOnly(2026, 4, 1);
        Assert.Throws<UserInputException>(() => DailyRecordService.Validate(new DailyRecordInput(today.AddDays(1), 7, null, null, null), today));
        DailyRecordService.Validate(new DailyRecordInput(today, 7, null, null, null), today);
    }
}
