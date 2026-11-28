/*
  ====================================================================================
  Smart Parking IoT - NodeMCU ESP8266 "บอร์ดช่องจอด" (บอร์ด B และ บอร์ด C)
  HC-SR04 x3 + LED 2 สี x3 คู่ + Wi-Fi/MQTT
  เชื่อมกับฉาก SmartParkNew.unity ผ่าน MQTT (คอมโพเนนต์ MqttParkingBridge)
  ====================================================================================

  ระบบทั้งหมดใช้ ESP8266 3 บอร์ด (ทุกบอร์ดต่อ Wi-Fi ส่งข้อมูลเข้า Unity เอง ไม่ต้องเดินสายถึงกัน)
    บอร์ด A : SmartParking_ESP8266.ino        LCD + RFID + Servo + ช่องจอด 1 + เซนเซอร์ทางออก
    บอร์ด B : ไฟล์นี้ ตั้ง SLOT_GROUP 1        ช่องจอด 2, 3, 4 (ช่องปกติ LED เขียว/แดง)
    บอร์ด C : ไฟล์นี้ ตั้ง SLOT_GROUP 2        ช่องจอด 5, 6, 7 (VIP / Accessibility / Maternity LED น้ำเงิน/แดง)

  ตั้งค่า Arduino IDE
    - Board: "NodeMCU 1.0 (ESP-12E Module)" จาก "esp8266 by ESP8266 Community"
    - ไลบรารี: PubSubClient by Nick O'Leary

  ตารางขา (ช่อง ก/ข/ค = ช่องที่ 1/2/3 ของบอร์ดนี้)
    D0 (GPIO16) -> Trig ของ HC-SR04 ทั้ง 3 ตัว (ใช้ร่วมกัน)
    D5 (GPIO14) -> Echo ช่อง ก      D1 (GPIO5)  -> LED ช่อง ก
    D6 (GPIO12) -> Echo ช่อง ข      D2 (GPIO4)  -> LED ช่อง ข
    D8 (GPIO15) -> Echo ช่อง ค      D7 (GPIO13) -> LED ช่อง ค
    LED แต่ละช่อง (ไม่ใช้ตัวต้านทาน ขาเดียวคุม 2 ดวง):
      เขียว/น้ำเงิน : ขายาว (+) ที่ 3.3V, ขาสั้น (-) ที่ขาของช่อง  -> ติดเมื่อช่องว่าง (LOW)
      แดง          : ขายาว (+) ที่ขาของช่อง, ขาสั้น (-) ที่ GND   -> ติดเมื่อมีรถ (HIGH)
    HC-SR04 : VCC -> VIN (5V จาก USB), GND -> GND
    ไม่ได้ใช้ขา RX/TX จึงไม่ต้องถอดสายตอนอัปโหลด และดู Serial Monitor (115200) ได้ตลอด
    ไฟ LED บนตัวบอร์ด (ขา D4) ติดค้าง = ต่อ MQTT สำเร็จ, กระพริบ = กำลังเชื่อมต่อ

  การทำงาน
    - รถเข้า/ออกช่อง (< 8 cm)  -> เปลี่ยนสีไฟ + ส่ง .../sensor/{เลขช่อง} = 1/0 (Unity เสกรถ/เอารถออก)
    - รถในเกมจอดช่อง           -> ไฟแดงติดด้วย (อ่านจาก .../slot/{เลขช่อง} ที่ Unity ส่งมา)
    - จอดเกิน 2 ชั่วโมง          -> ไฟแดงกระพริบ (หรือเมื่อ Unity แจ้ง overstay ซึ่งเร่งเวลาได้ด้วยปุ่ม TIME SPEED)
*/

#include <ESP8266WiFi.h>
#include <PubSubClient.h>
#include <string.h>

// ====================================================================================
// 1. ตั้งค่า Wi-Fi & MQTT Broker (ต้องเหมือนบอร์ด A)
// ====================================================================================
const char* WIFI_SSID     = "iQOO Z10 Turbo Pro";   // ESP8266 ต่อได้เฉพาะ Wi-Fi 2.4 GHz (คลื่น 5 GHz ใช้ไม่ได้)
const char* WIFI_PASSWORD = "jjlove444";
const char* MQTT_BROKER   = "broker.hivemq.com";
const int   MQTT_PORT     = 1883;

const char* TOPIC_ROOT    = "dmt321/smartparking";
const char* DEVICE_ID     = "PARK-01";      // ต้องตรงกับ deviceId ใน Unity (MqttParkingBridge)

// ====================================================================================
// 2. บอร์ดนี้ดูแลช่องไหน
// ====================================================================================
// 1 = บอร์ด B : ช่อง 2, 3, 4  (ช่องปกติ)
// 2 = บอร์ด C : ช่อง 5, 6, 7  (V.I.P, Accessibility, Maternity)
#define SLOT_GROUP 1

#if SLOT_GROUP == 1
const int   UNITY_SLOTS[3] = { 2, 3, 4 };
const char* BOARD_NAME     = "slots-2-4";
#elif SLOT_GROUP == 2
const int   UNITY_SLOTS[3] = { 5, 6, 7 };
const char* BOARD_NAME     = "slots-5-7";
#else
#error "SLOT_GROUP ต้องเป็น 1 หรือ 2"
#endif

const int           THRESHOLD_CM = 8;                            // มีรถจอด ถ้าระยะน้อยกว่านี้ (โมเดลขนาดเล็ก)
const unsigned long OVERSTAY_MS  = 2UL * 60UL * 60UL * 1000UL;   // 2 ชั่วโมง

// ====================================================================================
// 3. ขา Pin (เลข GPIO)
// ====================================================================================
const uint8_t PIN_TRIG        = 16;              // D0
const uint8_t PIN_ECHO[3]     = { 14, 12, 15 };  // D5, D6, D8
const uint8_t PIN_LED[3]      = { 5, 4, 13 };    // D1, D2, D7
const uint8_t PIN_STATUS_LED  = 2;               // D4 = LED บนตัวบอร์ด (ติดเมื่อเป็น LOW)

// ====================================================================================
// 4. สถานะ
// ====================================================================================
WiFiClient   wifiClient;
PubSubClient mqtt(wifiClient);

enum LedState { LED_UNSET, LED_FREE, LED_RED, LED_OFF };

struct Slot {
    bool          occupied;      // สถานะที่ยืนยันแล้ว
    uint8_t       agree;         // นับค่าที่อ่านได้ต่างจากเดิมติดกัน
    unsigned long parkedAt;
    bool          owedFree;      // รถจริงออกตอนเน็ตหลุด ยังไม่ได้บอก Unity
    bool          twinOccupied;  // ในเกมมีรถจอดช่องนี้ (ค่าจาก Unity)
    bool          twinOverstay;  // ค่าจาก Unity
    LedState      ledShown;
    char          sensorTopic[96];
    char          slotTopic[96];
};

Slot slots[3];

char baseTopic[64];
char topicTwinOnline[96];
char topicBoardOnline[96];

const uint16_t MQTT_BUFFER_SIZE = 1024;
char msgBuf[MQTT_BUFFER_SIZE];

bool          twinOnline    = false;
bool          mqttTried     = false;
unsigned long lastMqttTry   = 0;
unsigned long lastSensorRun = 0;
uint8_t       sensorTurn    = 0;

// ====================================================================================
// 5. LED ช่องจอด (1 ขาคุม 2 ดวง: LOW = เขียว/น้ำเงิน, HIGH = แดง, INPUT = ดับ)
// ====================================================================================
void setLed(uint8_t i, LedState s) {
    if (slots[i].ledShown == s) return;
    slots[i].ledShown = s;
    uint8_t pin = PIN_LED[i];
    switch (s) {
        case LED_FREE:
            pinMode(pin, OUTPUT);
            digitalWrite(pin, LOW);
            break;
        case LED_RED:
            pinMode(pin, OUTPUT);
            digitalWrite(pin, HIGH);
            break;
        default:
            pinMode(pin, INPUT);   // Hi-Z: ดับทั้งสองดวง (ใช้ตอนกระพริบ)
            break;
    }
}

// ช่องมีรถ ถ้าเซนเซอร์จริงเจอรถ หรือในเกมมีรถจอดอยู่
bool showOccupied(const Slot& s) {
    return s.occupied || (twinOnline && s.twinOccupied);
}

bool isOverstay(const Slot& s) {
    if (s.occupied && millis() - s.parkedAt >= OVERSTAY_MS) return true;
    return showOccupied(s) && twinOnline && s.twinOverstay;
}

void updateLeds() {
    bool blinkOn = (millis() / 500) % 2 == 0;
    for (uint8_t i = 0; i < 3; i++) {
        if (!showOccupied(slots[i])) setLed(i, LED_FREE);
        else if (isOverstay(slots[i])) setLed(i, blinkOn ? LED_RED : LED_OFF);
        else setLed(i, LED_RED);
    }
}

// ====================================================================================
// 6. HC-SR04 (Trig ร่วมขา D0 อ่านทีละตัว)
// ====================================================================================
long readDistanceCm(uint8_t echoPin) {
    digitalWrite(PIN_TRIG, LOW);
    delayMicroseconds(2);
    digitalWrite(PIN_TRIG, HIGH);
    delayMicroseconds(10);
    digitalWrite(PIN_TRIG, LOW);

    unsigned long us = pulseIn(echoPin, HIGH, 25000UL);   // timeout 25 ms (~4 เมตร)
    if (us == 0) return 999;
    return (long)(us / 58UL);                            // แปลงเป็น cm
}

bool sendSlot(uint8_t i, bool occupied) {
    if (!mqtt.connected()) return false;
    Serial.printf("[SLOT %d] -> %s = %s\n", UNITY_SLOTS[i], slots[i].sensorTopic, occupied ? "1" : "0");
    return mqtt.publish(slots[i].sensorTopic, occupied ? "1" : "0");
}

// เรียกตอนเซนเซอร์จริงเปลี่ยนสถานะ
void reportSlotChange(uint8_t i) {
    Slot& s = slots[i];
    if (s.occupied) {
        s.owedFree = false;
        sendSlot(i, true);
    } else {
        s.owedFree = !sendSlot(i, false);   // ส่งไม่ได้ (เน็ตหลุด) ค่อยส่งตอนต่อกลับ
    }
}

// เรียกตอนเพิ่งต่อ MQTT หรือ Unity เพิ่งออนไลน์
// ส่ง "0" เฉพาะช่องที่รถจริงออกไประหว่างเน็ตหลุด เพราะ Unity จะลบรถในช่องเมื่อได้ "0"
// (ถ้าส่ง "0" ทุกครั้งที่ต่อเน็ต รถที่จอดอยู่ในเกมจะหายไป)
void syncAllSlots() {
    for (uint8_t i = 0; i < 3; i++) {
        Slot& s = slots[i];
        if (s.occupied) sendSlot(i, true);
        else if (s.owedFree && sendSlot(i, false)) s.owedFree = false;
    }
}

void updateSensor(uint8_t i) {
    long cm = readDistanceCm(PIN_ECHO[i]);
    bool reading = cm < THRESHOLD_CM;
    Slot& s = slots[i];

    if (reading == s.occupied) {
        s.agree = 0;
        return;
    }
    if (++s.agree < 3) return;   // ค่าใหม่ต้องเหมือนกัน 3 ครั้งติด (~0.9 วินาที) กันสัญญาณรบกวน

    s.agree        = 0;
    s.occupied     = reading;
    s.parkedAt     = millis();
    s.twinOverstay = false;
    Serial.printf("[SLOT %d] %s (%ld cm)\n", UNITY_SLOTS[i], reading ? "รถเข้าจอด" : "รถออก", cm);
    reportSlotChange(i);
}

// ====================================================================================
// 7. MQTT
// ====================================================================================
// อ่านค่า true/false จาก JSON ของ Unity เช่น "occupied":true
bool jsonBool(const char* json, const char* key, bool& out) {
    char pattern[32];
    snprintf(pattern, sizeof(pattern), "\"%s\":", key);
    const char* p = strstr(json, pattern);
    if (p == nullptr) return false;
    out = strncmp(p + strlen(pattern), "true", 4) == 0;
    return true;
}

void onMqttMessage(char* topic, byte* payload, unsigned int length) {
    if (length >= sizeof(msgBuf)) length = sizeof(msgBuf) - 1;
    memcpy(msgBuf, payload, length);
    msgBuf[length] = '\0';

    // Unity ออนไลน์/ออฟไลน์ (Last Will ของ Unity)
    if (strcmp(topic, topicTwinOnline) == 0) {
        bool wasOnline = twinOnline;
        twinOnline = strcmp(msgBuf, "online") == 0;
        if (twinOnline && !wasOnline) syncAllSlots();   // Unity เพิ่งเปิด ส่งรถจริงที่จอดอยู่ให้ทันที
        if (!twinOnline) {
            for (uint8_t i = 0; i < 3; i++) slots[i].twinOccupied = slots[i].twinOverstay = false;
        }
        return;
    }

    // สถานะช่องจาก Unity -> มีรถในเกมไหม + overstay
    for (uint8_t i = 0; i < 3; i++) {
        if (strcmp(topic, slots[i].slotTopic) == 0) {
            jsonBool(msgBuf, "occupied", slots[i].twinOccupied);
            jsonBool(msgBuf, "overstay", slots[i].twinOverstay);
            Serial.printf("[TWIN %d] occupied=%d overstay=%d\n", UNITY_SLOTS[i], slots[i].twinOccupied, slots[i].twinOverstay);
            return;
        }
    }
}

void connectMqtt() {
    if (WiFi.status() != WL_CONNECTED || mqtt.connected()) return;
    if (mqttTried && millis() - lastMqttTry < 5000) return;   // ลองใหม่ทุก 5 วินาที
    mqttTried   = true;
    lastMqttTry = millis();
    twinOnline  = false;
    for (uint8_t i = 0; i < 3; i++) slots[i].twinOccupied = false;

    char clientId[48];
    snprintf(clientId, sizeof(clientId), "esp8266-%s-%06X", BOARD_NAME, (unsigned int)ESP.getChipId());
    Serial.printf("[MQTT] connecting %s:%d as %s ...\n", MQTT_BROKER, MQTT_PORT, clientId);

    if (mqtt.connect(clientId, topicBoardOnline, 0, true, "offline")) {
        mqtt.publish(topicBoardOnline, "online", true);
        mqtt.subscribe(topicTwinOnline);
        for (uint8_t i = 0; i < 3; i++) mqtt.subscribe(slots[i].slotTopic);
        syncAllSlots();
        Serial.println("[MQTT] connected");
    } else {
        Serial.printf("[MQTT] failed, state = %d\n", mqtt.state());
    }
}

void updateStatusLed() {
    bool on = mqtt.connected() || (millis() / 250) % 2 == 0;   // ติดค้าง = ออนไลน์, กระพริบ = กำลังต่อ
    digitalWrite(PIN_STATUS_LED, on ? LOW : HIGH);
}

// ====================================================================================
// 8. Setup & Loop
// ====================================================================================
void setup() {
    Serial.begin(115200);
    delay(200);
    Serial.printf("\n[Smart Parking ESP8266] บอร์ดช่องจอด %s (ช่อง %d, %d, %d)\n",
                  BOARD_NAME, UNITY_SLOTS[0], UNITY_SLOTS[1], UNITY_SLOTS[2]);

    snprintf(baseTopic, sizeof(baseTopic), "%s/%s", TOPIC_ROOT, DEVICE_ID);
    snprintf(topicTwinOnline, sizeof(topicTwinOnline), "%s/online", baseTopic);
    snprintf(topicBoardOnline, sizeof(topicBoardOnline), "%s/esp/%s/online", baseTopic, BOARD_NAME);

    pinMode(PIN_TRIG, OUTPUT);
    digitalWrite(PIN_TRIG, LOW);
    pinMode(PIN_STATUS_LED, OUTPUT);
    digitalWrite(PIN_STATUS_LED, HIGH);

    for (uint8_t i = 0; i < 3; i++) {
        slots[i] = Slot();
        slots[i].ledShown = LED_UNSET;
        snprintf(slots[i].sensorTopic, sizeof(slots[i].sensorTopic), "%s/sensor/%d", baseTopic, UNITY_SLOTS[i]);
        snprintf(slots[i].slotTopic, sizeof(slots[i].slotTopic), "%s/slot/%d", baseTopic, UNITY_SLOTS[i]);
        pinMode(PIN_ECHO[i], INPUT);
        setLed(i, LED_FREE);
    }

    WiFi.persistent(false);
    WiFi.mode(WIFI_STA);
    WiFi.setAutoReconnect(true);
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
    Serial.printf("[WiFi] connecting %s ...\n", WIFI_SSID);

    mqtt.setServer(MQTT_BROKER, MQTT_PORT);
    mqtt.setBufferSize(MQTT_BUFFER_SIZE);
    mqtt.setKeepAlive(30);
    mqtt.setSocketTimeout(5);
    mqtt.setCallback(onMqttMessage);
}

void loop() {
    if (WiFi.status() == WL_CONNECTED) {
        if (!mqtt.connected()) connectMqtt();
        else mqtt.loop();
    }

    // อ่าน HC-SR04 ทีละตัวทุก 100 ms (แต่ละตัวถูกอ่านทุก 300 ms)
    if (millis() - lastSensorRun >= 100) {
        lastSensorRun = millis();
        updateSensor(sensorTurn);
        sensorTurn = (sensorTurn + 1) % 3;
    }

    updateLeds();
    updateStatusLed();
}
