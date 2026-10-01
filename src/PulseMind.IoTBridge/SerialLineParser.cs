using System.Globalization;
using System.Text.RegularExpressions;

namespace PulseMind.IoTBridge;

/// <summary>
/// Arduino から届く1行を読み、心拍数を取り出す。次のどの形にも対応する。
///   "BPM:72"（付属のスケッチ）、"72"（数値だけ）、"♥  A HeartBeat Happened ! BPM: 72"（PulseSensor Playground の見本）
/// </summary>
public static partial class SerialLineParser
{
    [GeneratedRegex(@"BPM\s*[:=]?\s*(\d{2,3}(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BpmPattern();

    public static bool TryParse(string? line, out double bpm)
    {
        bpm = 0;
        if (string.IsNullOrWhiteSpace(line)) return false;
        string text = line.Trim();

        var match = BpmPattern().Match(text);
        string number = match.Success ? match.Groups[1].Value : text;
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out bpm) && bpm is >= 20 and <= 260;
    }
}
