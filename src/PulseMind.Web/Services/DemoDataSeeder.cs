using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;
using PulseMind.Core.Domain;
using PulseMind.Core.Health;
using PulseMind.Core.Time;

namespace PulseMind.Web.Services;

/// <summary>
/// 画面の確認やスクリーンショット用に、見本の利用者と30日分の記録を作る。
/// 設定 Demo:Email と Demo:Password があるときだけ動く（本番では設定しない）。
/// 値は乱数で作った架空のもので、実在の人のデータではない。
/// </summary>
public static class DemoDataSeeder
{
    private static readonly string[] Subjects = ["数学", "英語", "物理", "化学", "国語", "英単語"];

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        string? email = configuration["Demo:Email"], password = configuration["Demo:Password"];
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password)) return;

        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is not null) return;

        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "はるき", DailyGoalMinutes = 180 };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded) throw new InvalidOperationException(string.Join(" ", created.Errors.Select(e => e.Description)));

        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var zone = TimeZones.Resolve(user.TimeZoneId);
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var today = TimeZones.LocalDate(nowUtc, zone);
        var random = new Random(417);

        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PulseMindDbContext>>();
        await using (var context = await db.CreateDbContextAsync())
        {
            for (int d = 29; d >= 0; d--)
            {
                var date = today.AddDays(-d);
                double sleep = Math.Round(5.5 + random.NextDouble() * 2.5, 1);
                context.DailyRecords.Add(new DailyRecord
                {
                    UserId = user.Id, Date = date, SleepHours = sleep, Steps = 3000 + random.Next(9000),
                    Mood = Math.Clamp((int)Math.Round(1.5 + (sleep - 5.5) + random.NextDouble() * 1.8), 1, 5), UpdatedAtUtc = nowUtc,
                });

                // よく眠れた日ほど少し長く勉強する、という傾向を持たせる
                int sessions = d == 0 ? 1 : random.Next(1, 4);
                var cursor = TimeZones.StartOfLocalDayUtc(date, zone).AddHours(16 + random.NextDouble());
                for (int s = 0; s < sessions; s++)
                {
                    double minutes = 25 + random.Next(70) + (sleep - 6) * 12;
                    var end = cursor.AddMinutes(minutes);
                    if (end > nowUtc) break;
                    context.StudySessions.Add(new StudySession
                    {
                        UserId = user.Id, StartedAtUtc = cursor, EndedAtUtc = end, Subject = Subjects[random.Next(Subjects.Length)],
                        Focus = Math.Clamp((int)Math.Round(2 + (sleep - 5.5) * 0.9 + random.NextDouble() * 1.5), 1, 5), CreatedAtUtc = end,
                    });
                    cursor = end.AddMinutes(10 + random.Next(40));
                }
            }
            await context.SaveChangesAsync();
        }

        // 今日の心拍（夕方から今まで。勉強中はやや高め）
        var heartRates = scope.ServiceProvider.GetRequiredService<HeartRateIngestionService>();
        var start = nowUtc.AddHours(-5);
        var samples = new List<HeartRateSample>();
        for (var t = start; t < nowUtc; t = t.AddSeconds(20))
        {
            double minutesFromStart = (t - start).TotalMinutes;
            double baseline = 68 + 6 * Math.Sin(minutesFromStart / 35);
            samples.Add(new HeartRateSample(t, baseline + random.NextDouble() * 6 - 3));
        }
        await heartRates.IngestAsync(user.Id, DataSource.Arduino, samples, HeartRateMergeMode.Accumulate);
    }
}
