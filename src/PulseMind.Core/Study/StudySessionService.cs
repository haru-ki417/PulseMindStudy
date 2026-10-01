using Microsoft.EntityFrameworkCore;
using PulseMind.Core.Data;
using PulseMind.Core.Domain;
using PulseMind.Core.Time;

namespace PulseMind.Core.Study;

/// <summary>手入力で学習を記録するときの内容</summary>
public sealed record ManualStudyInput(DateTime StartUtc, DateTime EndUtc, string? Subject, int? Focus = null, string? Note = null);

/// <summary>タイマーを止めた結果。止め忘れで長くなりすぎた場合は上限で区切り、そのことを知らせる。</summary>
public sealed record StopResult(StudySession Session, bool WasCapped);

/// <summary>学習の記録（タイマーの開始・終了、手入力、削除、集計）</summary>
public sealed class StudySessionService(IDbContextFactory<PulseMindDbContext> dbFactory, TimeProvider time)
{
    /// <summary>1回の学習として認める最長の時間。タイマーの止め忘れで1日中「勉強中」にならないようにする。</summary>
    public static readonly TimeSpan MaxSessionLength = TimeSpan.FromHours(12);

    /// <summary>端末の時計のずれを考え、この分だけ未来の時刻までは受け付ける</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public async Task<StudySession?> GetRunningAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await FindRunningAsync(db, userId, cancellationToken);
    }

    private static Task<StudySession?> FindRunningAsync(PulseMindDbContext db, string userId, CancellationToken cancellationToken) =>
        db.StudySessions.Where(s => s.UserId == userId && s.EndedAtUtc == null)
            .OrderByDescending(s => s.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<StudySession> StartAsync(string userId, string? subject, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await FindRunningAsync(db, userId, cancellationToken) is not null)
            throw new UserInputException("すでに計測中の学習があります。先に終了してください。");

        var now = UtcNow;
        var session = new StudySession
        {
            UserId = userId,
            StartedAtUtc = now,
            Subject = NormalizeSubject(subject),
            Source = DataSource.Manual,
            CreatedAtUtc = now,
        };
        db.StudySessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<StopResult> StopAsync(string userId, int? focus = null, string? note = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await FindRunningAsync(db, userId, cancellationToken)
            ?? throw new UserInputException("計測中の学習はありません。");

        ValidateFocus(focus);
        var end = UtcNow;
        bool capped = end - session.StartedAtUtc > MaxSessionLength;
        if (capped) end = session.StartedAtUtc + MaxSessionLength;

        session.EndedAtUtc = end;
        session.Focus = focus;
        session.Note = NormalizeNote(note);
        await db.SaveChangesAsync(cancellationToken);
        return new StopResult(session, capped);
    }

    /// <summary>計測中の学習を記録せずに取り消す</summary>
    public async Task<bool> CancelRunningAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await FindRunningAsync(db, userId, cancellationToken);
        if (session is null) return false;
        db.StudySessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<StudySession> AddManualAsync(string userId, ManualStudyInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        DateTime start = AsUtc(input.StartUtc), end = AsUtc(input.EndUtc);

        if (end <= start) throw new UserInputException("終了時刻は開始時刻より後にしてください。");
        if (end - start > MaxSessionLength) throw new UserInputException($"1回の記録は {MaxSessionLength.TotalHours:0} 時間までです。分けて記録してください。");
        if (end > UtcNow + ClockSkew) throw new UserInputException("未来の時刻は記録できません。");
        ValidateFocus(input.Focus);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // 同じ時間帯に2つの学習が重なると、合計時間が実際より多くなってしまうので受け付けない
        var now = UtcNow;
        bool overlaps = await db.StudySessions.AnyAsync(s =>
            s.UserId == userId && s.StartedAtUtc < end && (s.EndedAtUtc ?? now) > start, cancellationToken);
        if (overlaps) throw new UserInputException("この時間帯には、すでに別の学習が記録されています。");

        var session = new StudySession
        {
            UserId = userId,
            StartedAtUtc = start,
            EndedAtUtc = end,
            Subject = NormalizeSubject(input.Subject),
            Focus = input.Focus,
            Note = NormalizeNote(input.Note),
            Source = DataSource.Manual,
            CreatedAtUtc = now,
        };
        db.StudySessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<bool> DeleteAsync(string userId, long sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // 他人の記録は消せないよう、利用者 ID も条件に入れる
        int deleted = await db.StudySessions.Where(s => s.Id == sessionId && s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    /// <summary>期間 [fromUtc, toUtc) に一部でもかかる学習を、新しい順に返す</summary>
    public async Task<IReadOnlyList<StudySession>> ListAsync(string userId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = UtcNow;
        var sessions = await db.StudySessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.StartedAtUtc < toUtc && (s.EndedAtUtc ?? now) > fromUtc)
            .ToListAsync(cancellationToken);
        return sessions.OrderByDescending(s => s.StartedAtUtc).ToList();
    }

    /// <summary>
    /// [from, to] の各日（利用者のタイムゾーン）の学習時間（分）。記録の無い日は 0 として含める。
    /// 計測中の学習は現在時刻までを数える。
    /// </summary>
    public async Task<SortedDictionary<DateOnly, double>> DailyMinutesAsync(
        string userId, DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken cancellationToken = default)
    {
        if (to < from) throw new ArgumentException("期間の終わりが始まりより前です。", nameof(to));

        var (fromUtc, toUtc) = TimeZones.LocalRangeUtc(from, to, zone);
        var now = UtcNow;
        var sessions = await ListAsync(userId, fromUtc, toUtc, cancellationToken);

        var spans = sessions
            .Select(s => StudyTime.Clip(s.StartedAtUtc, s.EndedAtUtc ?? Min(now, s.StartedAtUtc + MaxSessionLength), fromUtc, toUtc))
            .Where(c => c is not null)
            .Select(c => c!.Value);

        var minutes = StudyTime.MinutesPerLocalDay(spans, zone);
        for (var day = from; day <= to; day = day.AddDays(1)) minutes.TryAdd(day, 0);
        return minutes;
    }

    /// <summary>最近使った科目（新しい順、重複なし）。入力の候補として出す。</summary>
    public async Task<IReadOnlyList<string>> RecentSubjectsAsync(string userId, int take = 6, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var since = UtcNow.AddDays(-60);
        var subjects = await db.StudySessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.StartedAtUtc >= since && s.Subject != "")
            .OrderByDescending(s => s.StartedAtUtc)
            .Select(s => s.Subject)
            .Take(200)
            .ToListAsync(cancellationToken);
        return subjects.Distinct(StringComparer.Ordinal).Take(take).ToList();
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    internal static string NormalizeSubject(string? subject)
    {
        string s = (subject ?? "").Trim();
        return s.Length <= StudySession.SubjectMaxLength ? s : s[..StudySession.SubjectMaxLength];
    }

    private static string? NormalizeNote(string? note)
    {
        string s = (note ?? "").Trim();
        if (s.Length == 0) return null;
        if (s.Length > StudySession.NoteMaxLength) throw new UserInputException($"メモは {StudySession.NoteMaxLength} 文字までです。");
        return s;
    }

    private static void ValidateFocus(int? focus)
    {
        if (focus is < 1 or > 5) throw new UserInputException("集中度は 1〜5 で選んでください。");
    }

    internal static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => throw new ArgumentException("時刻は UTC で渡してください（DateTimeKind.Utc）。", nameof(value)),
    };
}
