namespace PulseMind.Core.Domain;

/// <summary>データの入手元。同じ時間帯の心拍でも、入手元ごとに分けて保存する。</summary>
public enum DataSource
{
    /// <summary>画面からの手入力・タイマー</summary>
    Manual = 0,

    /// <summary>Arduino の脈拍センサー（IoTBridge 経由）</summary>
    Arduino = 1,

    /// <summary>iPhone の「ヘルスケア」から書き出したデータ（Apple Watch の記録を含む）</summary>
    AppleHealth = 2,
}
