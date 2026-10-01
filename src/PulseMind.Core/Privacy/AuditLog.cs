using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;
using PulseMind.Core.Domain;

namespace PulseMind.Core.Privacy;

/// <summary>セキュリティの記録を書き込み・読み出す</summary>
public sealed class AuditLog(IDbContextFactory<PulseMindDbContext> dbFactory, TimeProvider time)
{
    /// <summary>この日数より古い記録は自動で消す（必要以上に持ち続けない）</summary>
    public const int RetentionDays = 180;

    public async Task WriteAsync(string userId, AuditKind kind, string? detail = null, IPAddress? ip = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        db.AuditEvents.Add(new AuditEvent
        {
            UserId = userId,
            Kind = kind,
            AtUtc = now,
            Detail = detail is { Length: > 200 } ? detail[..200] : detail,
            MaskedIp = Mask(ip),
        });
        await db.SaveChangesAsync(cancellationToken);

        // ついでに古い記録を消す（専用の定期処理を持たずに済むように）
        var cutoff = now.AddDays(-RetentionDays);
        // （しばらく使っていない利用者の分も、ここでまとめて消す）
        await db.AuditEvents.Where(e => e.AtUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEvent>> RecentAsync(string userId, int take = 20, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var events = await db.AuditEvents.AsNoTracking().Where(e => e.UserId == userId).ToListAsync(cancellationToken);
        return events.OrderByDescending(e => e.AtUtc).Take(take).ToList();
    }

    /// <summary>IPv4 は最後の1区切り、IPv6 は後ろ半分を伏せる</summary>
    public static string? Mask(IPAddress? ip)
    {
        if (ip is null) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return $"{b[0]}.{b[1]}.{b[2]}.x";
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return string.Join(':', Enumerable.Range(0, 4).Select(i => ((b[i * 2] << 8) | b[i * 2 + 1]).ToString("x", System.Globalization.CultureInfo.InvariantCulture))) + ":x:x:x:x";
        }
        return null;
    }
}
