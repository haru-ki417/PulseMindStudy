using System.IO.Compression;
using System.Text;
using PulseMind.Core.Health;
using PulseMind.Core.Records;
using PulseMind.Core.Time;

namespace PulseMind.Tests;

public class AppleHealthTests
{
    private static readonly TimeZoneInfo Tokyo = TimeZones.Resolve("Asia/Tokyo");
    private static readonly DateTime Since = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // 実際の書き出しと同じ形（DTD 付き）の小さな見本
    private const string Export = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE HealthData [
        <!ELEMENT HealthData (ExportDate,Me,(Record|Workout)*)>
        <!ATTLIST Record type CDATA #REQUIRED>
        ]>
        <HealthData locale="ja_JP">
         <ExportDate value="2026-04-02 08:00:00 +0900"/>
         <Me HKCharacteristicTypeIdentifierBiologicalSex="HKBiologicalSexNotSet"/>
         <Record type="HKQuantityTypeIdentifierHeartRate" sourceName="Watch" unit="count/min" startDate="2026-04-01 21:00:10 +0900" endDate="2026-04-01 21:00:10 +0900" value="72"/>
         <Record type="HKQuantityTypeIdentifierHeartRate" sourceName="Watch" unit="count/min" startDate="2026-04-01 21:00:40 +0900" endDate="2026-04-01 21:00:40 +0900" value="78"/>
         <Record type="HKQuantityTypeIdentifierHeartRate" sourceName="Watch" unit="count/min" startDate="2025-04-01 21:00:40 +0900" endDate="2025-04-01 21:00:40 +0900" value="60"/>
         <Record type="HKQuantityTypeIdentifierHeartRate" sourceName="Watch" unit="count/min" startDate="broken" endDate="broken" value="60"/>
         <Record type="HKQuantityTypeIdentifierStepCount" sourceName="iPhone" unit="count" startDate="2026-04-01 08:00:00 +0900" endDate="2026-04-01 08:10:00 +0900" value="1000"/>
         <Record type="HKQuantityTypeIdentifierStepCount" sourceName="iPhone" unit="count" startDate="2026-04-01 12:00:00 +0900" endDate="2026-04-01 12:10:00 +0900" value="2000"/>
         <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Watch" unit="count" startDate="2026-04-01 08:00:00 +0900" endDate="2026-04-01 08:10:00 +0900" value="2500"/>
         <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Watch" startDate="2026-04-01 23:30:00 +0900" endDate="2026-04-02 03:00:00 +0900" value="HKCategoryValueSleepAnalysisAsleepCore"/>
         <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Watch" startDate="2026-04-02 03:00:00 +0900" endDate="2026-04-02 03:20:00 +0900" value="HKCategoryValueSleepAnalysisAwake"/>
         <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Watch" startDate="2026-04-02 03:20:00 +0900" endDate="2026-04-02 06:50:00 +0900" value="HKCategoryValueSleepAnalysisAsleepREM"/>
         <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="iPhone" startDate="2026-04-02 01:00:00 +0900" endDate="2026-04-02 02:00:00 +0900" value="HKCategoryValueSleepAnalysisAsleepUnspecified"/>
         <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="iPhone" startDate="2026-04-01 23:00:00 +0900" endDate="2026-04-02 07:00:00 +0900" value="HKCategoryValueSleepAnalysisInBed"/>
         <Record type="HKQuantityTypeIdentifierBodyMass" sourceName="iPhone" unit="kg" startDate="2026-04-01 08:00:00 +0900" endDate="2026-04-01 08:00:00 +0900" value="60"/>
        </HealthData>
        """;

    [Fact]
    public void 時差付きの日時をUTCに変換できる()
    {
        Assert.True(AppleHealthExportReader.TryParseDate("2026-04-01 21:00:10 +0900", out var utc));
        Assert.Equal(new DateTime(2026, 4, 1, 12, 0, 10, DateTimeKind.Utc), utc);
        Assert.True(AppleHealthExportReader.TryParseDate("2026-04-01 06:00:00 -0430", out utc));
        Assert.Equal(new DateTime(2026, 4, 1, 10, 30, 0, DateTimeKind.Utc), utc);
        Assert.False(AppleHealthExportReader.TryParseDate("2026-04-01T21:00:10Z", out _));
    }

    [Fact]
    public void 書き出したXMLから心拍と歩数と睡眠を読み取る()
    {
        var data = AppleHealthExportReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(Export)), Tokyo, Since, cancellationToken: TestContext.Current.CancellationToken);

        // 心拍: 期間外（2025年）は除き、壊れた日付は数える
        Assert.Equal([72.0, 78.0], data.HeartRates.Select(h => h.Bpm));
        Assert.Equal(1, data.SkippedRecords);

        // 歩数: iPhone 3000 歩と Watch 2500 歩は同じ歩きを二重に記録したものなので、多い方の 3000 歩を使う
        Assert.Equal(3000, data.StepsPerDay[new DateOnly(2026, 4, 1)]);

        // 睡眠: 23:30-3:00 と 3:20-6:50（目が覚めていた20分は除く）。iPhone の 1:00-2:00 は重なりなので数えない。
        // 「ベッドにいた」だけの記録も数えない → 3.5 + 3.5 = 7 時間を、目が覚めた 4/2 に数える
        Assert.Equal(7.0, data.SleepHoursPerDay[new DateOnly(2026, 4, 2)], 6);
        Assert.Single(data.SleepHoursPerDay);
    }

    [Fact]
    public async Task zipのままでも読める()
    {
        var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("apple_health_export/export.xml");
            await using var w = await entry.OpenAsync(TestContext.Current.CancellationToken);
            await w.WriteAsync(Encoding.UTF8.GetBytes(Export), TestContext.Current.CancellationToken);
        }
        zipBytes.Position = 0;

        var data = await AppleHealthExportReader.ReadAsync(zipBytes, Tokyo, Since, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, data.HeartRates.Count);
    }

    [Fact]
    public async Task 外部の定義を読み込もうとするXMLでも外へ読みに行かない()
    {
        const string evil = """
            <?xml version="1.0"?>
            <!DOCTYPE HealthData [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <HealthData><Record type="HKQuantityTypeIdentifierHeartRate" startDate="2026-04-01 21:00:10 +0900" endDate="2026-04-01 21:00:10 +0900" value="70"/><Note>&xxe;</Note></HealthData>
            """;
        var ex = await Record.ExceptionAsync(() => Task.Run(() => AppleHealthExportReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(evil)), Tokyo, Since), TestContext.Current.CancellationToken));
        // DTD を無視するので、実体参照は展開されず、エラーになるか空のまま読み進む（どちらでもファイルの中身は漏れない）
        Assert.True(ex is null || ex is System.Xml.XmlException);
    }

    [Fact]
    public async Task 取り込んだ歩数は上書きし睡眠は手入力を優先する()
    {
        await using var db = await TestDatabase.CreateAsync();
        string user = await db.AddUserAsync();
        var records = new DailyRecordService(db.Factory, db.Time);
        var day1 = new DateOnly(2026, 3, 30);
        var day2 = new DateOnly(2026, 3, 31);
        await records.SaveAsync(user, new DailyRecordInput(day1, 8, 100, 4, "手入力"), Tokyo, TestContext.Current.CancellationToken);

        int updated = await records.ApplyImportedAsync(user,
            new Dictionary<DateOnly, int> { [day1] = 9000, [day2] = 4000 },
            new Dictionary<DateOnly, double> { [day1] = 6.5, [day2] = 7.25 },
            overwriteSleep: false, TestContext.Current.CancellationToken);

        Assert.Equal(2, updated);
        var list = await records.ListAsync(user, day1, day2, TestContext.Current.CancellationToken);
        Assert.Equal((9000, 8.0, 4, "手入力"), (list[0].Steps!.Value, list[0].SleepHours!.Value, list[0].Mood!.Value, list[0].Note!));
        Assert.Equal((4000, 7.25), (list[1].Steps!.Value, list[1].SleepHours!.Value));
    }
}
