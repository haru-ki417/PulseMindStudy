using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PulseMind.Core.Domain;

namespace PulseMind.Core.Data;

public class PulseMindDbContext(DbContextOptions<PulseMindDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<StudySession> StudySessions => Set<StudySession>();

    public DbSet<DailyRecord> DailyRecords => Set<DailyRecord>();

    public DbSet<HeartRateMinute> HeartRateMinutes => Set<HeartRateMinute>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        base.ConfigureConventions(configurationBuilder);

        // 時刻はすべて UTC で保存する。データベースから読み戻すと「種類不明」の時刻になるため、UTC として扱うよう指定する
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        // 利用者を削除したら、その人のデータもすべて削除する（退会時に記録が残らないように）
        builder.Entity<StudySession>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.StartedAtUtc });
            e.Ignore(x => x.IsRunning);
            e.Ignore(x => x.Duration);
        });

        builder.Entity<DailyRecord>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
        });

        builder.Entity<HeartRateMinute>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            // 同じ人・同じ入手元・同じ1分の行は1つだけ
            e.HasIndex(x => new { x.UserId, x.Source, x.MinuteUtc }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.MinuteUtc });
        });

        builder.Entity<Device>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.Ignore(x => x.IsActive);
        });

        ConfigureAudit(builder);
    }

    private static void ConfigureAudit(ModelBuilder builder) =>
        builder.Entity<AuditEvent>(e =>
        {
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.AtUtc });
        });

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
