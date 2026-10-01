using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;
using PulseMind.Core.Domain;
using PulseMind.Core.Time;

namespace PulseMind.Core.Records;

/// <summary>1日の記録の入力内容。null の項目は「入力しない（消す）」の意味。</summary>
public sealed record DailyRecordInput(DateOnly Date, double? SleepHours, int? Steps, int? Mood, string? Note);

/// <summary>睡眠・歩数・気分などの日ごとの記録</summary>
public sealed class DailyRecordService(PulseMindDbContext db, TimeProvider time)
{
    public const int MaxSteps = 200_000;

    public Task<DailyRecord?> GetAsync(string userId, DateOnly date, CancellationToken cancellationToken = default) =>
        db.DailyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId && r.Date == date, cancellationToken);

    public async Task<IReadOnlyList<DailyRecord>> ListAsync(string userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await db.DailyRecords.AsNoTracking()
            .Where(r => r.UserId == userId && r.Date >= from && r.Date <= to)
            .OrderBy(r => r.Date)
            .ToListAsync(cancellationToken);

    /// <summary>その日の記録を作るか、すでにあれば上書きする</summary>
    public async Task<DailyRecord> SaveAsync(string userId, DailyRecordInput input, TimeZoneInfo zone, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var now = time.GetUtcNow().UtcDateTime;
        Validate(input, TimeZones.LocalDate(now, zone));

        var record = await db.DailyRecords.FirstOrDefaultAsync(r => r.UserId == userId && r.Date == input.Date, cancellationToken);
        if (record is null)
        {
            record = new DailyRecord { UserId = userId, Date = input.Date };
            db.DailyRecords.Add(record);
        }

        record.SleepHours = input.SleepHours is double h ? Math.Round(h, 2) : null;
        record.Steps = input.Steps;
        record.Mood = input.Mood;
        record.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        record.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    internal static void Validate(DailyRecordInput input, DateOnly today)
    {
        if (input.Date > today) throw new UserInputException("未来の日付には記録できません。");
        if (input.SleepHours is < 0 or > 24 || input.SleepHours is double h && !double.IsFinite(h))
            throw new UserInputException("睡眠時間は 0〜24 時間で入力してください。");
        if (input.Steps is < 0 or > MaxSteps) throw new UserInputException($"歩数は 0〜{MaxSteps:N0} 歩で入力してください。");
        if (input.Mood is < 1 or > 5) throw new UserInputException("気分は 1〜5 で選んでください。");
        if (input.Note is { Length: > DailyRecord.NoteMaxLength }) throw new UserInputException($"メモは {DailyRecord.NoteMaxLength} 文字までです。");
    }
}
