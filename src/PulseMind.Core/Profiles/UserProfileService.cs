using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;
using PulseMind.Core.Time;

namespace PulseMind.Core.Profiles;

/// <summary>画面で使う利用者の設定</summary>
public sealed record UserProfile(string UserId, string? DisplayName, string Email, string TimeZoneId, int DailyGoalMinutes)
{
    public TimeZoneInfo Zone => TimeZones.Resolve(TimeZoneId);

    /// <summary>あいさつなどに使う名前（ニックネームが無ければメールアドレスの @ より前）</summary>
    public string Name => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : Email.Split('@')[0];
}

public sealed record UserProfileInput(string? DisplayName, string TimeZoneId, int DailyGoalMinutes);

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
            .Select(u => new UserProfile(u.Id, u.DisplayName, u.Email ?? "", u.TimeZoneId, u.DailyGoalMinutes))
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
        await db.SaveChangesAsync(cancellationToken);
    }
}
