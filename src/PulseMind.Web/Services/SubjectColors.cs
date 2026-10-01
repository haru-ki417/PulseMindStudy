namespace PulseMind.Web.Services;

/// <summary>
/// 科目ごとの色。同じ科目はいつも同じ色になるよう、科目名から決める（設定画面で選ばせる手間を省く）。
/// 役割の色（学習の藍・心拍の紅など）と見分けやすいよう、少しずらした 8 色から選ぶ。
/// </summary>
public static class SubjectColors
{
    private static readonly string[] Palette =
    [
        "#4f6bed", // 青
        "#e8743b", // 橙
        "#19a979", // 緑
        "#d64f8e", // 桃
        "#8a5bd6", // 紫
        "#d9a51b", // 黄土
        "#2a9fd6", // 空
        "#7a8b2e", // 草
    ];

    /// <summary>よく使われる科目は、イメージに合う色に固定する</summary>
    private static readonly Dictionary<string, int> Fixed = new(StringComparer.Ordinal)
    {
        ["数学"] = 0, ["英語"] = 1, ["英単語"] = 1, ["英文法"] = 1, ["国語"] = 3, ["現代文"] = 3, ["古文"] = 3, ["漢文"] = 3,
        ["物理"] = 6, ["化学"] = 2, ["生物"] = 7, ["地学"] = 5, ["日本史"] = 4, ["世界史"] = 4, ["地理"] = 5, ["情報"] = 6,
    };

    public static string For(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return "#8e929e";
        if (Fixed.TryGetValue(subject, out int index)) return Palette[index];

        // 実行のたびに変わる string.GetHashCode は使わず、文字から決まる値で選ぶ
        uint hash = 2166136261;
        foreach (char c in subject) hash = (hash ^ c) * 16777619;
        return Palette[hash % (uint)Palette.Length];
    }

    public static string Label(string? subject) => string.IsNullOrWhiteSpace(subject) ? "科目なし" : subject;
}
