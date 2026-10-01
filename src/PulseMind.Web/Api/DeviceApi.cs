using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PulseMind.Core.Devices;
using PulseMind.Core.Domain;
using PulseMind.Core.Health;

namespace PulseMind.Web.Api;

/// <summary>機器から送られてくる心拍数の1件。t は時差付きの日時（例: 2026-10-01T21:00:05+09:00）。</summary>
public sealed record HeartRateSampleDto([property: JsonPropertyName("t")] DateTimeOffset Time, [property: JsonPropertyName("bpm")] double Bpm);

public sealed record HeartRateUploadRequest([property: JsonPropertyName("samples")] IReadOnlyList<HeartRateSampleDto>? Samples);

public sealed record HeartRateUploadResponse(int Accepted, int Rejected, int Minutes);

public sealed record PingResponse(string Device, DateTime ServerTimeUtc);

/// <summary>
/// 機器（IoTBridge）向けの API。ログイン画面の代わりに、機器トークンで認証する。
///   Authorization: Bearer pmd_xxxxx
/// </summary>
public static class DeviceApi
{
    public const string RateLimitPolicy = "device";

    /// <summary>1回に送れる件数。これを超える場合は分けて送ってもらう。</summary>
    public const int MaxSamplesPerRequest = 2000;

    /// <summary>受け付ける本文の大きさ（2000 件で約 100KB なので余裕を持たせる）</summary>
    private const long MaxBodyBytes = 512 * 1024;

    public static IEndpointRouteBuilder MapDeviceApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireRateLimiting(RateLimitPolicy).DisableAntiforgery();

        // トークンが正しいかの確認（IoTBridge の起動時に使う）
        group.MapGet("/ping", async Task<Results<Ok<PingResponse>, UnauthorizedHttpResult>> (HttpRequest request, DeviceService devices, TimeProvider time, CancellationToken ct) =>
        {
            var device = await devices.AuthenticateAsync(ReadBearer(request), ct);
            return device is null ? TypedResults.Unauthorized() : TypedResults.Ok(new PingResponse(device.Name, time.GetUtcNow().UtcDateTime));
        });

        group.MapPost("/heart-rate", async Task<Results<Ok<HeartRateUploadResponse>, UnauthorizedHttpResult, ValidationProblem>> (
            HttpRequest request, HeartRateUploadRequest body, DeviceService devices, HeartRateIngestionService heartRates, CancellationToken ct) =>
        {
            var device = await devices.AuthenticateAsync(ReadBearer(request), ct);
            if (device is null) return TypedResults.Unauthorized();

            var samples = body.Samples ?? [];
            if (samples.Count == 0 || samples.Count > MaxSamplesPerRequest)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["samples"] = [$"samples は 1〜{MaxSamplesPerRequest} 件にしてください。"],
                });
            }

            var result = await heartRates.IngestAsync(device.UserId, DataSource.Arduino,
                samples.Select(s => new HeartRateSample(s.Time.UtcDateTime, s.Bpm)), HeartRateMergeMode.Accumulate, ct);
            return TypedResults.Ok(new HeartRateUploadResponse(result.Accepted, result.Rejected, result.MinutesWritten));
        })
        .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));

        return app;
    }

    /// <summary>"Authorization: Bearer xxx" から xxx を取り出す</summary>
    internal static string? ReadBearer(HttpRequest request)
    {
        string? header = request.Headers.Authorization;
        const string scheme = "Bearer ";
        return header is not null && header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) ? header[scheme.Length..].Trim() : null;
    }
}
