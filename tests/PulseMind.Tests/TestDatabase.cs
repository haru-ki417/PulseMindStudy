using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PulseMind.Core.Data;

namespace PulseMind.Tests;

/// <summary>
/// テスト用のデータベース（メモリ上の SQLite）。テストごとに空の状態から始める。
/// 本番の SQL Server とは細かな違いがあるが、検索条件や一意制約の確認には十分。
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection;

    /// <summary>テスト内の「現在時刻」。2026-04-01 12:00 UTC（日本時間 21:00）から始まる。</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));

    private TestDatabase(SqliteConnection connection)
    {
        this.connection = connection;
        Factory = new ContextFactory(this);
    }

    /// <summary>サービスに渡す DbContext の作り手</summary>
    public IDbContextFactory<PulseMindDbContext> Factory { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var db = new TestDatabase(connection);
        await using var context = db.NewContext();
        await context.Database.EnsureCreatedAsync();
        return db;
    }

    /// <summary>新しい DbContext を作る（同じデータベースを指す）。サービスごとに分けると、実際の使われ方に近くなる。</summary>
    public PulseMindDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PulseMindDbContext>().UseSqlite(connection).Options);

    public async Task<string> AddUserAsync(string email = "student@example.com")
    {
        await using var context = NewContext();
        var user = new ApplicationUser { UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant() };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();

    private sealed class ContextFactory(TestDatabase owner) : IDbContextFactory<PulseMindDbContext>
    {
        public PulseMindDbContext CreateDbContext() => owner.NewContext();
    }
}
