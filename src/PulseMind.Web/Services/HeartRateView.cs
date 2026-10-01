using PulseMind.Core.Domain;
using PulseMind.Core.Time;
using PulseMind.Web.Components.Ui;

namespace PulseMind.Web.Services;

/// <summary>保存されている1分ごとの心拍を、グラフ用の点に変える</summary>
public static class HeartRateView
{
    /// <summary>入手元が複数ある同じ1分は、件数で重み付けして1点にまとめる</summary>
    public static IReadOnlyList<HeartRatePoint> ToPoints(IEnumerable<HeartRateMinute> minutes, TimeZoneInfo zone) =>
        minutes
            .GroupBy(m => m.MinuteUtc)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                int n = g.Sum(m => m.SampleCount);
                double avg = n == 0 ? g.Average(m => m.AverageBpm) : g.Sum(m => m.AverageBpm * m.SampleCount) / n;
                return new HeartRatePoint(TimeZones.ToLocal(g.Key, zone), avg, g.Min(m => m.MinBpm), g.Max(m => m.MaxBpm));
            })
            .ToList();
}
