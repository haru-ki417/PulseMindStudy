// Pulse & Mind Study IoTBridge
// Arduino の脈拍センサーから届く心拍数を読み、まとめて Pulse & Mind Study に送る常駐プログラム。
//
// 使い方:
//   1. Web 画面の「機器とデータの取り込み」で機器を登録し、機器トークンを控える
//   2. 環境変数 PULSEMIND_DEVICE_TOKEN にトークンを設定する
//   3. appsettings.json の ServerUrl と SerialPort を自分の環境に合わせる
//   4. dotnet run（センサーが無いときは --Bridge:Simulate=true で見本の値を送れる）
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PulseMind.IoTBridge;

// テスト側の Web アプリの Program と名前がぶつからないよう、入口のクラス名を分けている
internal static class BridgeEntry
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true); // 個人の設定（リポジトリには含めない）

        builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection("Bridge"));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ReadingBuffer>();
        builder.Services.AddHostedService<SensorReader>();
        builder.Services.AddHttpClient<Uploader>(client => client.Timeout = TimeSpan.FromSeconds(30));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Uploader>());

        await builder.Build().RunAsync();
    }
}
