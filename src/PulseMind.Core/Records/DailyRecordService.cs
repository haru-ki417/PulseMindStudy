using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;
using PulseMind.Core.Domain;
using PulseMind.Core.Time;

namespace PulseMind.Core.Records;

/// <summary>1日の記録の入力内容。null の項目は「入力しない（消す）」の意味。</summary>
public sealed record DailyRecordInput(DateOnly Date, double? SleepHours, int? Steps, int? Mood, string? Note);

/// <summary>睡眠・歩数・気分などの日ごとの記録</summary>
public sealed class DailyRecordService(IDbContextFactory<PulseMindDbContext> dbFactory, TimeProvider time)
{
    public const int MaxSteps = 200_000;

    public async Task<DailyRecord?> GetAsync(string userId, DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.DailyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId && r.Date == date, cancellationToken);
    }

    public async Task<IReadOnlyList<DailyRecord>> ListAsync(string userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.DailyRecords.AsNoTracking()
            .Where(r => r.UserId == userId && r.Date >= from && r.Date <= to)
            .OrderBy(r => r.Date)
            .ToListAsync(cancellationToken);
    }

    /// <summary>その日の記録を作るか、すでにあれば上書きする</summary>
    public async Task<DailyRecord> SaveAsync(string userId, DailyRecordInput input, TimeZoneInfo zone, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var now = time.GetUtcNow().UtcDateTime;
        Validate(input, TimeZones.LocalDate(now, zone));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
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

    /// <summary>
    /// 取り込んだ歩数・睡眠を日ごとの記録に反映する。
    /// 歩数は機器の値で上書きする。睡眠は、手入力の値がある日は overwriteSleep が true のときだけ上書きする
    /// （ヘルスケアの睡眠はつけ忘れで短くなることがあり、本人の入力を優先するため）。
    /// </summary>
    /// <returns>更新した日数</returns>
    public async Task<int> ApplyImportedAsync(
        string userId, IReadOnlyDictionary<DateOnly, int> steps, IReadOnlyDictionary<DateOnly, double> sleepHours, bool overwriteSleep,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(sleepHours);
        // 端末の時計の誤りなどで未来の日付が入っていても、記録しない（日付変更線の差を考えて 1 日の余裕を持たせる）
        var latest = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(1);
        var days = steps.Keys.Union(sleepHours.Keys).Where(d => d <= latest).ToList();
        if (days.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        DateOnly from = days.Min(), to = days.Max();
        var existing = await db.DailyRecords.Where(r => r.UserId == userId && r.Date >= from && r.Date <= to)
            .ToDictionaryAsync(r => r.Date, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        int updated = 0;

        foreach (var day in days)
        {
            bool changed = false;
            if (!existing.TryGetValue(day, out var record))
            {
                record = new DailyRecord { UserId = userId, Date = day };
                db.DailyRecords.Add(record);
            }
            if (steps.TryGetValue(day, out int s) && s is >= 0 and <= MaxSteps && record.Steps != s)
            {
                record.Steps = s;
                changed = true;
            }
            if (sleepHours.TryGetValue(day, out double h) && h is > 0 and <= 24 && (record.SleepHours is null || overwriteSleep))
            {
                record.SleepHours = Math.Round(h, 2);
                changed = true;
            }
            if (changed)
            {
                record.UpdatedAtUtc = now;
                updated++;
            }
            else if (record.Id == 0)
            {
                db.DailyRecords.Remove(record);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return updated;
    }
}
