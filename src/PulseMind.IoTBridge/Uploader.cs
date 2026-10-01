using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PulseMind.IoTBridge;

/// <summary>
/// 溜まった心拍数を一定間隔でまとめてサーバーへ送る。
/// 送れなかったときは捨てずに残し、待ち時間を少しずつ延ばしながら送り直す。
/// </summary>
public sealed partial class Uploader(HttpClient http, IOptions<BridgeOptions> options, ReadingBuffer queue, IHostApplicationLifetime lifetime, ILogger<Uploader> logger)
    : BackgroundService
{
    /// <summary>1回に送る件数（サーバーの上限 2000 件より少なくする）</summary>
    internal const int BatchSize = 1000;

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!TryConfigure(o)) return;

        if (!await PingAsync(stoppingToken))
        {
            lifetime.StopApplication();
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(o.UploadIntervalSeconds, 2, 300));
        var backoff = interval;
        List<Reading> pending = [];

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(backoff, stoppingToken);

            if (pending.Count < BatchSize) pending.AddRange(queue.Drain(BatchSize - pending.Count));
            if (pending.Count == 0) continue;

            var outcome = await SendAsync(pending, stoppingToken);
            switch (outcome)
            {
                case SendOutcome.Sent:
                    pending.Clear();
                    backoff = interval;
                    break;
                case SendOutcome.Unauthorized:
                    LogUnauthorized();
                    lifetime.StopApplication();
                    return;
                case SendOutcome.Rejected:
                    pending.Clear(); // 内容が受け付けられない場合は、送り直しても同じなので捨てる
                    backoff = interval;
                    break;
                default:
                    backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                    LogRetry(pending.Count + queue.Count, (int)backoff.TotalSeconds);
                    break;
            }
        }
    }

    private bool TryConfigure(BridgeOptions o)
    {
        string? token = Environment.GetEnvironmentVariable("PULSEMIND_DEVICE_TOKEN") ?? o.DeviceToken;
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("pmd_", StringComparison.Ordinal))
        {
            LogMissingToken();
            lifetime.StopApplication();
            return false;
        }
        if (!Uri.TryCreate(o.ServerUrl, UriKind.Absolute, out var server) || (server.Scheme != Uri.UriSchemeHttps && !server.IsLoopback))
        {
            // トークンを暗号化されない通信で送らないよう、手元の PC 以外は https だけを認める
            LogBadServerUrl(o.ServerUrl);
            lifetime.StopApplication();
            return false;
        }

        http.BaseAddress = server;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return true;
    }

    private async Task<bool> PingAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(new Uri("api/v1/ping", UriKind.Relative), ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                LogUnauthorized();
                return false;
            }
            response.EnsureSuccessStatusCode();
            var ping = await response.Content.ReadFromJsonAsync<PingResponse>(ct);
            LogReady(ping?.Device ?? "?", http.BaseAddress!.Host);
            return true;
        }
        catch (HttpRequestException ex)
        {
            // 起動時にサーバーへ届かなくても、あとで届くかもしれないので続ける
            LogServerUnreachable(ex.Message);
            return true;
        }
    }

    internal enum SendOutcome { Sent, Unauthorized, Rejected, Failed }

    private async Task<SendOutcome> SendAsync(List<Reading> batch, CancellationToken ct)
    {
        var body = new { samples = batch.Select(r => new { t = r.Time, bpm = r.Bpm }) };
        try
        {
            using var response = await http.PostAsJsonAsync(new Uri("api/v1/heart-rate", UriKind.Relative), body, ct);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<UploadResponse>(ct);
                LogSent(result?.Accepted ?? batch.Count, result?.Rejected ?? 0);
                return SendOutcome.Sent;
            }
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => SendOutcome.Unauthorized,
                HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge => SendOutcome.Rejected,
                _ => SendOutcome.Failed, // 429（送りすぎ）や 5xx は、時間をおいて送り直す
            };
        }
        catch (HttpRequestException)
        {
            return SendOutcome.Failed;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return SendOutcome.Failed; // タイムアウト
        }
    }

    private sealed record PingResponse(string Device);

    private sealed record UploadResponse(int Accepted, int Rejected, int Minutes);

    [LoggerMessage(Level = LogLevel.Error, Message = "機器トークンがありません。Pulse & Mind Study の「機器とデータの取り込み」で機器を登録し、表示されたトークンを環境変数 PULSEMIND_DEVICE_TOKEN に設定してください")]
    private partial void LogMissingToken();

    [LoggerMessage(Level = LogLevel.Error, Message = "ServerUrl が正しくありません: \"{Url}\"（https:// で始まるアドレスを指定してください）")]
    private partial void LogBadServerUrl(string url);

    [LoggerMessage(Level = LogLevel.Error, Message = "機器トークンが正しくないか、無効にされています。新しく機器を登録し直してください")]
    private partial void LogUnauthorized();

    [LoggerMessage(Level = LogLevel.Information, Message = "準備ができました（機器: {Device} → {Host}）")]
    private partial void LogReady(string device, string host);

    [LoggerMessage(Level = LogLevel.Warning, Message = "サーバーに接続できません（{Reason}）。届くようになったら送ります")]
    private partial void LogServerUnreachable(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Accepted} 件送りました（範囲外で捨てられた値: {Rejected} 件）")]
    private partial void LogSent(int accepted, int rejected);

    [LoggerMessage(Level = LogLevel.Warning, Message = "送れませんでした。{Count} 件を保持し、{Seconds} 秒後に送り直します")]
    private partial void LogRetry(int count, int seconds);
}
