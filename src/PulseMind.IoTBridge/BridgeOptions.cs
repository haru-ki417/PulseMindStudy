namespace PulseMind.IoTBridge;

/// <summary>IoTBridge の設定（appsettings.json の "Bridge" 、または環境変数 Bridge__ServerUrl など）</summary>
public sealed class BridgeOptions
{
    /// <summary>Pulse &amp; Mind Study のアドレス（例: https://pulsemind.example.com）</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>
    /// 機器トークン。設定ファイルに書くと誤って共有しやすいので、環境変数 PULSEMIND_DEVICE_TOKEN で渡すことを勧める。
    /// </summary>
    public string? DeviceToken { get; set; }

    /// <summary>Arduino をつないだシリアルポート（Windows: COM3 など / Mac: /dev/tty.usbmodem... / Linux: /dev/ttyACM0）</summary>
    public string SerialPort { get; set; } = "COM3";

    public int BaudRate { get; set; } = 115200;

    /// <summary>まとめて送る間隔（秒）</summary>
    public int UploadIntervalSeconds { get; set; } = 10;

    /// <summary>true にすると、センサーが無くても見本の心拍を作って送る（動作確認用）</summary>
    public bool Simulate { get; set; }
}
