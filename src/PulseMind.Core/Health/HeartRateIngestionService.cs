using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;
using PulseMind.Core.Domain;

namespace PulseMind.Core.Health;

/// <summary>同じ1分の記録がすでにあったときの扱い</summary>
public enum HeartRateMergeMode
{
    /// <summary>足し合わせる。センサーから少しずつ届く場合に使う。</summary>
    Accumulate,

    /// <summary>置き換える。ヘルスケアの書き出しのように、同じデータを何度取り込んでも結果が変わらないようにしたい場合に使う。</summary>
    Replace,
}

/// <summary>取り込みの結果。受け付けた数・捨てた数・書き込んだ分数。</summary>
public sealed record HeartRateIngestResult(int Accepted, int Rejected, int MinutesWritten);

/// <summary>心拍数の測定値を受け取り、1分ごとにまとめて保存する</summary>
public sealed class HeartRateIngestionService(IDbContextFactory<PulseMindDbContext> dbFactory, TimeProvider time)
{
    /// <summary>この日より前の測定値は、時計の設定ミスなどとして捨てる</summary>
    public static readonly DateTime OldestAcceptedUtc = new(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>一度に保存する分数。大きな取り込みでもメモリを使いすぎないよう、区切って保存する。</summary>
    private const int ChunkSize = 1000;

    public async Task<HeartRateIngestResult> IngestAsync(
        string userId, DataSource source, IEnumerable<HeartRateSample> samples, HeartRateMergeMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var latest = time.GetUtcNow().UtcDateTime + ClockSkew;

        var accepted = new List<HeartRateSample>();
        int rejected = 0;
        foreach (var sample in samples)
        {
            var ts = sample.TimestampUtc.Kind == DateTimeKind.Local ? sample.TimestampUtc.ToUniversalTime() : sample.TimestampUtc;
            if (ts.Kind == DateTimeKind.Unspecified || ts < OldestAcceptedUtc || ts > latest || !HeartRateAggregation.IsPlausible(sample.Bpm))
            {
                rejected++;
                continue;
            }
            accepted.Add(sample with { TimestampUtc = ts });
        }

        var buckets = HeartRateAggregation.Aggregate(accepted);
        foreach (var chunk in buckets.Chunk(ChunkSize))
        {
            await SaveChunkWithRetryAsync(userId, source, chunk, mode, cancellationToken);
        }

        return new HeartRateIngestResult(accepted.Count, rejected, buckets.Count);
    }

    /// <summary>[fromUtc, toUtc) の1分ごとの心拍数を時刻順に返す</summary>
    public async Task<IReadOnlyList<HeartRateMinute>> GetMinutesAsync(
        string userId, DateTime fromUtc, DateTime toUtc, DataSource? source = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.HeartRateMinutes.AsNoTracking()
            .Where(m => m.UserId == userId && m.MinuteUtc >= fromUtc && m.MinuteUtc < toUtc);
        if (source is DataSource s) query = query.Where(m => m.Source == s);
        return await query.OrderBy(m => m.MinuteUtc).ToListAsync(cancellationToken);
    }

    private async Task SaveChunkWithRetryAsync(
        string userId, DataSource source, HeartRateBucket[] chunk, HeartRateMergeMode mode, CancellationToken cancellationToken)
    {
        try
        {
            await SaveChunkAsync(userId, source, chunk, mode, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 同じ1分の記録が別の送信と同時に作られた（重複の制約に当たった）。
            // 新しい接続で最新の状態を読み直し、もう一度だけ試す。
            await SaveChunkAsync(userId, source, chunk, mode, cancellationToken);
        }
    }

    private async Task SaveChunkAsync(
        string userId, DataSource source, HeartRateBucket[] chunk, HeartRateMergeMode mode, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        DateTime first = chunk[0].MinuteUtc, last = chunk[^1].MinuteUtc;
        var existing = await db.HeartRateMinutes
            .Where(m => m.UserId == userId && m.Source == source && m.MinuteUtc >= first && m.MinuteUtc <= last)
            .ToDictionaryAsync(m => m.MinuteUtc, cancellationToken);

        foreach (var bucket in chunk)
        {
            if (!existing.TryGetValue(bucket.MinuteUtc, out var row))
            {
                db.HeartRateMinutes.Add(new HeartRateMinute
                {
                    UserId = userId,
                    Source = source,
                    MinuteUtc = bucket.MinuteUtc,
                    AverageBpm = bucket.AverageBpm,
                    MinBpm = bucket.MinBpm,
                    MaxBpm = bucket.MaxBpm,
                    SampleCount = bucket.SampleCount,
                });
                continue;
            }

            var merged = mode == HeartRateMergeMode.Replace
                ? bucket
                : HeartRateAggregation.Combine(
                    new HeartRateBucket(row.MinuteUtc, row.AverageBpm, row.MinBpm, row.MaxBpm, row.SampleCount), bucket);

            row.AverageBpm = merged.AverageBpm;
            row.MinBpm = merged.MinBpm;
            row.MaxBpm = merged.MaxBpm;
            row.SampleCount = merged.SampleCount;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
