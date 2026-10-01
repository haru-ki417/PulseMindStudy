using System.ComponentModel.DataAnnotations;

namespace PulseMind.Core.Domain;

/// <summary>
/// 1分ごとにまとめた心拍数。
/// センサーは1秒に何回も値を送ってくるが、生の値をすべて保存すると量が膨大になり、
/// 集計にも時間がかかる。グラフや分析には1分単位で十分なので、平均・最小・最大・件数だけを残す。
/// </summary>
public sealed class HeartRateMinute
{
    public long Id { get; set; }

    [MaxLength(450)]
    public required string UserId { get; set; }

    public DataSource Source { get; set; }

    /// <summary>その1分の開始時刻（UTC、秒以下は 0）</summary>
    public DateTime MinuteUtc { get; set; }

    public double AverageBpm { get; set; }

    public double MinBpm { get; set; }

    public double MaxBpm { get; set; }

    /// <summary>平均の元になった値の数。あとから値が追加されたとき、平均を正しく更新するのに使う。</summary>
    public int SampleCount { get; set; }
}
