namespace PulseMind.Web.Components.Layout;

/// <summary>メニューの項目。PC の左メニューとスマホの下のタブで同じ並びを使う。</summary>
internal sealed record NavItem(string Href, string Icon, string Label, string ShortLabel, bool InTabBar);

internal static class NavItems
{
    public static readonly IReadOnlyList<NavItem> All =
    [
        new("", "home", "ホーム", "ホーム", true),
        new("study", "study", "学習の記録", "学習", true),
        new("daily", "calendar", "毎日の記録", "記録", true),
        new("insights", "insight", "分析", "分析", true),
        new("devices", "device", "機器・取り込み", "機器", false),
        new("settings", "settings", "設定", "設定", true),
    ];

    public static readonly IReadOnlyList<NavItem> Tabs = All.Where(i => i.InTabBar).ToList();
}
