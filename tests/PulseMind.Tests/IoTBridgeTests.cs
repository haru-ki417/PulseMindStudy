using PulseMind.IoTBridge;

namespace PulseMind.Tests;

public class IoTBridgeTests
{
    [Theory]
    [InlineData("BPM:72", 72)]
    [InlineData("BPM: 68.5\r", 68.5)]
    [InlineData("♥  A HeartBeat Happened ! BPM: 91", 91)]
    [InlineData("75", 75)]
    [InlineData("bpm=120", 120)]
    public void センサーの行から心拍数を取り出す(string line, double expected)
    {
        Assert.True(SerialLineParser.TryParse(line, out double bpm));
        Assert.Equal(expected, bpm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Signal:512")]
    [InlineData("BPM:0")]
    [InlineData("999")]
    [InlineData("hello")]
    public void 心拍数でない行は無視する(string line) => Assert.False(SerialLineParser.TryParse(line, out _));

    [Fact]
    public void 溜まりすぎたら古いものから捨てる()
    {
        var queue = new ReadingBuffer();
        var start = DateTimeOffset.UnixEpoch;
        for (int i = 0; i < ReadingBuffer.Capacity + 5; i++) queue.TryWrite(new Reading(start.AddSeconds(i), 70));

        var first = queue.Drain(1);
        Assert.Equal(start.AddSeconds(5), first[0].Time);
        Assert.Equal(ReadingBuffer.Capacity - 1, queue.Count);
    }
}
