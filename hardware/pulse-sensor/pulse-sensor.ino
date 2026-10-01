// Pulse & Mind Study — 脈拍センサー用スケッチ
//
// 光学式の脈拍センサー（PulseSensor.com の「Pulse Sensor Amped」など、アナログ出力のもの）を A0 につなぎ、
// 拍動を見つけるたびに「BPM:72」の形でシリアルに出力する。PC 側では IoTBridge がこれを読んでサーバーへ送る。
//
// 配線（Arduino Uno / Nano の例）
//   センサー +  → 5V（3.3V のボードは 3.3V）
//   センサー -  → GND
//   センサー S  → A0
//
// 拍動の見つけ方
//   1. 2ms ごとに値を読む（毎秒 500 回）
//   2. 最近の山（最大）と谷（最小）を覚えておき、その中間を「しきい値」にする（指の当て方で明るさが変わっても追従する）
//   3. 値がしきい値を下から上へ越えたときを「1拍」とする。ただし前の拍から 300ms 以内は無視（1分間に200回を超える速さは扱わない）
//   4. 直近 5 拍の間隔の平均から、1分あたりの拍数（BPM）を計算する
//   指が離れて揺れ幅が小さいときは何も出力しない。
//
// このスケッチは健康管理の目安のためのもので、医療機器ではありません。

const int SENSOR_PIN = A0;
const int LED_PIN = LED_BUILTIN;
const unsigned long SAMPLE_INTERVAL_MS = 2;
const unsigned long REFRACTORY_MS = 300;    // 前の拍からこの時間は次の拍とみなさない（= 最大 200 BPM）
const unsigned long MAX_INTERVAL_MS = 1500; // これより間が空いたら測り直す（= 最小 40 BPM）
const int MIN_AMPLITUDE = 40;               // 山と谷の差がこれより小さいときは「指が当たっていない」とみなす
const int AVERAGE_BEATS = 5;

unsigned long lastSampleAt = 0;
unsigned long lastBeatAt = 0;
int peak = 512;
int trough = 512;
bool above = false;

unsigned long intervals[AVERAGE_BEATS];
int intervalCount = 0;
int intervalIndex = 0;

void setup() {
  pinMode(LED_PIN, OUTPUT);
  Serial.begin(115200);
}

void loop() {
  unsigned long now = millis();
  if (now - lastSampleAt < SAMPLE_INTERVAL_MS) return;
  lastSampleAt = now;

  int value = analogRead(SENSOR_PIN);

  // 山と谷を少しずつ中央へ戻しながら、新しい値で更新する（古い明るさを引きずらないように）
  peak = max(value, peak - 1);
  trough = min(value, trough + 1);
  int amplitude = peak - trough;
  int threshold = trough + amplitude / 2;

  if (!above && value > threshold && amplitude >= MIN_AMPLITUDE && now - lastBeatAt > REFRACTORY_MS) {
    above = true;
    digitalWrite(LED_PIN, HIGH);
    onBeat(now);
  } else if (above && value < threshold) {
    above = false;
    digitalWrite(LED_PIN, LOW);
  }

  // 長い間拍動が無ければ、平均をやり直す
  if (now - lastBeatAt > MAX_INTERVAL_MS * 2) {
    intervalCount = 0;
  }
}

void onBeat(unsigned long now) {
  unsigned long interval = now - lastBeatAt;
  lastBeatAt = now;
  if (interval > MAX_INTERVAL_MS) return; // 最初の1拍、または間が空きすぎた

  intervals[intervalIndex] = interval;
  intervalIndex = (intervalIndex + 1) % AVERAGE_BEATS;
  if (intervalCount < AVERAGE_BEATS) intervalCount++;
  if (intervalCount < 3) return; // 3拍そろうまでは出力しない

  unsigned long sum = 0;
  for (int i = 0; i < intervalCount; i++) sum += intervals[i];
  float bpm = 60000.0 * intervalCount / sum;

  if (bpm >= 40 && bpm <= 200) {
    Serial.print("BPM:");
    Serial.println(bpm, 1);
  }
}
