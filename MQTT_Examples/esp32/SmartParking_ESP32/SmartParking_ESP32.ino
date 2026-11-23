/*
  ====================================================================================
  Smart Parking IoT - ESP32 Complete Firmware (Wi-Fi + MQTT + RFID + Ultrasonic + LCD)
  เชื่อมต่อระบบจำลอง SmartParkNew.unity ผ่าน MQTT (HiveMQ)
  
  อ้างอิงสเปกจาก: smart parking arduino guide.pdf
  ====================================================================================
  
  ไลบรารีที่ต้องติดตั้งใน Arduino IDE (ผ่าน Library Manager):
    1. PubSubClient by Nick O'Leary (สำหรับ MQTT)
    2. LiquidCrystal_I2C by Frank de Brabander (สำหรับจอ LCD 1602A I2C)
    3. MFRC522 by GithubCommunity (สำหรับ RFID RC522)
    4. ESP32Servo by Kevin Harrington (สำหรับควบคุม Servo SG90)
    
  *** หมายเหตุสำคัญเรื่องการจัดขา Pin เพื่อไม่ให้ชนกัน ***
  ในไฟล์ PDF หน้า 6 ขา EXIT_ECHO_PIN (เซนเซอร์ทางออก) ถูกกำหนดเป็นขา 26 ซึ่งชนกับ ledPins[6] (LED ช่องที่ 7)
  ในโค้ดนี้จึงได้จัดขาใหม่:
    - EXIT_ECHO_PIN = 26 (เซนเซอร์ทางออกตัวที่ 8)
    - ledPins[6]    = 3  (ใช้ GPIO 3 / RX0 สำหรับคุม LED ช่องที่ 7 แทน ทำให้ขาไม่ชนกัน 
                         และไม่กระทบกับ Serial.print ของขา TX0 / GPIO 1)
*/

#include <WiFi.h>
#include <PubSubClient.h>
#include <Wire.h>
#include <LiquidCrystal_I2C.h>
#include <SPI.h>
#include <MFRC522.h>
#include <ESP32Servo.h>

// ====================================================================================
// 1. ตั้งค่า Wi-Fi & MQTT Broker
// ====================================================================================
const char* WIFI_SSID       = "YOUR_WIFI_SSID";         // เปลี่ยนเป็นชื่อ Wi-Fi ของคุณ
const char* WIFI_PASSWORD   = "YOUR_WIFI_PASSWORD";     // เปลี่ยนเป็นรหัสผ่าน Wi-Fi
const char* MQTT_BROKER     = "broker.hivemq.com";      // หรือ IP ของ Mosquitto ในวง LAN
const int   MQTT_PORT       = 1883;

const char* TOPIC_ROOT      = "dmt321/smartparking";
const char* DEVICE_ID       = "PARK-01";                // ต้องตรงกับ deviceId ใน Unity (MqttParkingBridge)

char baseTopic[64];
char topicBuf[96];

WiFiClient espClient;
PubSubClient mqttClient(espClient);

// ====================================================================================
// 2. ตั้งค่าฮาร์ดแวร์ & ขา Pin ต่างๆ
// ====================================================================================

// --- จอ LCD 1602A (I2C: SDA=21, SCL=22) ---
// Address ปกติ 0x27 หรือ 0x3F
LiquidCrystal_I2C lcd(0x27, 16, 2);

// --- RFID RC522 (SPI) ---
#define SS_PIN  5
#define RST_PIN 4
MFRC522 rfid(SS_PIN, RST_PIN);

// --- Servo มอเตอร์ไม้กั้น ---
#define SERVO_PIN 13
Servo gateServo;

// --- เซนเซอร์วัดระยะ HC-SR04 (7 ช่องจอด + 1 ทางออก) ---
#define TRIG_PIN 12
const int echoPins[7] = {36, 39, 34, 35, 32, 33, 25};

// เซนเซอร์ทางออก (ถ้าไม่ได้ต่อ สามารถตั้งค่า USE_EXIT_SENSOR เป็น false)
#define USE_EXIT_SENSOR true
#define EXIT_ECHO_PIN 26

// --- ขาควบคุม LED 7 ช่อง (ใช้เทคนิค 1 ขาคุม 2 ดวงกลับขั้ว) ---
// ช่อง 0-2 (แถว 5, 10, 15)  : น้ำเงิน (ว่าง) + แดง (มีรถ)
// ช่อง 3-6 (แถว 20, 25, 30, 35) : เขียว (ว่าง) + แดง (มีรถ)
// *ช่องที่ 7 (index 6) ย้ายไปใช้ขา GPIO 3 (RX0) เพื่อเลี่ยงขา 26 ที่ใช้กับ Exit Echo*
#if USE_EXIT_SENSOR
const int ledPins[7] = {27, 14, 2, 17, 16, 15, 3};
#else
const int ledPins[7] = {27, 14, 2, 17, 16, 15, 26};
#endif

// ====================================================================================
// 3. การแมปช่องจอด (Mapping ระหว่าง ESP32 กับ Unity SmartParkNew)
// ====================================================================================
// โลกจริง (ตามเอกสาร PDF):
//   Index 0: VIP 1
//   Index 1: VIP 2 (หรือ Accessible)
//   Index 2: VIP 3 (หรือ Maternity)
//   Index 3: ทั่วไป 1 (General 1)
//   Index 4: ทั่วไป 2 (General 2)
//   Index 5: ทั่วไป 3 (General 3)
//   Index 6: ทั่วไป 4 (General 4)
//
// ใน Unity (ฉาก SmartParkNew):
//   Slot 1: Normal 1  (ParkingSlot_Normal_1)
//   Slot 2: Normal 2  (ParkingSlot_Normal_2)
//   Slot 3: Normal 3  (ParkingSlot_Normal_3)
//   Slot 4: Normal 4  (ParkingSlot_Normal_4)
//   Slot 5: VIP       (ParkingSlot_VIP)
//   Slot 6: Accessible(ParkingSlot_Accessibility)
//   Slot 7: Maternity (ParkingSlot_Maternity)
const int unitySlotMap[7] = { 5, 6, 7, 1, 2, 3, 4 };

// ====================================================================================
// 4. ตัวแปรเก็บสถานะการทำงาน
// ====================================================================================
bool isOccupied[7] = {false, false, false, false, false, false, false};
unsigned long parkStartTime[7] = {0, 0, 0, 0, 0, 0, 0};

// ระบบจัดการไม้กั้นแบบ Non-blocking (ไม่ใช้ delay ค้าง เพื่อให้ Wi-Fi / MQTT ไม่หลุด)
unsigned long gateCloseTime = 0;
bool isGateOpen = false;
String lcdGateMessage = "";

unsigned long lastSensorScanTime = 0;
const unsigned long SENSOR_SCAN_INTERVAL = 300; // สแกนเซนเซอร์ทุก 300 ms

unsigned long lastMqttRetryTime = 0;
unsigned long lastLcdUpdateTime = 0;

// ====================================================================================
// 5. ฟังก์ชันควบคุม LED
// ====================================================================================
// state: 0 = ว่าง (น้ำเงิน หรือ เขียว ติด), 1 = มีรถ (แดง ติด), 2 = ดับทั้งคู่ (ใช้กระพริบเตือน)
void setSlotLED(int slot, int state) {
    int pin = ledPins[slot];
    if (state == 0) {
        pinMode(pin, OUTPUT);
        digitalWrite(pin, LOW);  // ไฟสีน้ำเงินหรือเขียวติด
    } else if (state == 1) {
        pinMode(pin, OUTPUT);
        digitalWrite(pin, HIGH); // ไฟสีแดงติด
    } else {
        pinMode(pin, INPUT);     // Hi-Z ดับทั้งสองดวง
    }
}

// ====================================================================================
// 6. ฟังก์ชันอ่านระยะอัลตราโซนิก HC-SR04
// ====================================================================================
long readDistance(int echoPin) {
    digitalWrite(TRIG_PIN, LOW);
    delayMicroseconds(2);
    digitalWrite(TRIG_PIN, HIGH);
    delayMicroseconds(10);
    digitalWrite(TRIG_PIN, LOW);

    long duration = pulseIn(echoPin, HIGH, 25000); // timeout 25 ms (~4 เมตร)
    if (duration == 0) return 999;
    return (long)(duration * 0.034 / 2.0); // แปลงเป็น cm
}

// ====================================================================================
// 7. ควบคุมไม้กั้น (Non-blocking)
// ====================================================================================
void openGate(const char* reason, const char* gateTopicSuffix) {
    gateServo.write(90); // ยกไม้กั้นขึ้น
    isGateOpen = true;
    gateCloseTime = millis() + 3000; // เปิดค้างไว้ 3 วินาที (3000 ms)
    lcdGateMessage = String(reason);

    // อัปเดตข้อความบนจอ LCD
    lcd.clear();
    lcd.setCursor(0, 0);
    lcd.print(reason);
    lcd.setCursor(0, 1);
    lcd.print("Gate Opening...");

    // ส่งสัญญาณ MQTT ไปบอก Unity ให้ยกไม้กั้นในเกมขึ้นพร้อมกัน!
    snprintf(topicBuf, sizeof(topicBuf), "%s/gate/%s", baseTopic, gateTopicSuffix);
    mqttClient.publish(topicBuf, "open");
    Serial.printf("[GATE] %s -> MQTT: %s (open)\n", reason, topicBuf);
}

void checkGateTimeout() {
    if (isGateOpen && millis() >= gateCloseTime) {
        gateServo.write(0); // ปิดไม้กั้นลง
        isGateOpen = false;
        lcdGateMessage = "";
        Serial.println("[GATE] ไม้กั้นปิดลงแล้ว");
        updateLCDDisplay(); // คืนหน้าจอแสดงจำนวนที่ว่าง
    }
}

// ====================================================================================
// 8. อัปเดตหน้าจอ LCD 1602A
// ====================================================================================
void updateLCDDisplay() {
    if (isGateOpen) return; // ถ้ากำลังเปิดไม้กั้น ให้แสดงข้อความต้อนรับก่อน

    int freeVIP = 0;
    int freeGen = 0;

    for (int i = 0; i < 3; i++) {
        if (!isOccupied[i]) freeVIP++;
    }
    for (int i = 3; i < 7; i++) {
        if (!isOccupied[i]) freeGen++;
    }

    lcd.clear();
    lcd.setCursor(0, 0);
    lcd.print("VIP Free : ");
    lcd.print(freeVIP);
    lcd.print("/3 ");

    lcd.setCursor(0, 1);
    lcd.print("GEN Free : ");
    lcd.print(freeGen);
    lcd.print("/4 ");
}

// ====================================================================================
// 9. ระบบเชื่อมต่อ Wi-Fi & MQTT
// ====================================================================================
void setupWiFi() {
    Serial.print("Connecting to Wi-Fi: ");
    Serial.println(WIFI_SSID);
    lcd.clear();
    lcd.setCursor(0, 0);
    lcd.print("Connecting WiFi");

    WiFi.mode(WIFI_STA);
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

    int attempts = 0;
    while (WiFi.status() != WL_CONNECTED && attempts < 20) {
        delay(500);
        Serial.print(".");
        attempts++;
    }

    if (WiFi.status() == WL_CONNECTED) {
        Serial.println("\nWiFi Connected!");
        Serial.print("IP Address: ");
        Serial.println(WiFi.localIP());
        lcd.setCursor(0, 1);
        lcd.print("WiFi OK!");
    } else {
        Serial.println("\nWiFi Connect Timeout (จะต่อใหม่อัตโนมัติใน loop)");
        lcd.setCursor(0, 1);
        lcd.print("WiFi Pending...");
    }
    delay(1000);
}

void connectMQTT() {
    if (WiFi.status() != WL_CONNECTED) return;
    if (mqttClient.connected()) return;

    if (millis() - lastMqttRetryTime < 5000) return; // ลองใหม่ทุก 5 วินาที
    lastMqttRetryTime = millis();

    String clientId = "ESP32-SmartPark-" + String((uint32_t)ESP.getEfuseMac(), HEX);
    Serial.printf("Connecting to MQTT Broker (%s:%d)...\n", MQTT_BROKER, MQTT_PORT);

    char willTopic[80];
    snprintf(willTopic, sizeof(willTopic), "%s/online", baseTopic);

    if (mqttClient.connect(clientId.c_str(), willTopic, 0, true, "offline")) {
        Serial.println("MQTT Connected successfully!");
        mqttClient.publish(willTopic, "online", true);

        // ส่งสถานะเริ่มต้นของทุกช่องให้ Unity ทันทีหลังเชื่อมต่อ
        for (int i = 0; i < 7; i++) {
            int unitySlot = unitySlotMap[i];
            snprintf(topicBuf, sizeof(topicBuf), "%s/sensor/%d", baseTopic, unitySlot);
            mqttClient.publish(topicBuf, isOccupied[i] ? "1" : "0");
        }
    } else {
        Serial.printf("MQTT failed, state = %d\n", mqttClient.state());
    }
}

// ====================================================================================
// 10. Setup
// ====================================================================================
void setup() {
    Serial.begin(115200);
    delay(500);
    Serial.println("\n===========================================");
    Serial.println("  Smart Parking IoT Controller (ESP32)");
    Serial.println("===========================================");

    snprintf(baseTopic, sizeof(baseTopic), "%s/%s", TOPIC_ROOT, DEVICE_ID);

    // จอ LCD
    Wire.begin(21, 22);
    lcd.init();
    lcd.backlight();
    lcd.setCursor(0, 0);
    lcd.print("Smart Parking");
    lcd.setCursor(0, 1);
    lcd.print("Initializing...");

    // SPI & RFID RC522
    SPI.begin();
    rfid.PCD_Init();

    // Servo ไม้กั้น
    gateServo.attach(SERVO_PIN);
    gateServo.write(0); // ท่าเริ่มต้น = ปิดไม้กั้น

    // HC-SR04
    pinMode(TRIG_PIN, OUTPUT);
    digitalWrite(TRIG_PIN, LOW);
    for (int i = 0; i < 7; i++) {
        pinMode(echoPins[i], INPUT);
        setSlotLED(i, 0); // เริ่มต้นเปิดไฟว่าง (สีน้ำเงินสำหรับ VIP, สีเขียวสำหรับทั่วไป)
    }

#if USE_EXIT_SENSOR
    pinMode(EXIT_ECHO_PIN, INPUT);
#endif

    // เชื่อมต่อ Network
    setupWiFi();
    mqttClient.setServer(MQTT_BROKER, MQTT_PORT);

    updateLCDDisplay();
}

// ====================================================================================
// 11. Loop หลัก
// ====================================================================================
void loop() {
    // 1. จัดการการเชื่อมต่อ Wi-Fi และ MQTT
    if (WiFi.status() == WL_CONNECTED) {
        if (!mqttClient.connected()) {
            connectMQTT();
        } else {
            mqttClient.loop();
        }
    }

    // 2. เช็กเวลาปิดไม้กั้น
    checkGateTimeout();

    // 3. ตรวจสอบบัตร RFID ทางเข้า
    if (!isGateOpen) {
        if (rfid.PICC_IsNewCardPresent() && rfid.PICC_ReadCardSerial()) {
            Serial.println("[RFID] ตรวจพบบัตรที่ทางเข้า -> สั่งเปิดไม้กั้น");
            openGate("Welcome! Open...", "enter");
            rfid.PICC_HaltA();
            rfid.PCD_StopCrypto1();
        }
    }

    // 4. ตรวจสอบเซนเซอร์อัลตราโซนิกทางออก (EXIT)
#if USE_EXIT_SENSOR
    if (!isGateOpen) {
        long exitDist = readDistance(EXIT_ECHO_PIN);
        if (exitDist < 8) { // มีรถจ่อทางออกน้อยกว่า 8 cm
            Serial.printf("[EXIT SENSOR] ตรวจพบรถที่ทางออก (ระยะ %ld cm) -> สั่งเปิดไม้กั้น\n", exitDist);
            openGate("Goodbye! Exit...", "exit");
        }
    }
#endif

    // 5. สแกนเซนเซอร์ช่องจอดทั้ง 7 ช่อง (สแกนเป็นรอบเพื่อความแม่นยำ)
    if (millis() - lastSensorScanTime >= SENSOR_SCAN_INTERVAL) {
        lastSensorScanTime = millis();
        bool anyStateChanged = false;

        for (int i = 0; i < 7; i++) {
            long dist = readDistance(echoPins[i]);
            bool currentOccupied = (dist < 8); // มีรถจอด ถ้าระยะน้อยกว่า 8 cm

            // ถ้ารถเพิ่งเข้ามาจอด
            if (currentOccupied && !isOccupied[i]) {
                isOccupied[i] = true;
                parkStartTime[i] = millis();
                anyStateChanged = true;

                int unitySlot = unitySlotMap[i];
                snprintf(topicBuf, sizeof(topicBuf), "%s/sensor/%d", baseTopic, unitySlot);
                mqttClient.publish(topicBuf, "1");
                Serial.printf("[SLOT %d -> Unity Slot %d] รถเข้าจอด (ระยะ %ld cm)\n", i + 1, unitySlot, dist);
            }
            // ถ้ารถขับออกไป
            else if (!currentOccupied && isOccupied[i]) {
                isOccupied[i] = false;
                parkStartTime[i] = 0;
                anyStateChanged = true;

                int unitySlot = unitySlotMap[i];
                snprintf(topicBuf, sizeof(topicBuf), "%s/sensor/%d", baseTopic, unitySlot);
                mqttClient.publish(topicBuf, "0");
                Serial.printf("[SLOT %d -> Unity Slot %d] รถขับออก (ระยะ %ld cm)\n", i + 1, unitySlot, dist);
            }

            // จัดการสีไฟ LED & เตือนจอดเกินเวลา (2 ชั่วโมง = 7,200,000 ms)
            if (isOccupied[i]) {
                if ((millis() - parkStartTime[i]) > 7200000UL) {
                    // กระพริบไฟแดงเตือนเมื่อจอดเกินเวลา
                    if ((millis() / 500) % 2 == 0) setSlotLED(i, 1);
                    else setSlotLED(i, 2);
                } else {
                    setSlotLED(i, 1); // สีแดงติดค้าง (มีรถจอด)
                }
            } else {
                setSlotLED(i, 0); // สีน้ำเงิน (VIP) หรือ เขียว (ทั่วไป)
            }

            delay(20); // หน่วงเวลาเล็กน้อยระหว่างช่องป้องกันคลื่นเสียงสะท้อนกวนกัน
        }

        if (anyStateChanged) {
            updateLCDDisplay();
        }
    }
}
