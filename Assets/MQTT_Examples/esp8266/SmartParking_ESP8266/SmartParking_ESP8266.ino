/*
  ====================================================================================
  Smart Parking IoT - NodeMCU ESP8266 "บอร์ด A" (ทางเข้า-ออก + ช่องจอด 1)
  (Wi-Fi + MQTT + RFID RC522 + Servo SG90 + HC-SR04 x2 + LED 2 สี + LCD 1602A I2C)
  เชื่อมกับฉาก SmartParkNew.unity ผ่าน MQTT (คอมโพเนนต์ MqttParkingBridge)

  อ้างอิงการต่อสาย: Ardunio_New_Guid.pdf (ต่อสายจากขา NodeMCU ลง Breadboard โดยตรง ไม่ใช้บอร์ดขยาย
  และไม่ใช้ตัวต้านทาน) ไฟ 5V เอาจากขา VIN (หรือ VU) ของ NodeMCU ซึ่งเป็นไฟจากสาย USB

  ระบบทั้งหมดใช้ ESP8266 3 บอร์ด (ทุกบอร์ดต่อ Wi-Fi ส่งข้อมูลเข้า Unity เอง ไม่ต้องเดินสายถึงกัน)
    บอร์ด A : ไฟล์นี้                                     LCD + RFID + Servo + ช่องจอด 1 + เซนเซอร์ทางออก
    บอร์ด B : SmartParking_ESP8266_Slots.ino (SLOT_GROUP 1)  ช่องจอด 2, 3, 4
    บอร์ด C : SmartParking_ESP8266_Slots.ino (SLOT_GROUP 2)  ช่องจอด 5, 6, 7
  ====================================================================================

  ตั้งค่า Arduino IDE
    1. File > Preferences > Additional boards manager URLs ใส่
       https://arduino.esp8266.com/stable/package_esp8266com_index.json
    2. Boards Manager ติดตั้ง "esp8266 by ESP8266 Community"
    3. Tools > Board เลือก "NodeMCU 1.0 (ESP-12E Module)"

  ไลบรารีที่ต้องติดตั้ง (Library Manager)
    1. PubSubClient by Nick O'Leary            (MQTT)
    2. LiquidCrystal I2C by Frank de Brabander (จอ LCD 1602A I2C)
    3. MFRC522 by GithubCommunity  v1.4.x      (RFID RC522)
    * Servo.h มากับบอร์ด esp8266 อยู่แล้ว (ไม่ต้องลง ESP32Servo แบบเดิม)

  ตารางขา (ตาม PDF)
    D1 (GPIO5)  -> LCD SCL              D2 (GPIO4)  -> LCD SDA
    D5 (GPIO14) -> RC522 SCK            D6 (GPIO12) -> RC522 MISO
    D7 (GPIO13) -> RC522 MOSI           D8 (GPIO15) -> RC522 SDA (SS)
    RC522 RST   -> ราง 3.3V (ไม่ได้ใช้ขา GPIO)    RC522 3.3V -> ราง 3.3V เท่านั้น
    D4 (GPIO2)  -> สายสัญญาณ Servo ไม้กั้นทางเข้า (พ่วง Servo ทางออกที่ขานี้ได้ ยกพร้อมกัน)
    D3 (GPIO0)  -> LED ช่องจอดหลัก: เขียว/น้ำเงิน (+ ที่ 3.3V, - ที่ D3) + แดง (+ ที่ D3, - ที่ GND)
    D0 (GPIO16) -> Trig ของ HC-SR04 ทุกตัว (จุดรวม Trig บน Breadboard)
    RX (GPIO3)  -> Echo HC-SR04 ตัวที่ 1 (ช่องจอดหลัก)
    TX (GPIO1)  -> Echo HC-SR04 ตัวที่ 2 (หน้าไม้กั้นทางออก)

  สิ่งที่ต้องรู้
    - ขา RX/TX ถูกใช้เป็นขาเซนเซอร์ จึงใช้ Serial Monitor ไม่ได้ในโหมดปกติ
      ถอดสายที่ขา RX และ TX ออกก่อนอัปโหลดทุกครั้ง อัปโหลดเสร็จแล้วค่อยเสียบกลับ
    - ทดสอบขั้นที่ 1 ตาม PDF (ยังไม่ต่อ Servo/HC-SR04): ตั้ง SERIAL_DEBUG เป็น 1
      จะเห็นจอ LCD ติด และ UID ของบัตร RFID ขึ้นจอ + Serial Monitor (115200)
      ทดสอบเสร็จแล้วตั้งกลับเป็น 0 แล้วอัปโหลดใหม่ ก่อนต่อ HC-SR04

  การทำงาน
    - แตะบัตร RFID            -> ยกไม้กั้นทางเข้า 3 วินาที + ส่ง .../gate/enter ให้ Unity ยกไม้กั้นในเกม
    - รถจ่อหน้าทางออก (< 8 cm) -> ยกไม้กั้นค้างไว้ตลอดที่รถอยู่ + ส่ง .../gate/exit
    - รถเข้า/ออกช่องจอดหลัก    -> เปลี่ยนสีไฟ + ส่ง .../sensor/{MAIN_SLOT_UNITY} = 1/0 (Unity เสกรถ/เอารถออก)
    - รถในเกมจอดช่องหลัก       -> ไฟแดงติดด้วย (อ่านจาก .../slot/{MAIN_SLOT_UNITY} ที่ Unity ส่งมา)
    - จอดเกิน 2 ชั่วโมง         -> ไฟแดงกระพริบ (หรือเมื่อ Unity แจ้ง overstay ซึ่งเร่งเวลาได้ด้วยปุ่ม TIME SPEED)
    - จอ LCD แสดงจำนวนช่องว่างทั้งลานจาก Unity (.../status) และเวลาจอดของช่องหลัก
    - สั่งเปิดไม้กั้นจริงจากภายนอกได้ เช่น python parking_mqtt_tool.py gate enter
*/

#include <ESP8266WiFi.h>
#include <PubSubClient.h>
#include <Wire.h>
#include <LiquidCrystal_I2C.h>
#include <SPI.h>
#include <MFRC522.h>
#include <Servo.h>
#include <string.h>
#include <stdlib.h>

// ====================================================================================
// 1. ตั้งค่า Wi-Fi & MQTT Broker
// ====================================================================================
const char* WIFI_SSID     = "iQOO Z10 Turbo Pro";   // ESP8266 ต่อได้เฉพาะ Wi-Fi 2.4 GHz (คลื่น 5 GHz ใช้ไม่ได้)
const char* WIFI_PASSWORD = "jjlove444";
const char* MQTT_BROKER   = "broker.hivemq.com";
const int   MQTT_PORT     = 1883;

const char* TOPIC_ROOT    = "dmt321/smartparking";
const char* DEVICE_ID     = "PARK-01";      // ต้องตรงกับ deviceId ใน Unity (MqttParkingBridge)

// ====================================================================================
// 2. ตัวเลือกการทำงาน
// ====================================================================================
// 1 = โหมดทดสอบ: เปิด Serial Monitor (115200) และปิดเซนเซอร์ HC-SR04 ที่ขา RX/TX
#define SERIAL_DEBUG   0

// ช่องจอดหลักบนโมเดลไปโผล่ที่ Slot ไหนใน Unity
// 1-4 = ช่องปกติ (ใช้ LED เขียว), 5 = V.I.P, 6 = Accessibility, 7 = Maternity (ใช้ LED น้ำเงิน)
#define MAIN_SLOT_UNITY  1

// 0 = Servo ทางออกพ่วงสายสัญญาณร่วมขา D4 (ยกพร้อมทางเข้า) ตาม PDF
// 1 = แยก Servo ทางออกไปขา D3 (ตามหมายเหตุใน PDF) -> LED ช่องหลักต้องย้ายไปต่อแบบติดค้าง โค้ดจะไม่คุม LED
#define EXIT_SERVO_ON_D3 0

// ความสว่างไฟแดง 0-255 (255 = digitalWrite HIGH เต็มที่ ไฟแดงชัด)
// PDF แนะนำ analogWrite(D3, 140) เพื่อลดกระแสเพราะไม่มีตัวต้านทาน แต่ LED เขียวกับแดงใช้ขาเดียวกัน
// ช่วงที่ PWM เป็น LOW ไฟเขียวจะติดสลับด้วย ทำให้เห็นเป็นสีส้ม/เหลือง ไม่ใช่สีแดง
// ถ้ายอมให้สีเพี้ยนเพื่อลดกระแส ให้เปลี่ยนเป็น 140
#define RED_PWM_DUTY     255

const int           SLOT_THRESHOLD_CM = 8;      // มีรถจอด ถ้าระยะน้อยกว่านี้ (โมเดลขนาดเล็ก)
const int           EXIT_THRESHOLD_CM = 8;      // มีรถจ่อทางออก ถ้าระยะน้อยกว่านี้
const unsigned long OVERSTAY_MS       = 2UL * 60UL * 60UL * 1000UL; // 2 ชั่วโมง
const unsigned long GATE_OPEN_MS      = 3000;   // ยกไม้กั้นค้างไว้ 3 วินาที
const int           GATE_CLOSED_ANGLE = 90;
const int           GATE_OPEN_ANGLE   = 0;
const uint8_t       LCD_ADDRESS       = 0;      // 0 = หาเองอัตโนมัติ (0x27 / 0x3F ...) หรือใส่เลขที่อยู่ของจอเอง

// ====================================================================================
// 3. ขา Pin (ใช้เลข GPIO ตรงๆ เพื่อให้คอมไพล์ได้ทุกบอร์ด ESP8266)
// ====================================================================================
const uint8_t PIN_LCD_SDA   = 4;   // D2
const uint8_t PIN_LCD_SCL   = 5;   // D1
const uint8_t PIN_RFID_SS   = 15;  // D8  (SCK=D5, MISO=D6, MOSI=D7 เป็นขา SPI มาตรฐาน)
const uint8_t PIN_SERVO_IN  = 2;   // D4
const uint8_t PIN_D3        = 0;   // D3  (LED ช่องหลัก หรือ Servo ทางออก)
const uint8_t PIN_TRIG      = 16;  // D0
const uint8_t PIN_ECHO_SLOT = 3;   // RX
const uint8_t PIN_ECHO_EXIT = 1;   // TX

#if SERIAL_DEBUG
  #define LOG(...) Serial.printf(__VA_ARGS__)
#else
  #define LOG(...) do {} while (0)
#endif

// ====================================================================================
// 4. อุปกรณ์
// ====================================================================================
WiFiClient        wifiClient;
PubSubClient      mqtt(wifiClient);
LiquidCrystal_I2C* lcd = nullptr;   // สร้างใน setup() หลังหาที่อยู่ I2C ของจอเจอแล้ว
MFRC522           rfid(PIN_RFID_SS, MFRC522::UNUSED_PIN); // ขา RST ต่อเข้า 3.3V โดยตรง

Servo entranceServo;
#if EXIT_SERVO_ON_D3
Servo exitServo;
#endif

struct GateState {
    Servo*        servo;
    bool          isOpen;
    unsigned long openedAt;
};

GateState entranceGate = { &entranceServo, false, 0 };
#if EXIT_SERVO_ON_D3
GateState exitGateState = { &exitServo, false, 0 };
GateState* const exitGate = &exitGateState;
#else
GateState* const exitGate = &entranceGate;   // Servo ทางออกพ่วงขา D4 ใช้สถานะเดียวกัน
#endif

// ตัวกรองสัญญาณรบกวนของ HC-SR04: ต้องอ่านได้ค่าใหม่ติดกันหลายครั้งก่อนเปลี่ยนสถานะ
// สถานะไฟ LED ช่องจอด (ประกาศก่อนฟังก์ชันแรก เพราะ Arduino IDE สร้าง prototype ให้อัตโนมัติไว้ตรงนั้น)
enum LedState { LED_UNSET, LED_FREE, LED_RED, LED_OFF };

struct Debounce {
    bool    state;
    uint8_t count;
};

// คืน true เมื่อสถานะเปลี่ยน
bool debounce(Debounce& d, bool reading, uint8_t needed) {
    if (reading == d.state) {
        d.count = 0;
        return false;
    }
    if (++d.count < needed) return false;
    d.state = reading;
    d.count = 0;
    return true;
}

// ====================================================================================
// 5. ตัวแปรสถานะ
// ====================================================================================
char baseTopic[64];
char topicSensor[96];
char topicStatus[96];
char topicSlot[96];
char topicTwinOnline[96];
char topicGatePrefix[96];
char topicEspOnline[96];
char topicBuf[96];

const uint16_t MQTT_BUFFER_SIZE = 2048;   // ข้อความ .../status จาก Unity ยาวราว 1.5 KB
char msgBuf[MQTT_BUFFER_SIZE];

Debounce      slotSensor = { false, 0 };   // state = มีรถในช่องหลัก
Debounce      exitSensor = { false, 0 };   // state = มีรถจ่อทางออก
unsigned long parkedAt   = 0;
bool          owedFree   = false;   // รถจริงออกตอนเน็ตหลุด ยังไม่ได้บอก Unity

// ข้อมูลที่ได้จาก Unity (Digital Twin)
bool twinOnline   = false;
int  twinFree     = -1;
int  twinTotal    = -1;
bool twinOccupied = false;   // ช่องหลักในเกมมีรถ (รวมรถที่ขับเข้าไปจอดหรือเสกจากปุ่มในเกม)
bool twinOverstay = false;

unsigned long lastMqttTry   = 0;
bool          mqttTried     = false;
unsigned long lastSensorRun = 0;
bool          sensorTurn    = false;
unsigned long lastRfidRun   = 0;
unsigned long lastLcdRun    = 0;

char          lcdShown[2][17];
char          overlay[2][17];
unsigned long overlayUntil = 0;

// ====================================================================================
// 6. จอ LCD
// ====================================================================================
// หาที่อยู่ I2C ของจอ: ชิป PCF8574 ใช้ 0x20-0x27, PCF8574A ใช้ 0x38-0x3F (ที่พบบ่อยคือ 0x27 กับ 0x3F)
uint8_t findLcdAddress() {
    const uint8_t candidates[] = { 0x27, 0x3F, 0x26, 0x25, 0x24, 0x23, 0x22, 0x21, 0x20,
                                   0x3E, 0x3D, 0x3C, 0x3B, 0x3A, 0x39, 0x38 };
    for (uint8_t address : candidates) {
        Wire.beginTransmission(address);
        if (Wire.endTransmission() == 0) return address;
    }
    LOG("[LCD] ไม่พบจอบน I2C (เช็กสาย SDA=D2, SCL=D1, VCC=5V, GND) ใช้ 0x27 ไปก่อน\n");
    return 0x27;
}

void lcdLine(uint8_t row, const char* text) {
    char buf[17];
    snprintf(buf, sizeof(buf), "%-16s", text);
    if (strcmp(buf, lcdShown[row]) == 0) return;   // เขียนเฉพาะตอนเปลี่ยน จอไม่กระพริบ
    strcpy(lcdShown[row], buf);
    lcd->setCursor(0, row);
    lcd->print(buf);
}

// แสดงข้อความชั่วคราวทับหน้าจอสถานะ
void showMessage(const char* line0, const char* line1, unsigned long durationMs) {
    snprintf(overlay[0], sizeof(overlay[0]), "%s", line0);
    snprintf(overlay[1], sizeof(overlay[1]), "%s", line1);
    overlayUntil = millis() + durationMs;
    lcdLine(0, overlay[0]);
    lcdLine(1, overlay[1]);
}

const char* mainSlotLabel() {
    switch (MAIN_SLOT_UNITY) {
        case 5:  return "VIP";
        case 6:  return "ACCESS";
        case 7:  return "MATERN";
        default: return "SLOT";
    }
}

// ช่องหลักมีรถ ถ้าเซนเซอร์จริงเจอรถ หรือในเกมมีรถจอดอยู่
bool showOccupied() {
    return slotSensor.state || (twinOnline && twinOccupied);
}

bool isOverstay() {
    if (slotSensor.state && millis() - parkedAt >= OVERSTAY_MS) return true;
    return showOccupied() && twinOnline && twinOverstay;
}

void renderLcd() {
    if ((long)(overlayUntil - millis()) > 0) return;   // ยังแสดงข้อความชั่วคราวอยู่

    char line[24];

    // แถวบน: จำนวนช่องว่างทั้งลาน (จาก Unity) หรือสถานะการเชื่อมต่อ
    if (WiFi.status() != WL_CONNECTED) {
        lcdLine(0, "WiFi connecting");
    } else if (!mqtt.connected()) {
        lcdLine(0, "MQTT connecting");
    } else if (!twinOnline || twinTotal < 0) {
        lcdLine(0, "FREE -/-   WAIT");
    } else {
        snprintf(line, sizeof(line), "FREE %2d/%-2d  LIVE", twinFree, twinTotal);
        lcdLine(0, line);
    }

    // แถวล่าง: ช่องจอดหลัก ว่าง / เวลาจอด / จอดเกินเวลา
    char label[8];
    if (MAIN_SLOT_UNITY >= 1 && MAIN_SLOT_UNITY <= 4) snprintf(label, sizeof(label), "SLOT%d", MAIN_SLOT_UNITY);
    else snprintf(label, sizeof(label), "%s", mainSlotLabel());

    if (!showOccupied()) {
        snprintf(line, sizeof(line), "%-7sFREE", label);
    } else if (isOverstay() && (millis() / 1000) % 2 == 0) {
        snprintf(line, sizeof(line), "%-7sOVERSTAY", label);
    } else if (!slotSensor.state) {
        snprintf(line, sizeof(line), "%-7sIN GAME", label);   // มีรถเฉพาะในเกม
    } else {
        unsigned long s = (millis() - parkedAt) / 1000UL;
        snprintf(line, sizeof(line), "%-7s%02lu:%02lu:%02lu", label, s / 3600UL, (s / 60UL) % 60UL, s % 60UL);
    }
    lcdLine(1, line);
}

// ====================================================================================
// 7. LED ช่องจอดหลัก (ขา D3 ขาเดียวคุม 2 ดวง)
//    LOW = เขียว/น้ำเงินติด (ว่าง), HIGH = แดงติด (มีรถ), INPUT = ดับทั้งคู่ (ใช้ตอนกระพริบ)
// ====================================================================================
void setLed(LedState s);
LedState ledShown = LED_UNSET;

void setLed(LedState s) {
#if !EXIT_SERVO_ON_D3
    if (s == ledShown) return;
    ledShown = s;
    switch (s) {    
        case LED_FREE:
            pinMode(PIN_D3, OUTPUT);
            digitalWrite(PIN_D3, LOW);
            break;
        case LED_RED:
            pinMode(PIN_D3, OUTPUT);
  #if RED_PWM_DUTY >= 255
            digitalWrite(PIN_D3, HIGH);
  #else
            analogWrite(PIN_D3, RED_PWM_DUTY);
  #endif
            break;
        default:
            digitalWrite(PIN_D3, LOW);   // หยุด PWM ก่อน
            pinMode(PIN_D3, INPUT);      // Hi-Z: ดับทั้งสองดวง
            break;
    }
#else
    (void)s;
#endif
}

void updateLed() {
    if (!showOccupied()) setLed(LED_FREE);
    else if (isOverstay()) setLed((millis() / 500) % 2 == 0 ? LED_RED : LED_OFF);
    else setLed(LED_RED);
}

// ====================================================================================
// 8. ไม้กั้น
// ====================================================================================
// publishSuffix = "enter"/"exit" เพื่อบอก Unity, nullptr = ไม่ต้องส่ง (คำสั่งมาจาก MQTT อยู่แล้ว)
void openGate(GateState& g, const char* line0, const char* line1, const char* publishSuffix) {
    g.servo->write(GATE_OPEN_ANGLE);
    g.isOpen   = true;
    g.openedAt = millis();
    showMessage(line0, line1, GATE_OPEN_MS);

    if (publishSuffix != nullptr && mqtt.connected()) {
        snprintf(topicBuf, sizeof(topicBuf), "%s/gate/%s", baseTopic, publishSuffix);
        mqtt.publish(topicBuf, "open");
    }
    LOG("[GATE] open %s\n", publishSuffix != nullptr ? publishSuffix : "(remote)");
}

void updateGate(GateState& g) {
    if (g.isOpen && millis() - g.openedAt >= GATE_OPEN_MS) {
        g.servo->write(GATE_CLOSED_ANGLE);
        g.isOpen = false;
        LOG("[GATE] closed\n");
    }
}

// ====================================================================================
// 9. RFID ทางเข้า
// ====================================================================================
void checkRfid() {
    if (entranceGate.isOpen) return;
    if (!rfid.PICC_IsNewCardPresent() || !rfid.PICC_ReadCardSerial()) return;

    char uid[24] = "UID:";
    for (byte i = 0; i < rfid.uid.size && i < 7; i++) {
        snprintf(uid + strlen(uid), sizeof(uid) - strlen(uid), "%02X", rfid.uid.uidByte[i]);
    }
    LOG("[RFID] %s\n", uid);
    openGate(entranceGate, "WELCOME! OPEN", uid, "enter");

    rfid.PICC_HaltA();
    rfid.PCD_StopCrypto1();
}

// ====================================================================================
// 10. HC-SR04 (Trig ร่วมขา D0)
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

bool sendSlotSensor(bool occupied) {
    if (!mqtt.connected()) return false;
    LOG("[SLOT] -> %s = %s\n", topicSensor, occupied ? "1" : "0");
    return mqtt.publish(topicSensor, occupied ? "1" : "0");
}

// เรียกตอนเซนเซอร์จริงเปลี่ยนสถานะ
void reportSlotChange() {
    if (slotSensor.state) {
        owedFree = false;
        sendSlotSensor(true);
    } else {
        owedFree = !sendSlotSensor(false);   // ส่งไม่ได้ (เน็ตหลุด) ค่อยส่งตอนต่อกลับ
    }
}

// เรียกตอนเพิ่งต่อ MQTT หรือ Unity เพิ่งออนไลน์
// ส่ง "0" เฉพาะตอนที่รถจริงออกไประหว่างเน็ตหลุด เพราะ Unity จะลบรถในช่องเมื่อได้ "0"
// (ถ้าส่ง "0" ทุกครั้งที่ต่อเน็ต รถที่จอดอยู่ในเกมจะหายไป)
void syncSlotSensor() {
    if (slotSensor.state) sendSlotSensor(true);
    else if (owedFree && sendSlotSensor(false)) owedFree = false;
}

void updateSlotSensor() {
    bool reading = readDistanceCm(PIN_ECHO_SLOT) < SLOT_THRESHOLD_CM;
    if (!debounce(slotSensor, reading, 3)) return;   // ค่าเหมือนกัน 3 ครั้ง (~0.9 วินาที)

    parkedAt     = millis();
    twinOverstay = false;
    reportSlotChange();
}

void updateExitSensor() {
    bool reading = readDistanceCm(PIN_ECHO_EXIT) < EXIT_THRESHOLD_CM;
    bool changed = debounce(exitSensor, reading, 2);

    if (changed && exitSensor.state) {
        openGate(*exitGate, "GOODBYE!", "EXIT GATE OPEN", "exit");   // รถเพิ่งมาจ่อทางออก
    } else if (exitSensor.state && exitGate->isOpen) {
        exitGate->openedAt = millis();   // รถยังจ่ออยู่ ยกไม้กั้นค้างไว้ (ปิดหลังรถผ่านไป 3 วินาที)
    }
}

// ====================================================================================
// 11. MQTT
// ====================================================================================
// อ่านค่าตัวเลขจาก JSON ของ Unity เช่น "freeSlots":4
bool jsonInt(const char* json, const char* key, int& out) {
    char pattern[40];
    snprintf(pattern, sizeof(pattern), "\"%s\":", key);
    const char* p = strstr(json, pattern);
    if (p == nullptr) return false;
    out = atoi(p + strlen(pattern));
    return true;
}

// อ่านค่า true/false จาก JSON เช่น "overstay":true
bool jsonBool(const char* json, const char* key, bool& out) {
    char pattern[40];
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

    // สถานะทั้งลานจาก Unity -> จำนวนช่องว่างบนจอ LCD
    if (strcmp(topic, topicStatus) == 0) {
        int value;
        if (jsonInt(msgBuf, "freeSlots", value)) twinFree = value;
        if (jsonInt(msgBuf, "totalSlots", value)) twinTotal = value;
        return;
    }

    // สถานะช่องหลักจาก Unity -> มีรถในเกมไหม + overstay (Unity เร่งเวลาได้ด้วยปุ่ม TIME SPEED)
    if (strcmp(topic, topicSlot) == 0) {
        bool value;
        if (jsonBool(msgBuf, "occupied", value)) twinOccupied = value;
        if (jsonBool(msgBuf, "overstay", value)) twinOverstay = value;
        LOG("[TWIN] slot %d occupied=%d overstay=%d\n", MAIN_SLOT_UNITY, twinOccupied, twinOverstay);
        return;
    }

    // Unity ออนไลน์/ออฟไลน์ (Last Will ของ Unity)
    if (strcmp(topic, topicTwinOnline) == 0) {
        bool wasOnline = twinOnline;
        twinOnline = strcmp(msgBuf, "online") == 0;
        if (twinOnline && !wasOnline) {
            syncSlotSensor();   // Unity เพิ่งเปิด ส่งรถจริงที่จอดอยู่ให้ทันที
        } else if (!twinOnline) {
            twinFree = twinTotal = -1;
            twinOccupied = false;
            twinOverstay = false;
        }
        return;
    }

    // สั่งยกไม้กั้นจริงจากภายนอก (.../gate/enter หรือ .../gate/exit)
    size_t prefixLen = strlen(topicGatePrefix);
    if (strncmp(topic, topicGatePrefix, prefixLen) == 0) {
        const char* target = topic + prefixLen;
        bool isExit = strstr(target, "exit") != nullptr || strstr(target, "out") != nullptr;
        GateState& g = isExit ? *exitGate : entranceGate;
        if (!g.isOpen) {   // ข้อความที่เราส่งเองจะวนกลับมา ไม้กั้นเปิดอยู่แล้วจึงไม่ทำซ้ำ
            openGate(g, "REMOTE OPEN", isExit ? "EXIT GATE" : "ENTRANCE GATE", nullptr);
        }
    }
}

void connectMqtt() {
    if (WiFi.status() != WL_CONNECTED || mqtt.connected()) return;
    if (mqttTried && millis() - lastMqttTry < 5000) return;   // ลองใหม่ทุก 5 วินาที
    mqttTried   = true;
    lastMqttTry = millis();

    twinOnline = false;
    twinFree = twinTotal = -1;
    twinOccupied = false;

    char clientId[40];
    snprintf(clientId, sizeof(clientId), "esp8266-smartpark-%06X", (unsigned int)ESP.getChipId());
    LOG("[MQTT] connecting %s:%d ...\n", MQTT_BROKER, MQTT_PORT);

    // ใช้ topic .../esp/gate/online แยกจาก .../online ของ Unity (ของเดิมใช้ร่วมกัน ทำให้สถานะ Unity ผิด)
    if (mqtt.connect(clientId, topicEspOnline, 0, true, "offline")) {
        mqtt.publish(topicEspOnline, "online", true);
        mqtt.subscribe(topicStatus);
        mqtt.subscribe(topicSlot);
        mqtt.subscribe(topicTwinOnline);
        snprintf(topicBuf, sizeof(topicBuf), "%s+", topicGatePrefix);
        mqtt.subscribe(topicBuf);
        syncSlotSensor();
        LOG("[MQTT] connected\n");
    } else {
        LOG("[MQTT] failed, state = %d\n", mqtt.state());
    }
}

void setupWiFi() {
    lcdLine(0, "Connecting WiFi");
    lcdLine(1, WIFI_SSID);

    WiFi.persistent(false);
    WiFi.mode(WIFI_STA);
    WiFi.setAutoReconnect(true);
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

    for (int i = 0; i < 20 && WiFi.status() != WL_CONNECTED; i++) delay(500);

    if (WiFi.status() == WL_CONNECTED) {
        LOG("[WiFi] connected, IP %s\n", WiFi.localIP().toString().c_str());
        lcdLine(1, WiFi.localIP().toString().c_str());
    } else {
        LOG("[WiFi] timeout (จะต่อใหม่อัตโนมัติ)\n");
        lcdLine(1, "WiFi pending...");
    }
    delay(1000);
}

// ====================================================================================
// 12. Setup & Loop
// ====================================================================================
void setup() {
#if SERIAL_DEBUG
    Serial.begin(115200);
    delay(200);
    Serial.println("\n[Smart Parking ESP8266] SERIAL_DEBUG = 1 (ปิด HC-SR04 ที่ขา RX/TX)");
#endif

    snprintf(baseTopic, sizeof(baseTopic), "%s/%s", TOPIC_ROOT, DEVICE_ID);
    snprintf(topicSensor, sizeof(topicSensor), "%s/sensor/%d", baseTopic, MAIN_SLOT_UNITY);
    snprintf(topicStatus, sizeof(topicStatus), "%s/status", baseTopic);
    snprintf(topicSlot, sizeof(topicSlot), "%s/slot/%d", baseTopic, MAIN_SLOT_UNITY);
    snprintf(topicTwinOnline, sizeof(topicTwinOnline), "%s/online", baseTopic);
    snprintf(topicGatePrefix, sizeof(topicGatePrefix), "%s/gate/", baseTopic);
    snprintf(topicEspOnline, sizeof(topicEspOnline), "%s/esp/gate/online", baseTopic);

    // LCD 1602A (I2C: SDA=D2, SCL=D1)
    Wire.begin(PIN_LCD_SDA, PIN_LCD_SCL);
    uint8_t lcdAddress = LCD_ADDRESS != 0 ? LCD_ADDRESS : findLcdAddress();
    LOG("[LCD] address 0x%02X\n", lcdAddress);
    lcd = new LiquidCrystal_I2C(lcdAddress, 16, 2);
    lcd->init();
    delay(50);
    lcd->backlight();
    lcd->clear();
    memset(lcdShown, 0, sizeof(lcdShown));
    lcdLine(0, "Smart Parking");
    lcdLine(1, "ESP8266 start...");

    // RFID RC522 (SPI: SCK=D5, MISO=D6, MOSI=D7, SS=D8)
    SPI.begin();
    rfid.PCD_Init();

    // Servo ไม้กั้น
    entranceServo.attach(PIN_SERVO_IN, 500, 2400);
    entranceServo.write(GATE_CLOSED_ANGLE);
#if EXIT_SERVO_ON_D3
    exitServo.attach(PIN_D3, 500, 2400);
    exitServo.write(GATE_CLOSED_ANGLE);
#else
    analogWriteRange(255);
    setLed(LED_FREE);
#endif

    // HC-SR04
    pinMode(PIN_TRIG, OUTPUT);
    digitalWrite(PIN_TRIG, LOW);
#if !SERIAL_DEBUG
    pinMode(PIN_ECHO_SLOT, INPUT);   // ใช้ขา RX เป็น GPIO
    pinMode(PIN_ECHO_EXIT, INPUT);   // ใช้ขา TX เป็น GPIO
#endif

    // Network
    setupWiFi();
    mqtt.setServer(MQTT_BROKER, MQTT_PORT);
    mqtt.setBufferSize(MQTT_BUFFER_SIZE);
    mqtt.setKeepAlive(30);
    mqtt.setSocketTimeout(5);
    mqtt.setCallback(onMqttMessage);
}

void loop() {
    // 1. Wi-Fi & MQTT
    if (WiFi.status() == WL_CONNECTED) {
        if (!mqtt.connected()) connectMqtt();
        else mqtt.loop();
    }

    // 2. ปิดไม้กั้นเมื่อครบเวลา
    updateGate(entranceGate);
#if EXIT_SERVO_ON_D3
    updateGate(*exitGate);
#endif

    // 3. RFID ทางเข้า (ทุก 100 ms)
    if (millis() - lastRfidRun >= 100) {
        lastRfidRun = millis();
        checkRfid();
    }

    // 4. HC-SR04 สลับอ่านทีละตัวทุก 150 ms (Trig ร่วมกัน จึงอ่านพร้อมกันไม่ได้)
#if !SERIAL_DEBUG
    if (millis() - lastSensorRun >= 150) {
        lastSensorRun = millis();
        sensorTurn = !sensorTurn;
        if (sensorTurn) updateSlotSensor();
        else updateExitSensor();
    }
#endif

    // 5. ไฟ LED และจอ LCD
    updateLed();
    if (millis() - lastLcdRun >= 250) {
        lastLcdRun = millis();
        renderLcd();
    }
}
