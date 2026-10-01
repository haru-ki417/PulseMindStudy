using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseMind.Core.Data;

namespace PulseMind.Tests;

/// <summary>アプリ全体を起動して確かめる（データベースはメモリ上の SQLite に差し替える）</summary>
public sealed class PulseMindAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", "unused");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<PulseMindDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PulseMindDbContext>>();
            services.AddDbContextFactory<PulseMindDbContext>(o => o.UseSqlite(connection));

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<PulseMindDbContext>().Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}

public sealed class WebAppTests(PulseMindAppFactory factory) : IClassFixture<PulseMindAppFactory>
{
    [Fact]
    public async Task トップページが表示される()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Pulse &amp; Mind Study", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 死活監視はデータベースに接続できれば正常を返す()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var db = await client.GetAsync(new Uri("/healthz/db", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, db.StatusCode);
    }

    [Fact]
    public async Task ログインしていなければアカウント画面からログイン画面へ移る()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(new Uri("/Account/Manage", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString() ?? "", StringComparison.Ordinal);
    }
}
