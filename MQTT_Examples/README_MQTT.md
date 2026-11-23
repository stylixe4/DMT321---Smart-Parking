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
| `.../gate/{enter,exit}` | ภายนอก → Unity | | สั่งเปิดไม้กั้นทางเข้า หรือ ไม้กั้นทางออก ชั่วคราว 3 วินาที (จาก RFID / เซนเซอร์ทางออก) |

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

## ฮาร์ดแวร์จริง (ESP32)

ซอร์สโค้ดฉบับเต็ม: `esp32/SmartParking_ESP32/SmartParking_ESP32.ino`
- อ้างอิงวงจรและสเปกขาตาม `smart parking arduino guide.pdf`
- รวม Wi-Fi + MQTT + RFID RC522 + Servo SG90 + HC-SR04 x8 + Dual LED x7 + LCD 1602A
- แก้ไขปัญหาขา 26 ชนกันระหว่าง Exit Echo กับ LED ช่อง 7 โดยย้าย LED ช่อง 7 ไปขา GPIO 3 (RX0)
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
