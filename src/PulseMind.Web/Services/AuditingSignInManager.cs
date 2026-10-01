using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PulseMind.Core.Data;
using PulseMind.Core.Domain;
using PulseMind.Core.Privacy;

namespace PulseMind.Web.Services;

/// <summary>
/// ログインの成功・失敗・ロックを「セキュリティの記録」に残す SignInManager。
/// 利用者が設定画面で「身に覚えのないログイン」に気付けるようにする。
/// </summary>
public sealed class AuditingSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    AuditLog audit)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<SignInResult> PasswordSignInAsync(ApplicationUser user, string password, bool isPersistent, bool lockoutOnFailure)
    {
        ArgumentNullException.ThrowIfNull(user);
        var result = await base.PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure);
        if (result.IsLockedOut) await RecordAsync(user.Id, AuditKind.LockedOut);
        else if (!result.Succeeded && !result.RequiresTwoFactor && !result.IsNotAllowed) await RecordAsync(user.Id, AuditKind.SignInFailed);
        return result;
    }

    protected override async Task<SignInResult> SignInOrTwoFactorAsync(ApplicationUser user, bool isPersistent, string? loginProvider = null, bool bypassTwoFactor = false)
    {
        ArgumentNullException.ThrowIfNull(user);
        var result = await base.SignInOrTwoFactorAsync(user, isPersistent, loginProvider, bypassTwoFactor);
        if (result.Succeeded) await RecordAsync(user.Id, AuditKind.SignedIn, loginProvider);
        return result;
    }

    public override async Task<SignInResult> TwoFactorAuthenticatorSignInAsync(string code, bool isPersistent, bool rememberClient)
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        var result = await base.TwoFactorAuthenticatorSignInAsync(code, isPersistent, rememberClient);
        if (user is not null) await RecordTwoFactorAsync(user.Id, result, "2段階認証");
        return result;
    }

    public override async Task<SignInResult> TwoFactorRecoveryCodeSignInAsync(string recoveryCode)
    {
        var user = await GetTwoFactorAuthenticationUserAsync();
        var result = await base.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
        if (user is not null) await RecordTwoFactorAsync(user.Id, result, "回復用コード");
        return result;
    }

    private Task RecordTwoFactorAsync(string userId, SignInResult result, string method) =>
        result.Succeeded ? RecordAsync(userId, AuditKind.SignedIn, method)
        : result.IsLockedOut ? RecordAsync(userId, AuditKind.LockedOut)
        : RecordAsync(userId, AuditKind.SignInFailed, method);

    private Task RecordAsync(string userId, AuditKind kind, string? detail = null) =>
        audit.WriteAsync(userId, kind, detail, Context.Connection.RemoteIpAddress);
}
