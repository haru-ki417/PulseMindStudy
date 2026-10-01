using PulseMind.Core;
using PulseMind.Core.Profiles;
using PulseMind.Core.Study;
using PulseMind.Web.Services;

namespace PulseMind.Tests;

public class ExamAndSubjectTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    [Fact]
    public async Task 試験は名前と日付をそろえて保存し両方空なら外せる()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var profiles = new UserProfileService(db.Factory);

        await profiles.UpdateExamAsync(user, new ExamInput(" 大学入学共通テスト ", new DateOnly(2027, 1, 16)), Today, TestContext.Current.CancellationToken);
        var p = await profiles.GetAsync(user, TestContext.Current.CancellationToken);
        Assert.Equal(("大学入学共通テスト", new DateOnly(2027, 1, 16)), (p!.ExamName, p.ExamDate!.Value));

        await profiles.UpdateExamAsync(user, new ExamInput(null, null), Today, TestContext.Current.CancellationToken);
        p = await profiles.GetAsync(user, TestContext.Current.CancellationToken);
        Assert.Null(p!.ExamName);
        Assert.Null(p.ExamDate);
    }

    [Theory]
    [InlineData("模試", null)]          // 日付が無い
    [InlineData(null, "2027-01-16")]    // 名前が無い
    [InlineData("模試", "2026-09-30")]  // 過去の日
    [InlineData("模試", "2032-01-01")]  // 5年より先
    public async Task おかしな試験の設定は受け付けない(string? name, string? date)
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var profiles = new UserProfileService(db.Factory);
        DateOnly? d = date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

        await Assert.ThrowsAsync<UserInputException>(() => profiles.UpdateExamAsync(user, new ExamInput(name, d), Today, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void 残り日数と今のペースでの見込みを出す()
    {
        var c = Countdown.Create("共通テスト", new DateOnly(2027, 1, 16), Today, recentDailyAverageMinutes: 120);
        Assert.NotNull(c);
        Assert.Equal(107, c.DaysLeft);
        Assert.Equal(214, c.ProjectedHours!.Value, 6);

        Assert.Null(Countdown.Create("共通テスト", new DateOnly(2026, 9, 1), Today, 120)); // 終わった試験
        Assert.Null(Countdown.Create(null, new DateOnly(2027, 1, 16), Today, 120));               // 名前が無い
        Assert.Null(Countdown.Create("共通テスト", new DateOnly(2027, 1, 16), Today, 0)!.ProjectedHours); // まだ記録が無い
    }

    [Fact]
    public async Task 科目ごとの時間は期間をはみ出した部分を数えない()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var sessions = new StudySessionService(db.Factory, db.Time);
        var now = db.Time.GetUtcNow().UtcDateTime;

        await sessions.AddManualAsync(user, new ManualStudyInput(now.AddHours(-5), now.AddHours(-3), "数学"), TestContext.Current.CancellationToken);
        await sessions.AddManualAsync(user, new ManualStudyInput(now.AddHours(-2), now.AddHours(-1.5), "英語"), TestContext.Current.CancellationToken);
        await sessions.AddManualAsync(user, new ManualStudyInput(now.AddHours(-1), now.AddHours(-0.5), "数学"), TestContext.Current.CancellationToken);

        var result = await sessions.MinutesBySubjectAsync(user, now.AddHours(-4), now, TestContext.Current.CancellationToken);

        Assert.Equal([("数学", 90.0), ("英語", 30.0)], result.Select(r => (r.Subject, Math.Round(r.Minutes))));
    }

    [Fact]
    public void 科目の色は同じ科目ならいつも同じ()
    {
        Assert.Equal(SubjectColors.For("数学"), SubjectColors.For("数学"));
        Assert.Equal(SubjectColors.For("小論文"), SubjectColors.For("小論文"));
        Assert.Equal("#8e929e", SubjectColors.For(""));
        Assert.Equal("科目なし", SubjectColors.Label(null));
    }
}
