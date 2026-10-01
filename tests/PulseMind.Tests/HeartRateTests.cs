using PulseMind.Core.Domain;
using PulseMind.Core.Health;

namespace PulseMind.Tests;

public class HeartRateTests
{
    private static DateTime At(int minute, int second) => new(2026, 4, 1, 11, minute, second, DateTimeKind.Utc);

    [Fact]
    public void 一分ごとにまとめ範囲外の値は捨てる()
    {
        var buckets = HeartRateAggregation.Aggregate(
        [
            new(At(0, 5), 70), new(At(0, 30), 80), new(At(0, 59), 0), // 0 はセンサーの読み取り失敗
            new(At(1, 10), 90), new(At(1, 20), 300),                   // 300 はありえない値
        ]);

        Assert.Equal(2, buckets.Count);
        Assert.Equal(new HeartRateBucket(At(0, 0), 75, 70, 80, 2), buckets[0]);
        Assert.Equal(new HeartRateBucket(At(1, 0), 90, 90, 90, 1), buckets[1]);
    }

    [Fact]
    public void まとめ同士を結合すると件数で重み付けした平均になる()
    {
        var combined = HeartRateAggregation.Combine(new HeartRateBucket(At(0, 0), 60, 55, 65, 3), new HeartRateBucket(At(0, 0), 80, 78, 82, 1));
        Assert.Equal(new HeartRateBucket(At(0, 0), 65, 55, 82, 4), combined);
    }

    [Fact]
    public async Task センサーからの送信は同じ一分に足し合わせる()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        await using var ctx = db.NewContext();
        var service = new HeartRateIngestionService(ctx, db.Time);

        await service.IngestAsync(user, DataSource.Arduino, [new(At(0, 10), 60), new(At(0, 20), 70)], HeartRateMergeMode.Accumulate, TestContext.Current.CancellationToken);
        await service.IngestAsync(user, DataSource.Arduino, [new(At(0, 40), 80)], HeartRateMergeMode.Accumulate, TestContext.Current.CancellationToken);

        var minute = Assert.Single(await service.GetMinutesAsync(user, At(0, 0), At(5, 0), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(70, minute.AverageBpm, 6);
        Assert.Equal(3, minute.SampleCount);
        Assert.Equal(DateTimeKind.Utc, minute.MinuteUtc.Kind);
    }

    [Fact]
    public async Task ヘルスケアの取り込みは何度行っても結果が変わらない()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        await using var ctx = db.NewContext();
        var service = new HeartRateIngestionService(ctx, db.Time);
        HeartRateSample[] export = [new(At(0, 10), 60), new(At(0, 20), 70), new(At(2, 0), 90)];

        await service.IngestAsync(user, DataSource.AppleHealth, export, HeartRateMergeMode.Replace, TestContext.Current.CancellationToken);
        var second = await service.IngestAsync(user, DataSource.AppleHealth, export, HeartRateMergeMode.Replace, TestContext.Current.CancellationToken);

        Assert.Equal(new HeartRateIngestResult(3, 0, 2), second);
        var minutes = await service.GetMinutesAsync(user, At(0, 0), At(5, 0), DataSource.AppleHealth, TestContext.Current.CancellationToken);
        Assert.Equal([2, 1], minutes.Select(m => m.SampleCount));
        Assert.Equal(65, minutes[0].AverageBpm, 6);
    }

    [Fact]
    public async Task 入手元が違えば同じ一分でも別々に保存する()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        await using var ctx = db.NewContext();
        var service = new HeartRateIngestionService(ctx, db.Time);

        await service.IngestAsync(user, DataSource.Arduino, [new(At(0, 10), 60)], HeartRateMergeMode.Accumulate, TestContext.Current.CancellationToken);
        await service.IngestAsync(user, DataSource.AppleHealth, [new(At(0, 10), 64)], HeartRateMergeMode.Replace, TestContext.Current.CancellationToken);

        Assert.Equal(2, (await service.GetMinutesAsync(user, At(0, 0), At(5, 0), cancellationToken: TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task 未来の時刻や時差の分からない時刻は捨てる()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        await using var ctx = db.NewContext();
        var service = new HeartRateIngestionService(ctx, db.Time);
        var now = db.Time.GetUtcNow().UtcDateTime;

        var result = await service.IngestAsync(user, DataSource.Arduino,
        [
            new(now.AddMinutes(-1), 70),
            new(now.AddHours(1), 70),                                         // 未来
            new(DateTime.SpecifyKind(now.AddMinutes(-2), DateTimeKind.Unspecified), 70), // 時差不明
            new(new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc), 70),     // 古すぎる
        ], HeartRateMergeMode.Accumulate, TestContext.Current.CancellationToken);

        Assert.Equal(new HeartRateIngestResult(1, 3, 1), result);
    }
}
