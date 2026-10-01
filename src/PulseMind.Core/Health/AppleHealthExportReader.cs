using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace PulseMind.Core.Health;

/// <summary>ヘルスケアの書き出しから読み取った内容</summary>
public sealed class AppleHealthData
{
    /// <summary>心拍数の測定値（UTC）</summary>
    public List<HeartRateSample> HeartRates { get; } = [];

    /// <summary>日ごとの歩数（利用者のタイムゾーンでの日付）</summary>
    public SortedDictionary<DateOnly, int> StepsPerDay { get; } = [];

    /// <summary>日ごとの睡眠時間（時間）。目が覚めた日の日付で数える。</summary>
    public SortedDictionary<DateOnly, double> SleepHoursPerDay { get; } = [];

    /// <summary>読み飛ばした記録（日付が読めないなど）の数</summary>
    public int SkippedRecords { get; set; }
}

/// <summary>
/// iPhone の「ヘルスケア」アプリの「すべてのヘルスケアデータを書き出す」で作られる export.zip（または中の export.xml）を読む。
/// 数年分で数百 MB になることもあるため、ファイル全体をメモリに読み込まず、先頭から順に1件ずつ読む。
/// </summary>
public static class AppleHealthExportReader
{
    private const string HeartRateType = "HKQuantityTypeIdentifierHeartRate";
    private const string StepCountType = "HKQuantityTypeIdentifierStepCount";
    private const string SleepType = "HKCategoryTypeIdentifierSleepAnalysis";

    /// <summary>「眠っていた」とみなす睡眠の分類（ベッドにいただけ・目が覚めていた時間は含めない）</summary>
    private static readonly HashSet<string> AsleepValues = new(StringComparer.Ordinal)
    {
        "HKCategoryValueSleepAnalysisAsleep",
        "HKCategoryValueSleepAnalysisAsleepUnspecified",
        "HKCategoryValueSleepAnalysisAsleepCore",
        "HKCategoryValueSleepAnalysisAsleepDeep",
        "HKCategoryValueSleepAnalysisAsleepREM",
    };

    /// <summary>読み取る export.xml の大きさの上限（数年分でも収まる大きさ。極端に大きい細工ファイルを避ける）</summary>
    public const long MaxXmlBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>取り込む心拍の測定値の上限（Apple Watch で約 5 年分。これを超える場合は期間を短くしてもらう）</summary>
    public const int MaxHeartRateSamples = 2_000_000;

    /// <summary>歩数を「日 × 記録した機器」ごとに数えるときの組み合わせの上限</summary>
    private const int MaxStepKeys = 50_000;

    /// <summary>zip でも xml でも読めるようにする。zip のときは中の export.xml を探す。</summary>
    public static async Task<AppleHealthData> ReadAsync(
        Stream stream, TimeZoneInfo zone, DateTime sinceUtc, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            // zip を開くには読み戻しのできるストリームが必要なので、一時ファイルに受けてから読む
            string temp = Path.GetTempFileName();
            try
            {
                await using var file = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
                await stream.CopyToAsync(file, cancellationToken);
                file.Position = 0;
                return await ReadAsync(file, zone, sinceUtc, progress, cancellationToken);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        // 先頭の 2 バイトで zip か xml かを見分ける（zip は "PK" で始まる）
        long origin = stream.Position;
        var head = new byte[2];
        int read = await stream.ReadAtLeastAsync(head, 2, throwOnEndOfStream: false, cancellationToken);
        stream.Position = origin;

        if (read == 2 && head[0] == (byte)'P' && head[1] == (byte)'K')
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("export.xml", StringComparison.OrdinalIgnoreCase)
                                                        && !e.FullName.Contains("cda", StringComparison.OrdinalIgnoreCase))
                ?? throw new UserInputException("zip の中に export.xml が見つかりませんでした。ヘルスケアから書き出した「書き出したデータ.zip」を選んでください。");
            await using var xml = await entry.OpenAsync(cancellationToken);
            return Read(xml, zone, sinceUtc, progress, cancellationToken);
        }

        return Read(stream, zone, sinceUtc, progress, cancellationToken);
    }

    /// <summary>export.xml を読む。sinceUtc より前の記録は使わない。</summary>
    public static AppleHealthData Read(Stream xml, TimeZoneInfo zone, DateTime sinceUtc, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var settings = new XmlReaderSettings
        {
            // export.xml には DTD が含まれる。外部の定義を読みに行かない（XXE 対策）よう、DTD は無視する
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };

        var data = new AppleHealthData();
        // 歩数は iPhone と Apple Watch の両方が記録するため、そのまま足すと二重に数えてしまう。
        // 日ごと・記録した機器ごとに合計し、その日に一番多かった機器の値を使う。
        var stepsBySource = new Dictionary<(DateOnly Day, string Source), double>();
        var sleepIntervals = new List<(DateTime Start, DateTime End)>();
        long count = 0;

        using var limited = new LimitedReadStream(xml, MaxXmlBytes);
        using var reader = XmlReader.Create(limited, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "Record") continue;
            if (++count % 20_000 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(count);
            }

            string? type = reader.GetAttribute("type");
            if (type is not (HeartRateType or StepCountType or SleepType)) continue;

            if (!TryParseDate(reader.GetAttribute("startDate"), out var start) || !TryParseDate(reader.GetAttribute("endDate"), out var end))
            {
                data.SkippedRecords++;
                continue;
            }
            if (end < sinceUtc) continue;

            switch (type)
            {
                case HeartRateType:
                    if (double.TryParse(reader.GetAttribute("value"), NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm))
                    {
                        if (data.HeartRates.Count >= MaxHeartRateSamples)
                            throw new UserInputException("心拍の記録が多すぎます。取り込む期間を短くしてください。");
                        data.HeartRates.Add(new HeartRateSample(start, bpm));
                    }
                    else
                        data.SkippedRecords++;
                    break;

                case StepCountType:
                    if (double.TryParse(reader.GetAttribute("value"), NumberStyles.Float, CultureInfo.InvariantCulture, out double steps))
                    {
                        string source = reader.GetAttribute("sourceName") ?? "";
                        if (source.Length > 64) source = source[..64];
                        var key = (Time.TimeZones.LocalDate(start, zone), source);
                        if (stepsBySource.Count < MaxStepKeys || stepsBySource.ContainsKey(key))
                            stepsBySource[key] = stepsBySource.GetValueOrDefault(key) + steps;
                    }
                    else
                    {
                        data.SkippedRecords++;
                    }
                    break;

                case SleepType:
                    if (AsleepValues.Contains(reader.GetAttribute("value") ?? "") && end > start)
                        sleepIntervals.Add((start, end));
                    break;
            }
        }
        progress?.Report(count);

        foreach (var group in stepsBySource.GroupBy(kv => kv.Key.Day))
            data.StepsPerDay[group.Key] = (int)Math.Round(group.Max(kv => kv.Value));

        foreach (var (day, hours) in SleepHoursByWakeDay(sleepIntervals, zone))
            data.SleepHoursPerDay[day] = Math.Round(hours, 2);

        return data;
    }

    /// <summary>
    /// 睡眠の区間を、目が覚めた日ごとの合計時間にする。
    /// iPhone と Apple Watch で同じ時間帯が重なって記録されることがあるため、重なりは1回だけ数える。
    /// 一続きの睡眠（間が3時間未満）は、最後に目が覚めた日にまとめる。
    /// </summary>
    internal static SortedDictionary<DateOnly, double> SleepHoursByWakeDay(IEnumerable<(DateTime Start, DateTime End)> intervals, TimeZoneInfo zone)
    {
        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var (s, e) in intervals.OrderBy(i => i.Start))
        {
            if (merged.Count > 0 && s <= merged[^1].End)
            {
                if (e > merged[^1].End) merged[^1] = (merged[^1].Start, e);
            }
            else
            {
                merged.Add((s, e));
            }
        }

        var result = new SortedDictionary<DateOnly, double>();
        int i = 0;
        while (i < merged.Count)
        {
            double hours = 0;
            int j = i;
            while (true)
            {
                hours += (merged[j].End - merged[j].Start).TotalHours;
                if (j + 1 < merged.Count && merged[j + 1].Start - merged[j].End < TimeSpan.FromHours(3)) j++;
                else break;
            }
            var wakeDay = Time.TimeZones.LocalDate(merged[j].End, zone);
            result[wakeDay] = result.GetValueOrDefault(wakeDay) + hours;
            i = j + 1;
        }
        return result;
    }

    /// <summary>「2026-09-30 23:15:02 +0900」の形の日時を UTC に変換する</summary>
    internal static bool TryParseDate(string? text, out DateTime utc)
    {
        utc = default;
        if (text is null || text.Length != 25 || text[19] != ' ' || (text[20] != '+' && text[20] != '-')) return false;
        if (!DateTime.TryParseExact(text.AsSpan(0, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        if (!int.TryParse(text.AsSpan(21, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int oh)
            || !int.TryParse(text.AsSpan(23, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int om)) return false;

        var offset = new TimeSpan(oh, om, 0);
        if (text[20] == '-') offset = -offset;
        utc = DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        return true;
    }

    /// <summary>決まった大きさより多く読もうとしたら止める（展開すると巨大になる細工 zip への備え）</summary>
    private sealed class LimitedReadStream(Stream inner, long limit) : Stream
    {
        private long total;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => total; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Count(int n)
        {
            total += n;
            if (total > limit) throw new UserInputException("ファイルが大きすぎます。取り込む期間を短くして書き出し直してください。");
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
