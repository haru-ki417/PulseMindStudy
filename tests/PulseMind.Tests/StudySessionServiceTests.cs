using PulseMind.Core.Domain;
using PulseMind.Core;
using PulseMind.Core.Study;
using PulseMind.Core.Time;

namespace PulseMind.Tests;

public class StudySessionServiceTests
{
    private static readonly TimeZoneInfo Tokyo = TimeZones.Resolve("Asia/Tokyo");

    [Fact]
    public async Task タイマーを開始して終了すると経過時間が記録される()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var service = new StudySessionService(db.Factory, db.Time);

        await service.StartAsync(user, "  数学  ", TestContext.Current.CancellationToken);
        db.Time.Advance(TimeSpan.FromMinutes(50));
        var result = await service.StopAsync(user, focus: 4, note: "微分", TestContext.Current.CancellationToken);

        Assert.False(result.WasCapped);
        Assert.Equal("数学", result.Session.Subject);
        Assert.Equal(TimeSpan.FromMinutes(50), result.Session.Duration);
        Assert.Equal(4, result.Session.Focus);
        Assert.Null(await service.GetRunningAsync(user, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task 計測中にもう一度開始することはできない()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var service = new StudySessionService(db.Factory, db.Time);

        await service.StartAsync(user, "英語", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<UserInputException>(() => service.StartAsync(user, "数学", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task 止め忘れたタイマーは12時間で区切る()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var service = new StudySessionService(db.Factory, db.Time);

        await service.StartAsync(user, "物理", TestContext.Current.CancellationToken);
        db.Time.Advance(TimeSpan.FromHours(30));
        var result = await service.StopAsync(user, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.WasCapped);
        Assert.Equal(StudySessionService.MaxSessionLength, result.Session.Duration);
    }

    [Fact]
    public async Task 時間帯が重なる手入力は受け付けない()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var service = new StudySessionService(db.Factory, db.Time);
        var now = db.Time.GetUtcNow().UtcDateTime;

        await service.AddManualAsync(user, new ManualStudyInput(now.AddHours(-3), now.AddHours(-2), "化学"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UserInputException>(() =>
            service.AddManualAsync(user, new ManualStudyInput(now.AddHours(-2.5), now.AddHours(-1), "生物"), TestContext.Current.CancellationToken));

        // ちょうど続く時間帯（前の終わり = 次の始まり）は重なりではない
        await service.AddManualAsync(user, new ManualStudyInput(now.AddHours(-2), now.AddHours(-1), "生物"), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(-1, -2)]     // 終了が開始より前
    [InlineData(-14, -1)]    // 13 時間（長すぎる）
    [InlineData(1, 2)]       // 未来
    public async Task おかしな時間の手入力は受け付けない(double startHours, double endHours)
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var service = new StudySessionService(db.Factory, db.Time);
        var now = db.Time.GetUtcNow().UtcDateTime;

        await Assert.ThrowsAsync<UserInputException>(() =>
            service.AddManualAsync(user, new ManualStudyInput(now.AddHours(startHours), now.AddHours(endHours), "国語"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task 他の人の記録は削除できない()
    {
        await using var db = await TestDatabase.CreateAsync();
        string alice = await db.AddUserAsync("alice@example.com");
        string bob = await db.AddUserAsync("bob@example.com");
        var service = new StudySessionService(db.Factory, db.Time);
        var now = db.Time.GetUtcNow().UtcDateTime;

        var session = await service.AddManualAsync(alice, new ManualStudyInput(now.AddHours(-2), now.AddHours(-1), "数学"), TestContext.Current.CancellationToken);

        Assert.False(await service.DeleteAsync(bob, session.Id, TestContext.Current.CancellationToken));
        Assert.True(await service.DeleteAsync(alice, session.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task 日ごとの学習時間は記録の無い日も0として含め計測中の分も数える()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var service = new StudySessionService(db.Factory, db.Time);
        var now = db.Time.GetUtcNow().UtcDateTime; // 日本時間 4/1 21:00

        await service.AddManualAsync(user, new ManualStudyInput(now.AddDays(-2), now.AddDays(-2).AddMinutes(90), "数学"), TestContext.Current.CancellationToken);
        await service.StartAsync(user, "英語", TestContext.Current.CancellationToken);
        db.Time.Advance(TimeSpan.FromMinutes(20));

        var minutes = await service.DailyMinutesAsync(user, new DateOnly(2026, 3, 29), new DateOnly(2026, 4, 1), Tokyo, TestContext.Current.CancellationToken);

        Assert.Equal(4, minutes.Count);
        Assert.Equal(0, minutes[new DateOnly(2026, 3, 29)]);
        Assert.Equal(90, minutes[new DateOnly(2026, 3, 30)], 6);
        Assert.Equal(0, minutes[new DateOnly(2026, 3, 31)]);
        Assert.Equal(20, minutes[new DateOnly(2026, 4, 1)], 6);
    }
}

public class StudySessionEdgeTests
{
    [Fact]
    public async Task 計測中の学習は1人1つまでとデータベースでも保証する()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var now = db.Time.GetUtcNow().UtcDateTime;

        await using var ctx = db.NewContext();
        ctx.StudySessions.Add(new StudySession { UserId = user, StartedAtUtc = now, CreatedAtUtc = now });
        ctx.StudySessions.Add(new StudySession { UserId = user, StartedAtUtc = now.AddSeconds(1), CreatedAtUtc = now });
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => ctx.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task 止め忘れのタイマーがあっても12時間より後の時間帯は手入力できる()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var service = new StudySessionService(db.Factory, db.Time);

        await service.StartAsync(user, "数学", TestContext.Current.CancellationToken);
        db.Time.Advance(TimeSpan.FromHours(30));
        var now = db.Time.GetUtcNow().UtcDateTime;

        // 開始から 12 時間以内（止め忘れの範囲）は重なりとして断る
        await Assert.ThrowsAsync<UserInputException>(() =>
            service.AddManualAsync(user, new ManualStudyInput(now.AddHours(-25), now.AddHours(-24), "英語"), TestContext.Current.CancellationToken));
        // それより後は記録できる
        await service.AddManualAsync(user, new ManualStudyInput(now.AddHours(-2), now.AddHours(-1), "英語"), TestContext.Current.CancellationToken);
    }
}

public class StreakServiceTests
{
    [Fact]
    public async Task 一年を超えて続いている連続日数も数えられる()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var zone = PulseMind.Core.Time.TimeZones.Resolve("Asia/Tokyo");
        var today = PulseMind.Core.Time.TimeZones.LocalDate(db.Time.GetUtcNow().UtcDateTime, zone);

        await using (var ctx = db.NewContext())
        {
            for (int i = 1; i <= 400; i++)
            {
                var start = PulseMind.Core.Time.TimeZones.StartOfLocalDayUtc(today.AddDays(-i), zone).AddHours(10);
                ctx.StudySessions.Add(new StudySession { UserId = user, StartedAtUtc = start, EndedAtUtc = start.AddMinutes(30), Subject = "数学", CreatedAtUtc = start });
            }
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        int streak = await new StudySessionService(db.Factory, db.Time).StreakAsync(user, today, zone, TestContext.Current.CancellationToken);
        Assert.Equal(400, streak);
    }
}
