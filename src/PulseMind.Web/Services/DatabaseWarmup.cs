using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;

namespace PulseMind.Web.Services;

/// <summary>
/// 休止しているデータベースを、裏で起こしておく。
/// 無料枠の Azure SQL Database（サーバーレス）は、1 時間使われないと休止し、次の接続で起動するまで数十秒かかる。
/// 画面を開いた時点で起こし始めれば、ログインやデータの読み込みのときに待つ時間が短くなる。
/// </summary>
public sealed partial class DatabaseWaker(IDbContextFactory<PulseMindDbContext> factory, ILogger<DatabaseWaker> logger)
{
    // 最後に接続できてからこの時間は、起きているとみなして何もしない（休止は 1 時間後なので十分に短い）
    private static readonly long FreshMs = (long)TimeSpan.FromMinutes(5).TotalMilliseconds;
    private long _lastAwakeMs = long.MinValue / 2;
    private int _running;

    /// <summary>待たずに戻る。起こす処理は裏で 1 本だけ走らせる。</summary>
    public void Poke()
    {
        if (Environment.TickCount64 - Interlocked.Read(ref _lastAwakeMs) < FreshMs) return;
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var db = await factory.CreateDbContextAsync();
                if (await db.Database.CanConnectAsync())
                {
                    Interlocked.Exchange(ref _lastAwakeMs, Environment.TickCount64);
                }
            }
            catch (Exception ex)
            {
                LogWakeFailed(logger, ex);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "データベースを起こす途中で接続できませんでした（起動中の可能性があります）")]
    private static partial void LogWakeFailed(ILogger logger, Exception exception);
}

/// <summary>
/// 本番では、起動時のマイグレーションを裏で行う。
/// 以前は起動の途中で待っていたため、データベースが休止していると、アプリが応答を返し始めるまでに数十秒かかっていた。
/// 裏で行うことで、画面はすぐに返し、同時にデータベースも起こし始める。
/// </summary>
public sealed partial class BackgroundMigration(IServiceProvider services, ILogger<BackgroundMigration> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (int attempt = 1; attempt <= 5 && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PulseMindDbContext>();
                await db.Database.MigrateAsync(stoppingToken);
                LogMigrated(logger);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogMigrationRetry(logger, ex, attempt);
                await Task.Delay(TimeSpan.FromSeconds(15 * attempt), stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "データベースのマイグレーションが終わりました")]
    private static partial void LogMigrated(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "マイグレーションに失敗しました（{Attempt} 回目）。少し待ってやり直します")]
    private static partial void LogMigrationRetry(ILogger logger, Exception exception, int attempt);
}
