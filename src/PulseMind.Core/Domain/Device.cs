using System.ComponentModel.DataAnnotations;

namespace PulseMind.Core.Domain;

/// <summary>
/// 心拍を送ってくる機器（Arduino と、それをつないだ PC の IoTBridge）。
/// 機器はログイン画面を使えないため、登録時に発行した「機器トークン」で認証する。
/// トークンそのものは保存せず、ハッシュ値だけを保存する（データベースが漏れても、トークンは復元できない）。
/// </summary>
public sealed class Device
{
    public const int NameMaxLength = 40;

    public Guid Id { get; set; }

    [MaxLength(450)]
    public required string UserId { get; set; }

    [MaxLength(NameMaxLength)]
    public required string Name { get; set; }

    /// <summary>トークンの先頭部分。一覧画面で「どのトークンか」を見分けるためだけに使う。</summary>
    [MaxLength(16)]
    public required string TokenPrefix { get; set; }

    /// <summary>トークンの SHA-256 ハッシュ（16進数 64 文字）</summary>
    [MaxLength(64)]
    public required string TokenHash { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastSeenAtUtc { get; set; }

    /// <summary>無効にした時刻。無効にした機器からの送信は受け付けない。</summary>
    public DateTime? RevokedAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc is null;
}
