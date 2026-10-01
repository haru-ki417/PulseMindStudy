using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;
using PulseMind.Core.Time;

namespace PulseMind.Core.Profiles;

/// <summary>画面で使う利用者の設定</summary>
public sealed record UserProfile(
    string UserId, string? DisplayName, string Email, string TimeZoneId, int DailyGoalMinutes,
    string? ExamName = null, DateOnly? ExamDate = null, bool OnboardingCompleted = true)
{
    public TimeZoneInfo Zone => TimeZones.Resolve(TimeZoneId);

    /// <summary>あいさつなどに使う名前（ニックネームが無ければメールアドレスの @ より前）</summary>
    public string Name => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : Email.Split('@')[0];
}

public sealed record UserProfileInput(string? DisplayName, string TimeZoneId, int DailyGoalMinutes);

/// <summary>目標の試験。名前と日付の両方が空なら「設定しない」。</summary>
public sealed record ExamInput(string? Name, DateOnly? Date);

/// <summary>よく使われる試験の候補（日付は公表された実施日）</summary>
public static class ExamPresets
{
    public static readonly IReadOnlyList<(string Name, DateOnly Date)> All =
    [
        ("大学入学共通テスト", new DateOnly(2027, 1, 16)),
    ];
}

/// <summary>ニックネーム・タイムゾーン・学習目標の読み書き</summary>
public sealed class UserProfileService(IDbContextFactory<PulseMindDbContext> dbFactory)
{
    public const int MinGoalMinutes = 10;
    public const int MaxGoalMinutes = 16 * 60;

    public async Task<UserProfile?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserProfile(u.Id, u.DisplayName, u.Email ?? "", u.TimeZoneId, u.DailyGoalMinutes, u.ExamName, u.ExamDate, u.OnboardingCompleted))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpdateAsync(string userId, UserProfileInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        string? name = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim();
        if (name is { Length: > 40 }) throw new UserInputException("ニックネームは 40 文字までです。");
        if (!TimeZones.IsKnown(input.TimeZoneId)) throw new UserInputException("タイムゾーンを一覧から選んでください。");
        if (input.DailyGoalMinutes is < MinGoalMinutes or > MaxGoalMinutes)
            throw new UserInputException($"1日の目標は {MinGoalMinutes} 分〜{MaxGoalMinutes / 60} 時間で設定してください。");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("利用者が見つかりません。");
        user.DisplayName = name;
        user.TimeZoneId = input.TimeZoneId;
        user.DailyGoalMinutes = input.DailyGoalMinutes;
        user.OnboardingCompleted = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateExamAsync(string userId, ExamInput input, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        string? name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim();
        if (name is { Length: > 40 }) throw new UserInputException("試験の名前は 40 文字までです。");
        if (name is null != input.Date is null) throw new UserInputException("試験の名前と日付の両方を入力してください（使わない場合は両方とも空にします）。");
        if (input.Date is DateOnly d && (d < today || d > today.AddYears(5))) throw new UserInputException("試験の日付は、今日から 5 年以内の日を選んでください。");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("利用者が見つかりません。");
        user.ExamName = name;
        user.ExamDate = input.Date;
        await db.SaveChangesAsync(cancellationToken);
    }
}
