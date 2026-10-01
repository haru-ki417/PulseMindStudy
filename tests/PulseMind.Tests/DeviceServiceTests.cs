using Microsoft.EntityFrameworkCore;
using PulseMind.Core;
using PulseMind.Core.Devices;

namespace PulseMind.Tests;

public class DeviceServiceTests
{
    [Fact]
    public void トークンは毎回異なり決まった形をしている()
    {
        var (a, prefix, hash) = DeviceTokens.Create();
        var (b, _, _) = DeviceTokens.Create();

        Assert.NotEqual(a, b);
        Assert.StartsWith(DeviceTokens.Prefix, a, StringComparison.Ordinal);
        Assert.True(DeviceTokens.HasValidShape(a));
        Assert.Equal(a[..DeviceTokens.DisplayPrefixLength], prefix);
        Assert.Equal(64, hash.Length);
        Assert.False(DeviceTokens.HasValidShape("pmd_short"));
        Assert.False(DeviceTokens.HasValidShape(a.Replace('_', '!')));
        Assert.False(DeviceTokens.HasValidShape(null));
    }

    [Fact]
    public async Task データベースにはトークンそのものを保存しない()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var issued = await new DeviceService(db.Factory, db.Time).RegisterAsync(user, "机の Arduino", TestContext.Current.CancellationToken);

        await using var check = db.NewContext();
        var stored = await check.Devices.SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(issued.Token, stored.TokenHash);
        Assert.DoesNotContain(issued.Token, stored.TokenHash, StringComparison.Ordinal);
        Assert.Equal(DeviceTokens.Hash(issued.Token), stored.TokenHash);
    }

    [Fact]
    public async Task 正しいトークンで認証でき無効にした後は認証できない()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var service = new DeviceService(db.Factory, db.Time);
        var issued = await service.RegisterAsync(user, "机の Arduino", TestContext.Current.CancellationToken);

        var device = await service.AuthenticateAsync(issued.Token, TestContext.Current.CancellationToken);
        Assert.NotNull(device);
        Assert.Equal(user, device.UserId);
        Assert.NotNull(device.LastSeenAtUtc);

        Assert.Null(await service.AuthenticateAsync(issued.Token[..^1] + (issued.Token[^1] == 'A' ? 'B' : 'A'), TestContext.Current.CancellationToken));

        Assert.True(await service.RevokeAsync(user, issued.Device.Id, TestContext.Current.CancellationToken));
        Assert.Null(await service.AuthenticateAsync(issued.Token, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task 他の人の機器は無効にできない()
    {
        await using var db = await TestDatabase.CreateAsync();
        string alice = await db.AddUserAsync("alice@example.com");
        string bob = await db.AddUserAsync("bob@example.com");
        var service = new DeviceService(db.Factory, db.Time);
        var issued = await service.RegisterAsync(alice, "Arduino", TestContext.Current.CancellationToken);

        Assert.False(await service.RevokeAsync(bob, issued.Device.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(await service.AuthenticateAsync(issued.Token, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task 登録できる機器の数には上限がある()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var service = new DeviceService(db.Factory, db.Time);

        for (int i = 0; i < DeviceService.MaxActiveDevicesPerUser; i++)
            await service.RegisterAsync(user, $"機器{i}", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<UserInputException>(() => service.RegisterAsync(user, "もう1台", TestContext.Current.CancellationToken));
    }
}
