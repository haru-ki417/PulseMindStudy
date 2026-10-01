using System.ComponentModel.DataAnnotations;

namespace PulseMind.Core.Domain;

/// <summary>1日ごとの記録（睡眠・歩数・気分）。日付は利用者のタイムゾーンでの日付。</summary>
public sealed class DailyRecord
{
    public const int NoteMaxLength = 500;

    public long Id { get; set; }

    [MaxLength(450)]
    public required string UserId { get; set; }

    /// <summary>利用者のタイムゾーンでの日付</summary>
    public DateOnly Date { get; set; }

    /// <summary>前の晩の睡眠時間（時間）</summary>
    public double? SleepHours { get; set; }

    public int? Steps { get; set; }

    /// <summary>気分（1: とても悪い 〜 5: とても良い）</summary>
    public int? Mood { get; set; }

    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
