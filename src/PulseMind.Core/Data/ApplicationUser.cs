using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace PulseMind.Core.Data;

/// <summary>利用者。ログイン情報（メール・パスワードなど）は ASP.NET Core Identity が管理する。</summary>
public class ApplicationUser : IdentityUser
{
    public const string DefaultTimeZoneId = "Asia/Tokyo";
    public const int DefaultDailyGoalMinutes = 180;

    /// <summary>画面に表示する名前（ニックネーム）</summary>
    [MaxLength(40)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// 利用者のタイムゾーン（IANA 形式、例: Asia/Tokyo）。
    /// 「何日に何分勉強したか」は利用者の暮らす地域の日付で数える必要があるため、日付の区切りに使う。
    /// </summary>
    [MaxLength(64)]
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    /// <summary>1日の学習目標（分）</summary>
    public int DailyGoalMinutes { get; set; } = DefaultDailyGoalMinutes;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>同意した利用規約・プライバシーポリシーの版。規約を改定したら、再同意を求めるのに使う。</summary>
    [MaxLength(20)]
    public string? AcceptedTermsVersion { get; set; }

    public DateTime? AcceptedTermsAtUtc { get; set; }
}
