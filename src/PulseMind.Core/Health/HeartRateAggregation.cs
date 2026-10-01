namespace PulseMind.Core.Health;

/// <summary>心拍数の1つの測定値</summary>
public readonly record struct HeartRateSample(DateTime TimestampUtc, double Bpm);

/// <summary>1分ぶんの心拍数のまとめ</summary>
public sealed record HeartRateBucket(DateTime MinuteUtc, double AverageBpm, double MinBpm, double MaxBpm, int SampleCount);

/// <summary>心拍数を1分ごとにまとめる計算（データベースを使わない部分）</summary>
public static class HeartRateAggregation
{
    /// <summary>
    /// 人の心拍数として受け付ける範囲。
    /// 指先のセンサーは、指がずれたときなどに 0 や 300 のような値を出すことがあるため、範囲外は捨てる。
    /// （このアプリは医療機器ではないので、異常値を「病気の兆候」として扱うことはしない）
    /// </summary>
    public const double MinPlausibleBpm = 30;
    public const double MaxPlausibleBpm = 220;

    public static bool IsPlausible(double bpm) => double.IsFinite(bpm) && bpm >= MinPlausibleBpm && bpm <= MaxPlausibleBpm;

    public static DateTime TruncateToMinute(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    /// <summary>測定値を1分ごとにまとめる。範囲外の値は含めない。結果は時刻順。</summary>
    public static IReadOnlyList<HeartRateBucket> Aggregate(IEnumerable<HeartRateSample> samples) =>
        samples
            .Where(s => IsPlausible(s.Bpm))
            .GroupBy(s => TruncateToMinute(s.TimestampUtc))
            .OrderBy(g => g.Key)
            .Select(g => new HeartRateBucket(g.Key, g.Average(s => s.Bpm), g.Min(s => s.Bpm), g.Max(s => s.Bpm), g.Count()))
            .ToList();

    /// <summary>同じ1分の2つのまとめを1つにする（平均は件数で重み付けする）</summary>
    public static HeartRateBucket Combine(HeartRateBucket a, HeartRateBucket b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.MinuteUtc != b.MinuteUtc) throw new ArgumentException("異なる時刻のまとめは結合できません。", nameof(b));

        int count = a.SampleCount + b.SampleCount;
        double average = (a.AverageBpm * a.SampleCount + b.AverageBpm * b.SampleCount) / count;
        return new HeartRateBucket(a.MinuteUtc, average, Math.Min(a.MinBpm, b.MinBpm), Math.Max(a.MaxBpm, b.MaxBpm), count);
    }
}
