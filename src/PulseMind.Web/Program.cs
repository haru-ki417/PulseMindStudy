using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PulseMind.Core;
using PulseMind.Core.Data;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using PulseMind.Web.Api;
using PulseMind.Web.Components;
using PulseMind.Web.Components.Account;
using PulseMind.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// ---- データベース（開発時は LocalDB、本番は Azure SQL。接続文字列は設定から読む）
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("接続文字列 'DefaultConnection' が設定されていません。");
// SQL Server が無い環境（Mac / Linux での動作確認）では SQLite も使える
bool useSqlite = string.Equals(builder.Configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase);

// Blazor の画面は長く接続が続くため、DbContext は処理ごとに作り手（ファクトリー）から作る
builder.Services.AddDbContextFactory<PulseMindDbContext>(options =>
{
    if (useSqlite) options.UseSqlite(connectionString);
    else options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
});
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Cookie を暗号化する鍵をデータベースに保存する（再起動・複数台構成でもログインが切れないように）
builder.Services.AddDataProtection()
    .SetApplicationName("PulseMindStudy")
    .PersistKeysToDbContext<PulseMindDbContext>();

// ---- ログイン
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // メール送信の設定が済むまでは、メール確認なしで使えるようにする（設定で切り替え）
        options.SignIn.RequireConfirmedAccount = builder.Configuration.GetValue("Identity:RequireConfirmedAccount", false);
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<PulseMindDbContext>()
    .AddSignInManager<AuditingSignInManager>()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<JapaneseIdentityErrorDescriber>();

// ---- メール（SMTP の設定があれば実際に送る。無ければ送らずに画面へ確認用リンクを出す＝開発用）
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
if (builder.Configuration.GetSection("Smtp").Get<SmtpOptions>() is { IsConfigured: true })
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, SmtpEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// ---- Azure App Service などの中継サーバーの後ろで動くとき、利用者の本当の IP アドレスと https を受け取る
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---- アプリの処理
builder.Services.AddPulseMindCore();
builder.Services.AddScoped<CurrentUser>();

// ---- 送信回数の制限（総当たりや大量送信への備え）
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        var response = context.HttpContext.Response;
        if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
        {
            await response.WriteAsJsonAsync(new { title = "送信が多すぎます。しばらく待ってから送り直してください。" }, ct);
            return;
        }
        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync("""
            <!DOCTYPE html><html lang="ja"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
            <title>しばらくお待ちください</title><body style="font-family:sans-serif;max-width:480px;margin:60px auto;padding:0 16px;line-height:1.7">
            <h1 style="font-size:1.3rem">しばらくお待ちください</h1>
            <p>短い時間に何度も試されたため、一時的に受け付けを止めています。1分ほど待ってから、もう一度お試しください。</p>
            <p><a href="/">ホームへ戻る</a></p></body></html>
            """, ct);
    };

    // ログイン・登録・パスワード再設定の送信: IP アドレスごとに 1 分 10 回まで（パスワードの総当たり対策）
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/Account")
            && !context.Request.Path.StartsWithSegments("/Account/Logout")
            ? RateLimitPartition.GetFixedWindowLimiter("account:" + context.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter(""));

    // 機器 API: 送信元（トークンの先頭、なければ IP アドレス）ごとに 1 分 60 回まで
    options.AddPolicy(DeviceApi.RateLimitPolicy, context =>
    {
        string? token = DeviceApi.ReadBearer(context.Request);
        string key = token is { Length: > 12 } ? "t:" + token[..12] : "ip:" + context.Connection.RemoteIpAddress;
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

builder.Services.AddHealthChecks().AddDbContextCheck<PulseMindDbContext>();

var app = builder.Build();

if (app.Configuration.GetValue("ReverseProxy:TrustForwardedHeaders", false))
{
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// 起動時にデータベースを最新の形にする（設定で無効にできる）
if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<PulseMindDbContext>().Database;
    // マイグレーションは SQL Server 用。SQLite のときはモデルから直接テーブルを作る
    if (useSqlite) await database.EnsureCreatedAsync();
    else await database.MigrateAsync();
}

await DemoDataSeeder.SeedAsync(app.Services, app.Configuration);

// 「ページが見つかりません」などの画面は人向けのページだけに出す（機器向け API は状態コードをそのまま返す）
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();
app.UseSecurityHeaders();

app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.MapDeviceApi();

// 死活監視（Azure の正常性チェックが使う）。中身の詳細は返さない
app.MapHealthChecks("/healthz");

await app.RunAsync();

/// <summary>テスト（WebApplicationFactory）から参照できるようにする</summary>
public partial class Program;
