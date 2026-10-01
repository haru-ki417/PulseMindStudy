using System.IO.Compression;
using System.Net;
using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Devices;
using PulseMind.Core.Domain;
using PulseMind.Core.Health;
using PulseMind.Core.Privacy;
using PulseMind.Core.Records;
using PulseMind.Core.Study;
using PulseMind.Core.Time;
using PulseMind.Web.Services;

namespace PulseMind.Tests;

public class PrivacyTests
{
    private static readonly TimeZoneInfo Tokyo = TimeZones.Resolve("Asia/Tokyo");

    private static async Task<string> SeedAsync(TestDatabase db, string email)
    {
        string user = await db.AddUserAsync(email);
        var now = db.Time.GetUtcNow().UtcDateTime;
        await new StudySessionService(db.Factory, db.Time).AddManualAsync(user, new ManualStudyInput(now.AddHours(-2), now.AddHours(-1), "=1+1", 4, "メモ, \"引用\""));
        await new DailyRecordService(db.Factory, db.Time).SaveAsync(user, new DailyRecordInput(TimeZones.LocalDate(now, Tokyo), 7, 5000, 4, null), Tokyo);
        await new HeartRateIngestionService(db.Factory, db.Time).IngestAsync(user, DataSource.Arduino, [new(now.AddMinutes(-3), 70)], HeartRateMergeMode.Accumulate);
        await new DeviceService(db.Factory, db.Time).RegisterAsync(user, "机の Arduino");
        await new AuditLog(db.Factory, db.Time).WriteAsync(user, AuditKind.SignedIn, ip: IPAddress.Parse("203.0.113.45"));
        return user;
    }

    [Fact]
    public async Task アカウントを削除するとすべての記録が消え他の人の記録は残る()
    {
        await using var db = await TestDatabase.CreateAsync();
        string alice = await SeedAsync(db, "alice@example.com");
        string bob = await SeedAsync(db, "bob@example.com");

        await using (var ctx = db.NewContext())
        {
            ctx.Users.Remove(await ctx.Users.SingleAsync(u => u.Id == alice, TestContext.Current.CancellationToken));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var check = db.NewContext();
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(0, await check.StudySessions.CountAsync(x => x.UserId == alice, ct) + await check.DailyRecords.CountAsync(x => x.UserId == alice, ct)
            + await check.HeartRateMinutes.CountAsync(x => x.UserId == alice, ct) + await check.Devices.CountAsync(x => x.UserId == alice, ct)
            + await check.AuditEvents.CountAsync(x => x.UserId == alice, ct));
        Assert.Equal(1, await check.StudySessions.CountAsync(x => x.UserId == bob, ct));
        Assert.Equal(1, await check.Devices.CountAsync(x => x.UserId == bob, ct));
    }

    [Fact]
    public async Task 書き出しには全データが入りトークンの照合値は入らない()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await SeedAsync(db, "alice@example.com");
        await SeedAsync(db, "bob@example.com");

        using var zipStream = new MemoryStream();
        await new DataExportService(db.Factory, db.Time).WriteZipAsync(user, zipStream, TestContext.Current.CancellationToken);
        zipStream.Position = 0;
        using var zip = new ZipArchive(zipStream);

        Assert.Equal(
            ["README.txt", "daily_records.csv", "devices.csv", "heart_rate_minutes.csv", "profile.json", "study_sessions.csv"],
            zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));

        string Read(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open()); return r.ReadToEnd(); }
        string sessions = Read("study_sessions.csv");
        Assert.Contains("'=1+1", sessions, StringComparison.Ordinal);             // 表計算ソフトで数式として動かない
        Assert.Contains("\"メモ, \"\"引用\"\"\"", sessions, StringComparison.Ordinal); // カンマと引用符を正しく囲む
        Assert.Equal(2, Read("heart_rate_minutes.csv").Trim().Split('\n').Length);
        Assert.Contains("alice@example.com", Read("profile.json"), StringComparison.Ordinal);

        string devices = Read("devices.csv");
        await using var ctx = db.NewContext();
        string hash = (await ctx.Devices.SingleAsync(d => d.UserId == user, TestContext.Current.CancellationToken)).TokenHash;
        Assert.DoesNotContain(hash, devices, StringComparison.Ordinal);
        Assert.DoesNotContain("bob@example.com", zip.Entries.Select(e => Read(e.FullName)).Aggregate(string.Concat), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("203.0.113.45", "203.0.113.x")]
    [InlineData("2001:db8:85a3:8d3:1319:8a2e:370:7348", "2001:db8:85a3:8d3:x:x:x:x")]
    [InlineData("::ffff:192.0.2.10", "192.0.2.x")]
    public void IPアドレスは末尾を伏せる(string ip, string masked) => Assert.Equal(masked, AuditLog.Mask(IPAddress.Parse(ip)));

    [Fact]
    public async Task 古いセキュリティの記録は自動で消える()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var audit = new AuditLog(db.Factory, db.Time);
        await audit.WriteAsync(user, AuditKind.SignedIn, cancellationToken: TestContext.Current.CancellationToken);
        db.Time.Advance(TimeSpan.FromDays(AuditLog.RetentionDays + 1));
        await audit.WriteAsync(user, AuditKind.SignedIn, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(await audit.RecentAsync(user, cancellationToken: TestContext.Current.CancellationToken));
    }
}

public sealed class SecurityTests(PulseMindAppFactory factory) : IClassFixture<PulseMindAppFactory>
{
    [Fact]
    public async Task 安全のための応答ヘッダーが付く()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task ログインの送信を短時間に繰り返すと止められる()
    {
        using var client = factory.CreateClient();
        HttpStatusCode last = default;
        for (int i = 0; i < 11; i++)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Email"] = "a@example.com", ["Input.Password"] = "x" });
            var response = await client.PostAsync(new Uri("/Account/Login", UriKind.Relative), content, TestContext.Current.CancellationToken);
            last = response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task 利用規約とプライバシーポリシーはログインしなくても読める()
    {
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/terms", "/privacy" })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
