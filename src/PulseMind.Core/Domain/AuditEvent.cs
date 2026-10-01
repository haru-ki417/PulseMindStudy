using System.ComponentModel.DataAnnotations;

namespace PulseMind.Core.Domain;

/// <summary>セキュリティにかかわる出来事の種類</summary>
public enum AuditKind
{
    SignedIn = 1,
    SignInFailed = 2,
    LockedOut = 3,
    PasswordChanged = 4,
    DeviceRegistered = 10,
    DeviceRevoked = 11,
    DataExported = 20,
    DataImported = 21,
}

/// <summary>
/// 利用者本人が「いつ・何が起きたか」を確認できるようにする記録（ログインや機器の登録など）。
/// IP アドレスは個人を特定しやすいので、末尾を伏せた形（例: 203.0.113.x）でだけ保存する。
/// </summary>
public sealed class AuditEvent
{
    public long Id { get; set; }

    [MaxLength(450)]
    public required string UserId { get; set; }

    public AuditKind Kind { get; set; }

    public DateTime AtUtc { get; set; }

    /// <summary>補足（機器の名前など）。健康データは書かない。</summary>
    [MaxLength(200)]
    public string? Detail { get; set; }

    /// <summary>末尾を伏せた IP アドレス</summary>
    [MaxLength(64)]
    public string? MaskedIp { get; set; }
}
