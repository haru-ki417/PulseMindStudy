using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PulseMind.Core;
using PulseMind.Core.Data;
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
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<JapaneseIdentityErrorDescriber>();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// ---- アプリの処理
builder.Services.AddPulseMindCore();
builder.Services.AddScoped<CurrentUser>();

builder.Services.AddHealthChecks().AddDbContextCheck<PulseMindDbContext>();

var app = builder.Build();

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

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

// 死活監視（Azure の正常性チェックが使う）。中身の詳細は返さない
app.MapHealthChecks("/healthz");

await app.RunAsync();

/// <summary>テスト（WebApplicationFactory）から参照できるようにする</summary>
public partial class Program;
