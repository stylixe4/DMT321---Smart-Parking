/*
  ทดสอบจอ LCD 1602A (I2C) บน NodeMCU ESP8266  —  SDA = D2, SCL = D1
  ----------------------------------------------------------------------------
  ใช้หาสาเหตุ "จอไม่ขึ้น" ก่อนลงโค้ดตัวจริง (SmartParking_ESP8266)
    1. สแกนหาอุปกรณ์ I2C ทุกที่อยู่ แล้วพิมพ์ผลใน Serial Monitor (115200)
    2. ถ้าเจอที่อยู่ของจอ จะสั่งให้จอขึ้นข้อความ "LCD OK" + ที่อยู่
    3. สแกนซ้ำทุก 5 วินาที ขยับสาย/หมุนตัวปรับความคมชัดระหว่างนี้ได้เลย

  ต่อแค่จอ LCD (จะต่อ RFID ไว้ด้วยก็ได้ ไม่เกี่ยวกัน) และยังไม่ต้องต่อ HC-SR04 ที่ขา RX/TX
  Board: NodeMCU 1.0 (ESP-12E Module)   ไลบรารี: LiquidCrystal I2C by Frank de Brabander
*/

#include <Wire.h>
#include <LiquidCrystal_I2C.h>

const uint8_t PIN_SDA = 4;   // D2
const uint8_t PIN_SCL = 5;   // D1

bool isLcdAddress(uint8_t address) {
    return (address >= 0x20 && address <= 0x27) || (address >= 0x38 && address <= 0x3F);
}

void scan() {
    Serial.println();
    Serial.println("=== สแกน I2C (SDA=D2, SCL=D1) ===");
    uint8_t found = 0;

    for (uint8_t address = 1; address < 127; address++) {
        Wire.beginTransmission(address);
        if (Wire.endTransmission() != 0) continue;

        found++;
        Serial.printf("พบอุปกรณ์ที่ 0x%02X\n", address);
        if (!isLcdAddress(address)) continue;

        LiquidCrystal_I2C lcd(address, 16, 2);
        lcd.init();
        lcd.backlight();
        lcd.clear();
        lcd.setCursor(0, 0);
        lcd.print("LCD OK");
        char line[17];
        snprintf(line, sizeof(line), "Address 0x%02X", address);
        lcd.setCursor(0, 1);
        lcd.print(line);
        Serial.printf("  -> ส่งข้อความไปที่จอแล้ว ถ้าไฟติดแต่ไม่เห็นตัวอักษร ให้หมุนตัวปรับสีฟ้าหลังจอ\n");
    }

    if (found == 0) {
        Serial.println("ไม่พบอุปกรณ์ I2C เลย ให้เช็ก:");
        Serial.println("  - SDA ของจอ -> D2, SCL ของจอ -> D1 (สลับกันหรือไม่)");
        Serial.println("  - VCC ของจอ -> 5V (วัดได้ราว 4.5-5V), GND ของจอ -> GND");
        Serial.println("  - สายจัมเปอร์หลวม / เสียบผิดแถวบน Breadboard");
    }
}

void setup() {
    Serial.begin(115200);
    delay(500);
    Serial.println();
    Serial.println("LCD I2C Test - NodeMCU ESP8266");
    Wire.begin(PIN_SDA, PIN_SCL);
}

void loop() {
    scan();
    delay(5000);
}
