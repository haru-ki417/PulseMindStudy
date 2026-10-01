# 脈拍センサー（Arduino）

光学式の脈拍センサーで測った心拍数を、PC の **IoTBridge** 経由で Pulse & Mind Study に送ります。

```
[脈拍センサー] --A0--> [Arduino] --USB(シリアル "BPM:72")--> [PC: IoTBridge] --HTTPS + 機器トークン--> [Pulse & Mind Study]
```

## 用意するもの

- Arduino Uno / Nano など（USB でシリアル通信できるもの）
- アナログ出力の光学式脈拍センサー（例: Pulse Sensor Amped）
- USB ケーブル

## 配線

| センサー | Arduino |
|---|---|
| `+`（赤） | 5V（3.3V のボードは 3.3V） |
| `-`（黒） | GND |
| `S`（紫） | A0 |

## 書き込み

1. Arduino IDE で `pulse-sensor.ino` を開き、ボードとポートを選んで書き込みます（追加のライブラリは不要です）。
2. シリアルモニタを 115200 bps で開き、指先をセンサーに軽く当てると、数秒後に `BPM:72.4` のような行が出ます。
   - 強く押しすぎると血流が止まって測れません。指先をそっと乗せてください。
   - 拍動に合わせて基板の LED が点滅します。

## IoTBridge で送る

1. Web 画面の **機器・取り込み** で機器を登録し、表示された **機器トークン** を控えます（一度しか表示されません）。
2. `src/PulseMind.IoTBridge/appsettings.json` の `ServerUrl` と `SerialPort` を自分の環境に合わせます
   （個人の設定は同じフォルダの `appsettings.Local.json` に書くと、リポジトリに含まれません）。
3. トークンを環境変数で渡して起動します。

```powershell
# Windows（PowerShell）
$env:PULSEMIND_DEVICE_TOKEN = "pmd_..."
dotnet run --project src/PulseMind.IoTBridge
```

センサーが手元に無いときは、見本の心拍を送って動作を確かめられます。

```powershell
dotnet run --project src/PulseMind.IoTBridge -- --Bridge:Simulate=true
```

## IoTBridge の動き

- センサーの値は PC の時計で時刻を付け、10 秒ごとにまとめて送ります（1回 最大 1000 件）。
- 通信できないときは捨てずに保持し、待ち時間を 10 秒 → 20 秒 → … → 最大 5 分と延ばしながら送り直します。
  保持できるのは 20,000 件（約 5 時間分）で、それを超えると古いものから捨てます。
- USB を抜き差ししても、5 秒おきに自動でつなぎ直します。
- トークンが無効にされた（Web 画面で「無効にする」を押した）ときは、送るのをやめて終了します。
- 手元の PC（localhost）以外へは `https://` でしか送りません（トークンが盗み見られないように）。

> このセンサーと IoTBridge は、勉強中の体調の目安を知るためのものです。医療機器ではなく、病気の診断には使えません。
