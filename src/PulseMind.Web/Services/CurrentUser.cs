using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using PulseMind.Core.Profiles;

namespace PulseMind.Web.Services;

/// <summary>
/// ログイン中の利用者の情報を画面に渡す。
/// 画面の接続（Blazor の回線）ごとに1つ作られ、同じ接続の中では読み込んだ設定を使い回す。
/// </summary>
public sealed class CurrentUser(AuthenticationStateProvider authentication, UserProfileService profiles)
{
    private UserProfile? cached;

    public async Task<UserProfile> GetAsync(CancellationToken cancellationToken = default)
    {
        var state = await authentication.GetAuthenticationStateAsync();
        string userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("ログインしていません。");

        if (cached?.UserId == userId) return cached;
        cached = await profiles.GetAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("利用者が見つかりません。");
        return cached;
    }

    /// <summary>設定を変えたあとに呼び、次回は読み直すようにする</summary>
    public void Invalidate() => cached = null;
}
