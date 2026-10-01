using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PulseMind.Core.Data;
using PulseMind.Core.Devices;
using PulseMind.Core.Health;
using PulseMind.Web.Api;

namespace PulseMind.Tests;

public sealed class DeviceApiTests(PulseMindAppFactory factory) : IClassFixture<PulseMindAppFactory>
{
    private async Task<(string UserId, string Token)> RegisterDeviceAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseMindDbContext>();
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid():N}@example.com", Email = "x@example.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var issued = await scope.ServiceProvider.GetRequiredService<DeviceService>().RegisterAsync(user.Id, "テスト機器", TestContext.Current.CancellationToken);
        return (user.Id, issued.Token);
    }

    private HttpClient Client(string? token)
    {
        var client = factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task 正しいトークンなら心拍を受け付けて保存する()
    {
        var (userId, token) = await RegisterDeviceAsync();
        using var client = Client(token);
        var now = DateTimeOffset.UtcNow;

        var response = await client.PostAsJsonAsync("/api/v1/heart-rate", new
        {
            samples = new object[]
            {
                new { t = now.AddSeconds(-30), bpm = 70 },
                new { t = now.AddSeconds(-20), bpm = 74 },
                new { t = now.AddSeconds(-10), bpm = 400 }, // ありえない値は捨てられる
            },
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HeartRateUploadResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(2, body!.Accepted);
        Assert.Equal(1, body.Rejected);

        using var scope = factory.Services.CreateScope();
        var minutes = await scope.ServiceProvider.GetRequiredService<HeartRateIngestionService>()
            .GetMinutesAsync(userId, now.UtcDateTime.AddMinutes(-5), now.UtcDateTime.AddMinutes(1), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, minutes.Sum(m => m.SampleCount));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pmd_wrongtokenwrongtokenwrongtokenwrongtoken12")]
    [InlineData("not-a-token")]
    public async Task トークンが無いか間違っていれば401(string? token)
    {
        using var client = Client(token);
        var response = await client.PostAsJsonAsync("/api/v1/heart-rate",
            new { samples = new[] { new { t = DateTimeOffset.UtcNow, bpm = 70 } } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 空の送信は400()
    {
        var (_, token) = await RegisterDeviceAsync();
        using var client = Client(token);
        var response = await client.PostAsJsonAsync("/api/v1/heart-rate", new { samples = Array.Empty<object>() }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task pingで機器の名前が返る()
    {
        var (_, token) = await RegisterDeviceAsync();
        using var client = Client(token);
        var ping = await client.GetFromJsonAsync<PingResponse>("/api/v1/ping", TestContext.Current.CancellationToken);
        Assert.Equal("テスト機器", ping!.Device);
    }
}
