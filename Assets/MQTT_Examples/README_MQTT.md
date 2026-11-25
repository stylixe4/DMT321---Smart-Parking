# Smart Parking Twin — MQTT

Unity (ฉาก SmartParkNew) เชื่อม MQTT ผ่านคอมโพเนนต์ **MqttParkingBridge** ที่อยู่บน `GameManager`
ไม่ต้องลงแพ็กเกจเพิ่ม ตัว client เขียนเองใน `Assets/Projects/Scripts/MqttClientLite.cs` (MQTT 3.1.1, TCP/TLS)

## ค่าเริ่มต้น

| ค่า | ค่าเริ่มต้น | แก้ที่ |
|---|---|---|
| Broker | `broker.hivemq.com:1883` (สาธารณะ ไม่มีรหัส) | Inspector ของ MqttParkingBridge |
| Topic หลัก | `dmt321/smartparking/PARK-01` | `topicRoot` + `deviceId` |
| ส่งสถานะซ้ำ | ทุก 5 วินาที + ทุกครั้งที่มีการเปลี่ยนแปลง | `heartbeatSeconds` |

> broker สาธารณะใครก็อ่าน/ส่งได้ ใช้ทดสอบในห้องเรียนเท่านั้น งานจริงให้ใช้ broker ของตัวเอง
> (Mosquitto ในเครื่อง / HiveMQ Cloud / EMQX Cloud) แล้วเปิด `useTls`, port 8883 และใส่ username/password
> ถ้าหลายกลุ่มทดสอบพร้อมกัน ให้เปลี่ยน `deviceId` เป็นชื่อกลุ่ม เช่น `PARK-G3` จะได้ไม่ชนกัน

## Topic ทั้งหมด

| Topic | ทิศทาง | Retain | เนื้อหา |
|---|---|---|---|
| `.../status` | Unity → ภายนอก | ✓ | JSON สรุปทั้งลาน (ด้านล่าง) |
| `.../slot/{1-7}` | Unity → ภายนอก | ✓ | JSON ของช่องนั้น |
| `.../online` | Unity → ภายนอก | ✓ | `online` / `offline` (Last Will: ถ้า Unity หลุดเอง broker จะส่ง `offline` ให้) |
| `.../ack` | Unity → ภายนอก | | ผลของคำสั่ง `{"ok":true,"cmd":"spawn","slot":3,"message":"..."}` |
| `.../cmd` | ภายนอก → Unity | | คำสั่ง (ด้านล่าง) |
| `.../sensor/{1-7}` | ภายนอก → Unity | | `1` = มีรถ, `0` = ว่าง (รับ `true/false`, `occupied/free`, `{"occupied":true}` ได้ด้วย) |
| `.../gate/{enter,exit}` | ภายนอก ↔ Unity + บอร์ด | | สั่งเปิดไม้กั้นทางเข้า หรือ ไม้กั้นทางออก ชั่วคราว 3 วินาที (จาก RFID / เซนเซอร์ทางออก) บอร์ด ESP8266 ก็ยกไม้กั้นจริงตามด้วย |
| `.../esp/{gate,slots-2-4,slots-5-7}/online` | บอร์ด → ภายนอก | ✓ | `online` / `offline` ของบอร์ด ESP8266 A / B / C (แยกจาก `.../online` ของ Unity) |

หมายเลขช่อง: 1-4 = ช่องปกติ, 5 = V.I.P, 6 = Accessibility, 7 = Maternity (ตรงกับปุ่ม SLOT 1-7 บนจอ)

### ตัวอย่าง `.../status`

```json
{
  "deviceId": "PARK-01",
  "timestamp": "2026-11-18T17:17:38+07:00",
  "totalSlots": 7, "freeSlots": 4, "occupiedSlots": 1, "reservedSlots": 2,
  "availabilityPercent": 57,
  "slot1Occupied": true, "slot2Occupied": false, "slot3Occupied": false, "slot4Occupied": false,
  "vipOccupied": false, "accessibilityOccupied": false, "maternityOccupied": false,
  "warningMessage": "-",
  "timeScale": 20.0, "overstayLimitHours": 2.0,
  "slots": [
    {"slot":1,"name":"Slot 1","type":"Normal","occupied":true,"reserved":false,"overstay":false,
     "parkedSeconds":705,"parkedTime":"00:11:45","spawnedByApp":true}
  ]
}
```

ฟิลด์ `slot1Occupied … maternityOccupied`, `reservedSlots`, `warningMessage` ใช้ชื่อเดียวกับ `Assets/Projects/Data/parking_normal.json`
`parkedSeconds` เป็นเวลาจำลอง (ถูกเร่งตามปุ่ม TIME SPEED)

### คำสั่งที่ส่งเข้า `.../cmd`

ส่งได้ทั้ง JSON หรือข้อความสั้น

| ข้อความสั้น | JSON | ผล |
|---|---|---|
| `spawn 3` | `{"cmd":"spawn","slot":3}` | เสกรถลงช่อง 3 |
| `remove 3` | `{"cmd":"remove","slot":3}` | เอารถที่เสกไว้ออก |
| `toggle 3` | `{"cmd":"toggle","slot":3}` | สลับ เพิ่ม/เอาออก |
| `reserve 6` / `unreserve 6` | `{"cmd":"reserve","slot":6,"state":true}` | จอง / ยกเลิกจอง |
| `speed 10` | `{"cmd":"speed","value":10}` | เร่งเวลาจอด x10 (1 = ปกติ) |
| `normal` | `{"cmd":"normal"}` | เวลาเดินปกติ |
| `clear` | `{"cmd":"clear"}` | เอารถที่เสกไว้ออกทั้งหมด |
| `status` | `{"cmd":"status"}` | ขอสถานะล่าสุดทันที |

คำสั่งที่ค้างแบบ retain ใน broker จะไม่ถูกทำซ้ำ

## ทดสอบเร็วๆ

1. กด Play ใน Unity ดูแถว **MQTT ONLINE** ที่แผง TIME SPEED มุมขวา
2. เปิด https://www.hivemq.com/demos/websocket-client/ → Connect → Add New Topic Subscription: `dmt321/smartparking/PARK-01/#`
3. Publish topic `dmt321/smartparking/PARK-01/cmd` ข้อความ `spawn 3` → รถโผล่ช่อง 3 และได้ `ack` กลับมา

หรือใช้ Python (โฟลเดอร์ `python/`):

```bash
pip install paho-mqtt
python parking_mqtt_tool.py monitor          # ดูสถานะที่ Unity ส่งออกมา
python parking_mqtt_tool.py cmd "speed 10"   # ส่งคำสั่งเร่งเวลา
python parking_mqtt_tool.py sensor 1 1       # จำลองเซนเซอร์ช่อง 1 มีรถ (เสกรถเข้าช่อง 1)
python parking_mqtt_tool.py sensor 1 0       # จำลองเซนเซอร์ช่อง 1 ว่าง (เอารถออกจากช่อง 1)
python parking_mqtt_tool.py gate enter       # จำลองแตะบัตร RFID เปิดไม้กั้นทางเข้า
python parking_mqtt_tool.py gate exit        # จำลองผ่านเซนเซอร์ทางออกเปิดไม้กั้นทางออก
python parking_mqtt_tool.py simulate         # สุ่มรถเข้า-ออกช่อง 1-4 อัตโนมัติ
```

## ฮาร์ดแวร์จริง (NodeMCU ESP8266 x3) — ฉบับปัจจุบัน

ใช้ NodeMCU ESP8266 3 บอร์ด ทุกบอร์ดต่อ Wi-Fi และ MQTT เอง (ไม่ต้องเดินสายเชื่อมบอร์ดถึงกัน) ต่อสายจากขา NodeMCU ลง Breadboard โดยตรง ไม่ใช้บอร์ดขยายและไม่ใช้ตัวต้านทาน
ขั้นตอนต่อสายแบบละเอียดอยู่ในเอกสาร "คู่มือต่อวงจร Smart Parking (NodeMCU ESP8266)"

| บอร์ด | โค้ด | หน้าที่ | HC-SR04 |
|---|---|---|---|
| A | `esp8266/SmartParking_ESP8266/SmartParking_ESP8266.ino` | LCD + RFID + Servo ไม้กั้น + ช่อง 1 + เซนเซอร์ทางออก | 2 |
| B | `esp8266/SmartParking_ESP8266_Slots/SmartParking_ESP8266_Slots.ino` ตั้ง `SLOT_GROUP 1` | ช่อง 2, 3, 4 | 3 |
| C | ไฟล์เดียวกับบอร์ด B ตั้ง `SLOT_GROUP 2` | ช่อง 5 (V.I.P), 6 (Accessibility), 7 (Maternity) | 3 |

- Board: `NodeMCU 1.0 (ESP-12E Module)` จาก "esp8266 by ESP8266 Community"
- ไลบรารี: PubSubClient (ทุกบอร์ด), LiquidCrystal I2C (Frank de Brabander) และ MFRC522 v1.4.x (เฉพาะบอร์ด A) — Servo.h มากับบอร์ดอยู่แล้ว
- **ESP8266 ต่อ Wi-Fi ได้เฉพาะ 2.4 GHz** ทุกบอร์ดต้องใช้ Wi-Fi, broker และ `DEVICE_ID` เดียวกัน
- จอ LCD หาที่อยู่ I2C เองอัตโนมัติ (`LCD_ADDRESS 0`) ถ้าจอไม่ขึ้น ใช้ `esp8266/LCD_I2C_Test` สแกนหาก่อน

### บอร์ด A (ตาม PDF)

| ขา | อุปกรณ์ | หน้าที่ |
|---|---|---|
| D1 / D2 | LCD 1602A (I2C) | SCL / SDA (VCC ใช้ 5V) |
| D5 / D6 / D7 / D8 | RFID RC522 | SCK / MISO / MOSI / SDA(SS) — RST ต่อ 3.3V, VCC ใช้ 3.3V เท่านั้น |
| D4 | Servo SG90 | ไม้กั้นทางเข้า (พ่วง Servo ทางออกร่วมขานี้ได้) |
| D3 | LED ช่อง 1 | LOW = เขียว (ว่าง), HIGH = แดง (มีรถ), กระพริบแดง = จอดเกิน 2 ชม. |
| D0 | HC-SR04 ทั้ง 2 ตัว | Trig ร่วมกัน |
| RX / TX | HC-SR04 ตัวที่ 1 / 2 | Echo ช่อง 1 / Echo หน้าไม้กั้นทางออก |

### บอร์ด B และ C

| ขา | อุปกรณ์ | หน้าที่ |
|---|---|---|
| D0 | HC-SR04 ทั้ง 3 ตัว | Trig ร่วมกัน |
| D5 / D6 / D8 | HC-SR04 ตัวที่ 1 / 2 / 3 | Echo ช่องแรก / ช่องที่สอง / ช่องที่สาม ของบอร์ด (B = 2/3/4, C = 5/6/7) |
| D1 / D2 / D7 | LED ช่องแรก / สอง / สาม | LOW = เขียว (B) หรือ น้ำเงิน (C) ว่าง, HIGH = แดง มีรถ |
| D4 | LED บนตัวบอร์ด | ติดค้าง = ต่อ MQTT แล้ว, กระพริบ = กำลังต่อ |

บอร์ด B/C ไม่ใช้ขา RX/TX จึงไม่ต้องถอดสายตอนอัปโหลด และดู Serial Monitor (115200) ได้ตลอด

การทำงานกับ Unity
- แต่ละบอร์ดส่ง `.../sensor/{เลขช่อง}` = `1`/`0` เมื่อรถเข้า/ออก (กรองสัญญาณรบกวน 3 ครั้งติด) และส่งซ้ำทุกครั้งที่ Unity เพิ่งออนไลน์
- แตะบัตร RFID → ไม้กั้นจริงยก 3 วินาที + ส่ง `.../gate/enter` (จอ LCD แสดง UID ของบัตร)
- รถจ่อทางออก → ไม้กั้นยกค้างตลอดที่รถอยู่ + ส่ง `.../gate/exit`
- บอร์ด A รับ `.../status` มาแสดงจำนวนช่องว่างทั้งลานบน LCD
- ทุกบอร์ดรับ `.../slot/{n}` ของช่องตัวเอง ไฟแดงจึงกระพริบตาม overstay ของ Unity ได้ (เร่งเวลาด้วยปุ่ม TIME SPEED เพื่อสาธิต)
- `python parking_mqtt_tool.py gate enter` จะยกทั้งไม้กั้นในเกมและไม้กั้นจริง

ข้อควรระวัง
- บอร์ด A ใช้ขา RX/TX เป็นขาเซนเซอร์ ต้องถอดสายที่ RX และ TX ก่อนอัปโหลดทุกครั้ง และใช้ Serial Monitor ไม่ได้ (ทดสอบจอ + RFID อย่างเดียวให้ตั้ง `SERIAL_DEBUG 1`)
- บอร์ด A: D3 (GPIO0) และ TX (GPIO1) เป็นขาที่ชิปอ่านตอนบูต ถ้าบอร์ดค้างไม่ยอมบูต ให้ถอดสาย D3/TX แล้วกด RST ก่อนเสียบกลับ
- PDF แนะนำ `analogWrite(D3, 140)` ให้ไฟแดง แต่วงจรนี้ใช้ขาเดียวคุมสองดวง ช่วง PWM เป็น LOW ไฟเขียวจะติดสลับ เห็นเป็นสีส้ม โค้ดจึงใช้ `digitalWrite HIGH` (ปรับได้ที่ `RED_PWM_DUTY` ของบอร์ด A)

## ฮาร์ดแวร์รุ่นเดิม (ESP32) — เก็บไว้อ้างอิง

ซอร์สโค้ด: `esp32/SmartParking_ESP32/SmartParking_ESP32.ino` (ตาม `smart parking arduino guide.pdf` ชุดเดิม: HC-SR04 x8, Dual LED x7)
- แมปช่องจอดตรงกับ Unity: ช่อง VIP 1-3 บนบอร์ดจะไปโผล่ที่ Slot 5, 6, 7 ใน Unity, และช่องทั่วไป 1-4 จะไปโผล่ที่ Slot 1-4 ใน Unity
- ช่อง Reserve ทั้ง 3 ช่อง (VIP, Accessibility, Maternity) เมื่อรถออกจะคืนสถานะเป็น Reserve อัตโนมัติเสมอ

## ใช้จากสคริปต์อื่นใน Unity

```csharp
var bridge = FindFirstObjectByType<MqttParkingBridge>();
bridge.Publish(bridge.BaseTopic + "/event", "{\"gate\":\"open\"}");
bridge.ExecuteCommand("spawn 2");        // ใช้ตัวแปลคำสั่งเดียวกับ MQTT
var status = bridge.BuildStatus();       // ข้อมูลสถานะชุดเดียวกับที่ส่งออก
```

คลิกขวาที่หัวคอมโพเนนต์ MqttParkingBridge (ตอน Play) มีเมนู `MQTT/Test: toggle Slot 1 via broker` ไว้ทดสอบวนรอบผ่าน broker

ข้อจำกัด: build เป็น WebGL ใช้ TCP ไม่ได้ (ต้องใช้ MQTT over WebSocket แทน) ส่วน Windows/Mac/Android ใช้ได้ตามปกติ
