/*
  Smart Parking Twin - ESP32 + เซนเซอร์อัลตราโซนิก HC-SR04 ส่งสถานะช่องจอดเข้า Unity ผ่าน MQTT

  ไลบรารี (Arduino IDE > Library Manager): PubSubClient (Nick O'Leary)
  บอร์ด: ESP32 Dev Module

  การทำงาน
   - วัดระยะทุก 200 ms ถ้าใกล้กว่า OCCUPIED_CM ติดกัน 1.5 วินาที = มีรถ, ไกลกว่า FREE_CM ติดกัน 1.5 วินาที = ว่าง
   - เมื่อสถานะเปลี่ยน ส่ง "1"/"0" ไปที่  dmt321/smartparking/PARK-01/sensor/<slot>
   - รับ  .../slot/<slot>  กลับมาจาก Unity เพื่อเปิดไฟ LED (แดง = มีรถ, เขียว = ว่าง, กระพริบ = จอดเกินเวลา)

  ต่อสาย (ต่อ 1 ช่อง; เพิ่มช่องได้ที่ตาราง SLOTS)
   HC-SR04  VCC->5V  GND->GND  TRIG->GPIO5  ECHO->GPIO18 (ผ่านตัวแบ่งแรงดัน 5V->3.3V)
   LED แดง -> GPIO25 (ผ่าน R 220Ω), LED เขียว -> GPIO26 (ผ่าน R 220Ω)
*/
#include <WiFi.h>
#include <PubSubClient.h>

// ---------- ตั้งค่า ----------
const char* WIFI_SSID     = "YOUR_WIFI";
const char* WIFI_PASSWORD = "YOUR_PASSWORD";
const char* MQTT_HOST     = "broker.hivemq.com";
const uint16_t MQTT_PORT  = 1883;
const char* BASE_TOPIC    = "dmt321/smartparking/PARK-01";

const float OCCUPIED_CM = 25.0;   // ใกล้กว่านี้ = มีรถ
const float FREE_CM     = 40.0;   // ไกลกว่านี้ = ว่าง (ช่วงกลางกันค่ากระพริบ)
const unsigned long STABLE_MS = 1500;

struct Slot {
  int number;       // หมายเลขช่องใน Unity (1-7)
  int trigPin, echoPin, redPin, greenPin;
  bool occupied;
  bool candidate;
  unsigned long candidateSince;
  bool overstay;    // ได้จาก Unity
  bool twinOccupied;
};

Slot SLOTS[] = {
  { 1, 5, 18, 25, 26, false, false, 0, false, false },
  // { 2, 19, 21, 32, 33, false, false, 0, false, false },
};
const int SLOT_COUNT = sizeof(SLOTS) / sizeof(SLOTS[0]);

WiFiClient net;
PubSubClient mqtt(net);
char topicBuf[96];

// ---------- เซนเซอร์ ----------
float readDistanceCm(const Slot& s) {
  digitalWrite(s.trigPin, LOW);  delayMicroseconds(2);
  digitalWrite(s.trigPin, HIGH); delayMicroseconds(10);
  digitalWrite(s.trigPin, LOW);
  unsigned long us = pulseIn(s.echoPin, HIGH, 30000UL); // timeout ~5 m
  if (us == 0) return 999.0;
  return us * 0.0343f / 2.0f;
}

void publishSlot(const Slot& s) {
  snprintf(topicBuf, sizeof(topicBuf), "%s/sensor/%d", BASE_TOPIC, s.number);
  mqtt.publish(topicBuf, s.occupied ? "1" : "0");
  Serial.printf("[sensor] slot %d = %s\n", s.number, s.occupied ? "occupied" : "free");
}

// ---------- MQTT ----------
void onMessage(char* topic, byte* payload, unsigned int length) {
  String text;
  for (unsigned int i = 0; i < length; i++) text += (char)payload[i];

  // .../slot/<n>  -> {"slot":1,...,"occupied":true,...,"overstay":false,...}
  for (int i = 0; i < SLOT_COUNT; i++) {
    snprintf(topicBuf, sizeof(topicBuf), "%s/slot/%d", BASE_TOPIC, SLOTS[i].number);
    if (strcmp(topic, topicBuf) != 0) continue;
    SLOTS[i].twinOccupied = text.indexOf("\"occupied\":true") >= 0;
    SLOTS[i].overstay = text.indexOf("\"overstay\":true") >= 0;
  }
}

void connectMqtt() {
  while (!mqtt.connected()) {
    String id = "esp32-park-" + String((uint32_t)ESP.getEfuseMac(), HEX);
    Serial.printf("MQTT connecting %s:%d ...\n", MQTT_HOST, MQTT_PORT);
    if (mqtt.connect(id.c_str())) {
      Serial.println("MQTT connected");
      for (int i = 0; i < SLOT_COUNT; i++) {
        snprintf(topicBuf, sizeof(topicBuf), "%s/slot/%d", BASE_TOPIC, SLOTS[i].number);
        mqtt.subscribe(topicBuf);
        publishSlot(SLOTS[i]); // ส่งสถานะปัจจุบันให้ Unity ทันทีหลังต่อได้
      }
    } else {
      Serial.printf("MQTT failed rc=%d, retry in 3 s\n", mqtt.state());
      delay(3000);
    }
  }
}

void setup() {
  Serial.begin(115200);
  for (int i = 0; i < SLOT_COUNT; i++) {
    pinMode(SLOTS[i].trigPin, OUTPUT);
    pinMode(SLOTS[i].echoPin, INPUT);
    pinMode(SLOTS[i].redPin, OUTPUT);
    pinMode(SLOTS[i].greenPin, OUTPUT);
  }

  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  Serial.print("WiFi");
  while (WiFi.status() != WL_CONNECTED) { delay(400); Serial.print("."); }
  Serial.printf("\nWiFi OK  IP %s\n", WiFi.localIP().toString().c_str());

  mqtt.setServer(MQTT_HOST, MQTT_PORT);
  mqtt.setCallback(onMessage);
  mqtt.setBufferSize(1024);
}

void loop() {
  if (!mqtt.connected()) connectMqtt();
  mqtt.loop();

  static unsigned long lastRead = 0;
  unsigned long now = millis();
  if (now - lastRead >= 200) {
    lastRead = now;
    for (int i = 0; i < SLOT_COUNT; i++) {
      Slot& s = SLOTS[i];
      float cm = readDistanceCm(s);
      bool reading = s.occupied;
      if (cm < OCCUPIED_CM) reading = true;
      else if (cm > FREE_CM) reading = false;

      if (reading != s.candidate) { s.candidate = reading; s.candidateSince = now; }
      if (s.candidate != s.occupied && now - s.candidateSince >= STABLE_MS) {
        s.occupied = s.candidate;
        publishSlot(s);
      }
    }
  }

  // ไฟแสดงผลตามสถานะจาก Digital Twin
  bool blinkOn = (now / 500) % 2 == 0;
  for (int i = 0; i < SLOT_COUNT; i++) {
    const Slot& s = SLOTS[i];
    bool red = s.twinOccupied && (!s.overstay || blinkOn);
    digitalWrite(s.redPin, red ? HIGH : LOW);
    digitalWrite(s.greenPin, s.twinOccupied ? LOW : HIGH);
  }
}
