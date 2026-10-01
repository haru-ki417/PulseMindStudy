namespace PulseMind.Web.Components.Ui;

/// <summary>棒グラフの1本</summary>
public sealed record BarItem(string Label, double Value, string Tooltip, bool Highlight = false, string? SubLabel = null);

/// <summary>心拍グラフの1点（利用者のタイムゾーンでの時刻）</summary>
public sealed record HeartRatePoint(DateTime Local, double Average, double Min, double Max);
