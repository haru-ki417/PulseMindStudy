using System.ComponentModel.DataAnnotations;

namespace PulseMind.Core.Domain;

/// <summary>1回分の学習。タイマーで計った場合も、あとから手入力した場合もこの形で保存する。</summary>
public sealed class StudySession
{
    public const int SubjectMaxLength = 40;
    public const int NoteMaxLength = 500;

    public long Id { get; set; }

    [MaxLength(450)]
    public required string UserId { get; set; }

    /// <summary>開始時刻（UTC）</summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>終了時刻（UTC）。タイマーで計測中は null。</summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>科目（例: 数学、英語）。自由入力。</summary>
    [MaxLength(SubjectMaxLength)]
    public string Subject { get; set; } = "";

    /// <summary>集中できたかの自己評価（1〜5）。未入力なら null。</summary>
    public int? Focus { get; set; }

    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    public DataSource Source { get; set; } = DataSource.Manual;

    public DateTime CreatedAtUtc { get; set; }

    public bool IsRunning => EndedAtUtc is null;

    public TimeSpan? Duration => EndedAtUtc - StartedAtUtc;
}
