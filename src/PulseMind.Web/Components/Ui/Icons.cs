namespace PulseMind.Web.Components.Ui;

/// <summary>
/// アプリで使うアイコン（24×24 の線画）。外部のアイコン集に頼らず、必要なものだけを自作している。
/// </summary>
internal static class Icons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["home"] = """<path d="M3.5 10.5 12 3.5l8.5 7"/><path d="M5.5 9v11h5v-6h3v6h5V9"/>""",
        ["study"] = """<path d="M4 5a2 2 0 0 1 2-2h14v15H6a2 2 0 0 0-2 2z"/><path d="M4 20a2 2 0 0 0 2 2h14v-4"/><path d="M8.5 7.5h7"/>""",
        ["timer"] = """<circle cx="12" cy="13.5" r="7.5"/><path d="M12 10v3.5l2.5 2"/><path d="M9.5 2.5h5"/><path d="M12 2.5V6"/>""",
        ["calendar"] = """<rect x="3.5" y="5" width="17" height="15.5" rx="2.5"/><path d="M3.5 10h17"/><path d="M8 3v4M16 3v4"/>""",
        ["chart"] = """<path d="M4 4v16h16"/><path d="M8.5 16v-4M12.5 16V8M16.5 16v-6"/>""",
        ["insight"] = """<path d="M12 3.5l1.7 4.8 4.8 1.7-4.8 1.7L12 16.5l-1.7-4.8L5.5 10l4.8-1.7z"/><path d="M18.5 15.5l.7 1.8 1.8.7-1.8.7-.7 1.8-.7-1.8-1.8-.7 1.8-.7z"/>""",
        ["device"] = """<rect x="6" y="6" width="12" height="12" rx="2"/><path d="M10 10h4v4h-4z"/><path d="M9.5 2.5V6M14.5 2.5V6M9.5 18v3.5M14.5 18v3.5M2.5 9.5H6M2.5 14.5H6M18 9.5h3.5M18 14.5h3.5"/>""",
        ["settings"] = """<path d="M4 6.5h9M17 6.5h3M4 12h3M11 12h9M4 17.5h11M19 17.5h1"/><circle cx="15" cy="6.5" r="2"/><circle cx="9" cy="12" r="2"/><circle cx="17" cy="17.5" r="2"/>""",
        ["logout"] = """<path d="M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4"/><path d="M10 16.5 5.5 12 10 7.5"/><path d="M5.5 12H16"/>""",
        ["user"] = """<circle cx="12" cy="8" r="4"/><path d="M4.5 20.5c1.3-3.5 4.2-5.5 7.5-5.5s6.2 2 7.5 5.5"/>""",
        ["heart"] = """<path d="M12 20s-7.4-4.5-9.1-9.3C1.7 7.3 3.9 4 7.2 4c2 0 3.6 1.1 4.8 2.9C13.2 5.1 14.8 4 16.8 4c3.3 0 5.5 3.3 4.3 6.7C19.4 15.5 12 20 12 20z"/>""",
        ["pulse"] = """<path d="M2.5 12.5h4.2l2.1-5 4.2 10 2.4-6.2 1.4 1.2h4.7"/>""",
        ["moon"] = """<path d="M19.5 14.6A7.8 7.8 0 1 1 9.4 4.5a6.2 6.2 0 0 0 10.1 10.1z"/>""",
        ["steps"] = """<path d="M8.2 3.5c1.6 0 2.4 2 2.4 4.4s-1 3.9-2.4 3.9-2.4-1.4-2.4-3.9.8-4.4 2.4-4.4z"/><path d="M6.2 14.8h4v1.4a2 2 0 0 1-4 0z"/><path d="M15.8 7.5c1.6 0 2.4 2 2.4 4.4s-1 3.9-2.4 3.9-2.4-1.4-2.4-3.9.8-4.4 2.4-4.4z"/><path d="M13.8 18.8h4v.2a2 2 0 0 1-4 0z"/>""",
        ["play"] = """<path d="M7.5 4.8v14.4L19 12z" fill="currentColor"/>""",
        ["stop"] = """<rect x="6.5" y="6.5" width="11" height="11" rx="2" fill="currentColor"/>""",
        ["plus"] = """<path d="M12 5v14M5 12h14"/>""",
        ["trash"] = """<path d="M4 7h16"/><path d="M9.5 7V4.5h5V7"/><path d="M6.5 7l.9 13h9.2l.9-13"/>""",
        ["edit"] = """<path d="M4 20h4L19 9l-4-4L4 16z"/><path d="M13.5 6.5l4 4"/>""",
        ["x"] = """<path d="M6 6l12 12M18 6 6 18"/>""",
        ["check"] = """<path d="M5 12.5l4.5 4.5L19 7.5"/>""",
        ["info"] = """<circle cx="12" cy="12" r="9"/><path d="M12 11v5.5"/><path d="M12 7.5h.01"/>""",
        ["alert"] = """<path d="M12 3.8 2.8 19.8h18.4z"/><path d="M12 10v4.5"/><path d="M12 17.3h.01"/>""",
        ["shield"] = """<path d="M12 3l7.5 3v6c0 4.4-3.2 7.8-7.5 9-4.3-1.2-7.5-4.6-7.5-9V6z"/><path d="M8.8 12.2l2.3 2.3 4.3-4.6"/>""",
        ["lock"] = """<rect x="4.5" y="10.5" width="15" height="10" rx="2.5"/><path d="M8 10.5V7.5a4 4 0 0 1 8 0v3"/>""",
        ["download"] = """<path d="M12 4v11"/><path d="M7 10.5l5 5 5-5"/><path d="M4.5 19.5h15"/>""",
        ["upload"] = """<path d="M12 15.5v-11"/><path d="M7 9l5-5 5 5"/><path d="M4.5 19.5h15"/>""",
        ["copy"] = """<rect x="8.5" y="8.5" width="11.5" height="11.5" rx="2"/><path d="M15.5 8.5V5.5a1.5 1.5 0 0 0-1.5-1.5H5.5A1.5 1.5 0 0 0 4 5.5V14a1.5 1.5 0 0 0 1.5 1.5h3"/>""",
        ["chevron-left"] = """<path d="M15 5.5 8.5 12l6.5 6.5"/>""",
        ["chevron-right"] = """<path d="M9 5.5l6.5 6.5L9 18.5"/>""",
        ["flame"] = """<path d="M12 21c-3.9 0-6.5-2.6-6.5-6.2 0-3.6 2.7-5.6 4.1-8.8.3 2.2 1.4 3.4 2.6 4 .1-3 1.3-5.5 3.3-7 0 3.6 3 5.7 3 10.6 0 4.4-2.7 7.4-6.5 7.4z"/>""",
        ["target"] = """<circle cx="12" cy="12" r="8.5"/><circle cx="12" cy="12" r="4.5"/><circle cx="12" cy="12" r=".8" fill="currentColor"/>""",
        ["watch"] = """<rect x="6" y="6" width="12" height="12" rx="3.5"/><path d="M8.5 6l.8-3h5.4l.8 3M8.5 18l.8 3h5.4l.8-3"/><path d="M12 9.5V12l1.5 1.5"/>""",
        ["external"] = """<path d="M14 4.5h5.5V10"/><path d="M19.5 4.5 11 13"/><path d="M18 14v4.5a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 4 18.5v-11A1.5 1.5 0 0 1 5.5 6H10"/>""",

        // 気分（1: とても悪い 〜 5: とても良い）
        ["mood-1"] = """<circle cx="12" cy="12" r="9"/><circle cx="9" cy="10" r=".9" fill="currentColor"/><circle cx="15" cy="10" r=".9" fill="currentColor"/><path d="M8 16.8c1.2-1.7 2.5-2.4 4-2.4s2.8.7 4 2.4"/>""",
        ["mood-2"] = """<circle cx="12" cy="12" r="9"/><circle cx="9" cy="10" r=".9" fill="currentColor"/><circle cx="15" cy="10" r=".9" fill="currentColor"/><path d="M8.6 16.1c1-.8 2.1-1.1 3.4-1.1s2.4.3 3.4 1.1"/>""",
        ["mood-3"] = """<circle cx="12" cy="12" r="9"/><circle cx="9" cy="10" r=".9" fill="currentColor"/><circle cx="15" cy="10" r=".9" fill="currentColor"/><path d="M8.8 15.3h6.4"/>""",
        ["mood-4"] = """<circle cx="12" cy="12" r="9"/><circle cx="9" cy="10" r=".9" fill="currentColor"/><circle cx="15" cy="10" r=".9" fill="currentColor"/><path d="M8.6 14.3c1 .9 2.1 1.3 3.4 1.3s2.4-.4 3.4-1.3"/>""",
        ["mood-5"] = """<circle cx="12" cy="12" r="9"/><circle cx="9" cy="10" r=".9" fill="currentColor"/><circle cx="15" cy="10" r=".9" fill="currentColor"/><path d="M7.8 13.6c1.2 2 2.6 2.9 4.2 2.9s3-.9 4.2-2.9"/>""",
    };

    public static string Get(string name) => Paths.TryGetValue(name, out var path) ? path : Paths["info"];
}
