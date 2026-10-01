using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;
using PulseMind.Core.Time;

namespace PulseMind.Core.Privacy;

/// <summary>
/// 利用者の全データを zip（CSV と JSON）にまとめて書き出す。
/// 「自分のデータを持ち出せる」ことを約束するための機能で、表計算ソフトでそのまま開けるよう CSV は UTF-8（BOM 付き）にする。
/// </summary>
public sealed class DataExportService(IDbContextFactory<PulseMindDbContext> dbFactory, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public async Task WriteZipAsync(string userId, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, cancellationToken);
        var zone = TimeZones.Resolve(user.TimeZoneId);

        await using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        await WriteEntryAsync(zip, "profile.json", JsonSerializer.Serialize(new
        {
            exportedAtUtc = time.GetUtcNow().UtcDateTime,
            email = user.Email,
            displayName = user.DisplayName,
            timeZone = user.TimeZoneId,
            dailyGoalMinutes = user.DailyGoalMinutes,
            createdAtUtc = user.CreatedAtUtc,
            acceptedTermsVersion = user.AcceptedTermsVersion,
            acceptedTermsAtUtc = user.AcceptedTermsAtUtc,
            twoFactorEnabled = user.TwoFactorEnabled,
        }, Json), cancellationToken);

        var sessions = await db.StudySessions.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.StartedAtUtc).ToListAsync(cancellationToken);
        await WriteCsvAsync(zip, "study_sessions.csv", ["started_local", "ended_local", "minutes", "subject", "focus", "note", "source"],
            sessions.Select(s => new[]
            {
                Local(s.StartedAtUtc, zone), s.EndedAtUtc is DateTime e ? Local(e, zone) : "",
                s.Duration is TimeSpan d ? d.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture) : "",
                s.Subject, s.Focus?.ToString(CultureInfo.InvariantCulture) ?? "", s.Note ?? "", s.Source.ToString(),
            }), cancellationToken);

        var records = await db.DailyRecords.AsNoTracking().Where(r => r.UserId == userId).OrderBy(r => r.Date).ToListAsync(cancellationToken);
        await WriteCsvAsync(zip, "daily_records.csv", ["date", "sleep_hours", "steps", "mood", "note"],
            records.Select(r => new[]
            {
                r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.SleepHours?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
                r.Steps?.ToString(CultureInfo.InvariantCulture) ?? "", r.Mood?.ToString(CultureInfo.InvariantCulture) ?? "", r.Note ?? "",
            }), cancellationToken);

        // 心拍は量が多いので、少しずつ読みながら書く
        var heartEntry = zip.CreateEntry("heart_rate_minutes.csv", CompressionLevel.Optimal);
        await using (var stream = await heartEntry.OpenAsync(cancellationToken))
        await using (var writer = new StreamWriter(stream, Utf8WithBom))
        {
            await writer.WriteLineAsync("minute_local,source,average_bpm,min_bpm,max_bpm,samples");
            await foreach (var m in db.HeartRateMinutes.AsNoTracking().Where(m => m.UserId == userId).OrderBy(m => m.MinuteUtc).AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                await writer.WriteLineAsync(string.Join(',', Local(m.MinuteUtc, zone), m.Source,
                    m.AverageBpm.ToString("0.#", CultureInfo.InvariantCulture), m.MinBpm.ToString("0.#", CultureInfo.InvariantCulture),
                    m.MaxBpm.ToString("0.#", CultureInfo.InvariantCulture), m.SampleCount.ToString(CultureInfo.InvariantCulture)));
            }
        }

        // 機器はトークンのハッシュを含めない（持ち出しても使い道がなく、漏れる理由だけが増えるため）
        var devices = await db.Devices.AsNoTracking().Where(d => d.UserId == userId).OrderBy(d => d.CreatedAtUtc).ToListAsync(cancellationToken);
        await WriteCsvAsync(zip, "devices.csv", ["name", "token_prefix", "created_local", "last_seen_local", "revoked_local"],
            devices.Select(d => new[]
            {
                d.Name, d.TokenPrefix, Local(d.CreatedAtUtc, zone),
                d.LastSeenAtUtc is DateTime s ? Local(s, zone) : "", d.RevokedAtUtc is DateTime r ? Local(r, zone) : "",
            }), cancellationToken);

        await WriteEntryAsync(zip, "README.txt", $"""
            Pulse & Mind Study から書き出したデータです。
            時刻は「{user.TimeZoneId}」のタイムゾーンで表しています。

            profile.json            アカウントの設定
            study_sessions.csv      学習の記録（focus: 集中度 1〜5）
            daily_records.csv       毎日の記録（mood: 気分 1〜5）
            heart_rate_minutes.csv  1分ごとにまとめた心拍数（source: Arduino / AppleHealth）
            devices.csv             登録した機器（トークンは含みません）
            """, cancellationToken);
    }

    private static string Local(DateTime utc, TimeZoneInfo zone) =>
        TimeZones.ToLocal(utc, zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static async Task WriteEntryAsync(ZipArchive zip, string name, string content, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(ct);
        await using var writer = new StreamWriter(stream, Utf8WithBom);
        await writer.WriteAsync(content.AsMemory(), ct);
    }

    private static async Task WriteCsvAsync(ZipArchive zip, string name, string[] header, IEnumerable<string[]> rows, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = await entry.OpenAsync(ct);
        await using var writer = new StreamWriter(stream, Utf8WithBom);
        await writer.WriteLineAsync(string.Join(',', header));
        foreach (var row in rows) await writer.WriteLineAsync(string.Join(',', row.Select(Escape)));
    }

    /// <summary>CSV の値を囲む。表計算ソフトで数式として実行されないよう、= + - @ で始まる値の前には ' を付ける</summary>
    internal static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal) && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
