using PulseMind.Core.Study;
using PulseMind.Core.Time;

namespace PulseMind.Tests;

public class TimeAndStudyTimeTests
{
    private static readonly TimeZoneInfo Tokyo = TimeZones.Resolve("Asia/Tokyo");

    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void 日本時間の0時はUTCの前日15時()
    {
        Assert.Equal(Utc(2026, 3, 31, 15), TimeZones.StartOfLocalDayUtc(new DateOnly(2026, 4, 1), Tokyo));
    }

    [Fact]
    public void 日付をまたいだ学習はそれぞれの日に分けて数える()
    {
        // 日本時間 4/1 23:30 〜 4/2 0:45
        var minutes = StudyTime.MinutesPerLocalDay([(Utc(2026, 4, 1, 14, 30), Utc(2026, 4, 1, 15, 45))], Tokyo);

        Assert.Equal(30, minutes[new DateOnly(2026, 4, 1)], 6);
        Assert.Equal(45, minutes[new DateOnly(2026, 4, 2)], 6);
    }

    [Fact]
    public void 夏時間に切り替わる日は23時間として数える()
    {
        // アメリカ東部は 2026-03-08 に夏時間が始まり、その日は 23 時間しかない
        var newYork = TimeZones.Resolve("America/New_York");
        var (start, end) = TimeZones.LocalDayRangeUtc(new DateOnly(2026, 3, 8), newYork);
        Assert.Equal(TimeSpan.FromHours(23), end - start);

        var minutes = StudyTime.MinutesPerLocalDay([(start.AddHours(-1), end.AddHours(1))], newYork);
        Assert.Equal(23 * 60, minutes[new DateOnly(2026, 3, 8)], 6);
    }

    [Fact]
    public void 知らないタイムゾーンは日本時間として扱う()
    {
        Assert.Equal(Tokyo.BaseUtcOffset, TimeZones.Resolve("Mars/Olympus").BaseUtcOffset);
        Assert.False(TimeZones.IsKnown("Mars/Olympus"));
        Assert.True(TimeZones.IsKnown("Europe/London"));
    }

    [Fact]
    public void 期間の外にはみ出した部分は切り捨てる()
    {
        var clipped = StudyTime.Clip(Utc(2026, 4, 1, 10), Utc(2026, 4, 1, 14), Utc(2026, 4, 1, 12), Utc(2026, 4, 2, 0));
        Assert.Equal((Utc(2026, 4, 1, 12), Utc(2026, 4, 1, 14)), clipped);
        Assert.Null(StudyTime.Clip(Utc(2026, 4, 1, 10), Utc(2026, 4, 1, 11), Utc(2026, 4, 1, 12), Utc(2026, 4, 2, 0)));
    }
}
