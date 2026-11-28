/*
  Smart Parking IoT - บอร์ด B (ช่องจอด 2, 3, 4)
  แยกขา Trig: D0 (ช่อง 2), D3 (ช่อง 3), D4 (ช่อง 4)
*/

#include <ESP8266WiFi.h>
#include <PubSubClient.h>
#include <string.h>

// ====================================================================================
// 1. ตั้งค่า Wi-Fi & MQTT Broker (ต้องตรงกับบอร์ด A และ Unity)
// ====================================================================================
const char* WIFI_SSID     = "iQOO Z10 Turbo Pro";   // ถ้าเปลี่ยนเน็ตเป็น youcraf ให้แก้ตรงนี้ให้ตรงกัน
const char* WIFI_PASSWORD = "jjlove444";
const char* MQTT_BROKER   = "broker.hivemq.com";
const int   MQTT_PORT     = 1883;

const char* TOPIC_ROOT    = "dmt321/smartparking";
const char* DEVICE_ID     = "PARK-01";

// ====================================================================================
// 2. ตั้งค่าช่องจอด (1 = บอร์ด B ช่อง 2,3,4 | 2 = บอร์ด C ช่อง 5,6,7)
// ====================================================================================
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

const int           THRESHOLD_CM = 8;                            // ลดเหลือ 5 หรือ 6 ถ้าเซนเซอร์จับโดนพื้นโมเดล
const unsigned long OVERSTAY_MS  = 2UL * 60UL * 60UL * 1000UL;

// ====================================================================================
// 3. ขา Pin (แยก Trig: D0, D3, D4 ไม่ใช้ RX/TX)
// ====================================================================================
const uint8_t PIN_TRIG[3] = { 16, 0, 2 };    // D0, D3, D4
const uint8_t PIN_ECHO[3] = { 14, 12, 15 };  // D5, D6, D8
const uint8_t PIN_LED[3]  = { 5, 4, 13 };    // D1, D2, D7

// ====================================================================================
// 4. สถานะ
// ====================================================================================
WiFiClient   wifiClient;
PubSubClient mqtt(wifiClient);

enum LedState { LED_UNSET, LED_FREE, LED_RED, LED_OFF };

struct Slot {
    bool          occupied;
    uint8_t       agree;
    unsigned long parkedAt;
    bool          owedFree;
    bool          twinOccupied;
    bool          twinOverstay;
    LedState      ledShown;
    long          lastCm;
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
unsigned long lastDebugPrint = 0;
uint8_t       sensorTurn    = 0;

// ====================================================================================
// 5. LED ช่องจอด
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
            pinMode(pin, INPUT);
            break;
    }
}

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
// 6. HC-SR04 (แยก Trig + ตัดค่าหลอน 0-1 cm และ > 400 cm)
// ====================================================================================
long readDistanceCm(uint8_t idx) {
    uint8_t trigPin = PIN_TRIG[idx];
    uint8_t echoPin = PIN_ECHO[idx];

    digitalWrite(trigPin, LOW);
    delayMicroseconds(4);
    digitalWrite(trigPin, HIGH);
    delayMicroseconds(10);
    digitalWrite(trigPin, LOW);

    unsigned long us = pulseIn(echoPin, HIGH, 25000UL);
    long cm = (long)(us / 58UL);
    if (us == 0 || cm < 2 || cm > 400) return 999;
    return cm;
}

bool sendSlot(uint8_t i, bool occupied) {
    if (!mqtt.connected()) return false;
    Serial.printf("[SLOT %d] -> %s = %s\n", UNITY_SLOTS[i], slots[i].sensorTopic, occupied ? "1" : "0");
    return mqtt.publish(slots[i].sensorTopic, occupied ? "1" : "0");
}

void reportSlotChange(uint8_t i) {
    Slot& s = slots[i];
    if (s.occupied) {
        s.owedFree = false;
        sendSlot(i, true);
    } else {
        s.owedFree = !sendSlot(i, false);
    }
}

void syncAllSlots() {
    for (uint8_t i = 0; i < 3; i++) {
        Slot& s = slots[i];
        if (s.occupied) sendSlot(i, true);
        else if (s.owedFree && sendSlot(i, false)) s.owedFree = false;
    }
}

void updateSensor(uint8_t i) {
    long cm = readDistanceCm(i);
    Slot& s = slots[i];
    s.lastCm = cm;
    bool reading = cm < THRESHOLD_CM;

    if (reading == s.occupied) {
        s.agree = 0;
        return;
    }
    if (++s.agree < 4) return;   // ต้องตรงกัน 4 ครั้งติด (~1.2 วินาที) กันค่ากระโดด

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

    if (strcmp(topic, topicTwinOnline) == 0) {
        bool wasOnline = twinOnline;
        twinOnline = strcmp(msgBuf, "online") == 0;
        if (twinOnline && !wasOnline) syncAllSlots();
        if (!twinOnline) {
            for (uint8_t i = 0; i < 3; i++) slots[i].twinOccupied = slots[i].twinOverstay = false;
        }
        return;
    }

    for (uint8_t i = 0; i < 3; i++) {
        if (strcmp(topic, slots[i].slotTopic) == 0) {
            jsonBool(msgBuf, "occupied", slots[i].twinOccupied);
            jsonBool(msgBuf, "overstay", slots[i].twinOverstay);
            return;
        }
    }
}

void connectMqtt() {
    if (WiFi.status() != WL_CONNECTED || mqtt.connected()) return;
    if (mqttTried && millis() - lastMqttTry < 5000) return;
    mqttTried   = true;
    lastMqttTry = millis();
    twinOnline  = false;
    for (uint8_t i = 0; i < 3; i++) slots[i].twinOccupied = false;

    char clientId[48];
    snprintf(clientId, sizeof(clientId), "esp8266-%s-%06X", BOARD_NAME, (unsigned int)ESP.getChipId());
    if (mqtt.connect(clientId, topicBoardOnline, 0, true, "offline")) {
        mqtt.publish(topicBoardOnline, "online", true);
        mqtt.subscribe(topicTwinOnline);
        for (uint8_t i = 0; i < 3; i++) mqtt.subscribe(slots[i].slotTopic);
        syncAllSlots();
        Serial.println("[MQTT] connected");
    }
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

    for (uint8_t i = 0; i < 3; i++) {
        slots[i] = Slot();
        slots[i].ledShown = LED_UNSET;
        slots[i].lastCm = 999;
        snprintf(slots[i].sensorTopic, sizeof(slots[i].sensorTopic), "%s/sensor/%d", baseTopic, UNITY_SLOTS[i]);
        snprintf(slots[i].slotTopic, sizeof(slots[i].slotTopic), "%s/slot/%d", baseTopic, UNITY_SLOTS[i]);

        pinMode(PIN_TRIG[i], OUTPUT);
        digitalWrite(PIN_TRIG[i], LOW);
        pinMode(PIN_ECHO[i], INPUT);
        setLed(i, LED_FREE);
    }

    WiFi.persistent(false);
    WiFi.mode(WIFI_STA);
    WiFi.setAutoReconnect(true);
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

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

    if (millis() - lastSensorRun >= 100) {
        lastSensorRun = millis();
        updateSensor(sensorTurn);
        sensorTurn = (sensorTurn + 1) % 3;
    }

    // แสดงค่าระยะสดของทั้ง 3 ช่องใน Serial Monitor ทุก 500 ms
    if (millis() - lastDebugPrint >= 500) {
        lastDebugPrint = millis();
        Serial.printf("[DIST] ช่อง %d = %ld cm | ช่อง %d = %ld cm | ช่อง %d = %ld cm\n",
                      UNITY_SLOTS[0], slots[0].lastCm,
                      UNITY_SLOTS[1], slots[1].lastCm,
                      UNITY_SLOTS[2], slots[2].lastCm);
    }

    updateLeds();
}