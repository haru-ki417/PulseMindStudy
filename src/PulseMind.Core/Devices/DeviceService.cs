using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;
using PulseMind.Core.Domain;

namespace PulseMind.Core.Devices;

/// <summary>登録した機器と、一度だけ表示するトークン</summary>
public sealed record IssuedDeviceToken(Device Device, string Token);

/// <summary>機器の登録・一覧・無効化と、トークンによる認証</summary>
public sealed class DeviceService(IDbContextFactory<PulseMindDbContext> dbFactory, TimeProvider time)
{
    /// <summary>1人が同時に使える機器の数</summary>
    public const int MaxActiveDevicesPerUser = 5;

    /// <summary>「最後に通信した時刻」を更新する間隔。送信のたびに書き込むとデータベースの負担になるため。</summary>
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(1);

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public async Task<IssuedDeviceToken> RegisterAsync(string userId, string? name, CancellationToken cancellationToken = default)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) throw new UserInputException("機器の名前を入力してください。");
        if (trimmed.Length > Device.NameMaxLength) throw new UserInputException($"機器の名前は {Device.NameMaxLength} 文字までです。");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        int active = await db.Devices.CountAsync(d => d.UserId == userId && d.RevokedAtUtc == null, cancellationToken);
        if (active >= MaxActiveDevicesPerUser)
            throw new UserInputException($"登録できる機器は {MaxActiveDevicesPerUser} 台までです。使っていない機器を無効にしてください。");

        var (token, prefix, hash) = DeviceTokens.Create();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = trimmed,
            TokenPrefix = prefix,
            TokenHash = hash,
            CreatedAtUtc = UtcNow,
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync(cancellationToken);
        return new IssuedDeviceToken(device, token);
    }

    public async Task<IReadOnlyList<Device>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var devices = await db.Devices.AsNoTracking().Where(d => d.UserId == userId).ToListAsync(cancellationToken);
        return devices.OrderBy(d => d.RevokedAtUtc is not null).ThenByDescending(d => d.CreatedAtUtc).ToList();
    }

    public async Task<bool> RevokeAsync(string userId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId, cancellationToken);
        if (device is null || device.RevokedAtUtc is not null) return false;
        device.RevokedAtUtc = UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>トークンから有効な機器を探す。見つからない・無効にされている場合は null。</summary>
    public async Task<Device?> AuthenticateAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (!DeviceTokens.HasValidShape(token)) return null;

        string hash = DeviceTokens.Hash(token!);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var device = await db.Devices.FirstOrDefaultAsync(d => d.TokenHash == hash && d.RevokedAtUtc == null, cancellationToken);
        if (device is null) return null;

        var now = UtcNow;
        if (device.LastSeenAtUtc is not DateTime seen || now - seen >= LastSeenResolution)
        {
            device.LastSeenAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
        }
        return device;
    }
}
