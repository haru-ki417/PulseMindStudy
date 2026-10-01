using System.Threading.Channels;

namespace PulseMind.IoTBridge;

/// <summary>センサーの1回分の値（受け取った時刻はこの PC の時計で付ける）</summary>
public readonly record struct Reading(DateTimeOffset Time, double Bpm);

/// <summary>
/// センサーを読む側と送る側をつなぐ入れ物。
/// 通信が長く途切れても PC のメモリを使い切らないよう、上限を超えたら古いものから捨てる。
/// </summary>
public sealed class ReadingBuffer
{
    public const int Capacity = 20_000;

    private readonly Channel<Reading> channel = Channel.CreateBounded<Reading>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    public bool TryWrite(Reading reading) => channel.Writer.TryWrite(reading);

    /// <summary>いま溜まっている分を最大 max 件取り出す</summary>
    public List<Reading> Drain(int max)
    {
        var batch = new List<Reading>();
        while (batch.Count < max && channel.Reader.TryRead(out var r)) batch.Add(r);
        return batch;
    }

    public int Count => channel.Reader.Count;
}
