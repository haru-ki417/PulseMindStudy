using System.IO.Ports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PulseMind.IoTBridge;

/// <summary>Arduino（シリアルポート）から心拍数を読み続ける。抜き差しされても数秒おきに接続し直す。</summary>
public sealed partial class SensorReader(IOptions<BridgeOptions> options, ReadingBuffer queue, TimeProvider time, ILogger<SensorReader> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (o.Simulate)
        {
            await SimulateAsync(stoppingToken);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var port = new SerialPort(o.SerialPort, o.BaudRate) { ReadTimeout = 2000, NewLine = "\n" };
                port.Open();
                LogConnected(o.SerialPort, o.BaudRate);

                while (!stoppingToken.IsCancellationRequested)
                {
                    string line;
                    try
                    {
                        line = port.ReadLine();
                    }
                    catch (TimeoutException)
                    {
                        continue; // しばらく値が来ないだけ（指が離れているなど）
                    }

                    if (SerialLineParser.TryParse(line, out double bpm))
                        queue.TryWrite(new Reading(time.GetUtcNow(), bpm));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                LogPortError(o.SerialPort, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    /// <summary>センサーが無くても動作を確かめられるよう、ゆるやかに上下する心拍を1秒ごとに作る</summary>
    private async Task SimulateAsync(CancellationToken stoppingToken)
    {
        LogSimulating();
        var random = new Random();
        double phase = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            phase += 0.02;
            double bpm = 70 + 8 * Math.Sin(phase) + random.NextDouble() * 4 - 2;
            queue.TryWrite(new Reading(time.GetUtcNow(), Math.Round(bpm)));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "センサーに接続しました（{Port}, {Baud} bps）")]
    private partial void LogConnected(string port, int baud);

    [LoggerMessage(Level = LogLevel.Warning, Message = "シリアルポート {Port} を開けません（{Reason}）。5秒後にもう一度試します")]
    private partial void LogPortError(string port, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "見本の心拍を作って送ります（Simulate モード）")]
    private partial void LogSimulating();
}
