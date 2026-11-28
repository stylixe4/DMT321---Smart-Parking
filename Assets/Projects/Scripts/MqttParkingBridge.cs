using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// สะพาน MQTT ของลานจอดรถ (Digital Twin)
//   ส่งออก : สถานะลานจอดทั้งหมด + รายช่อง (JSON) เมื่อมีการเปลี่ยนแปลง และส่งซ้ำทุก heartbeatSeconds
//   รับเข้า : คำสั่ง (เสกรถ/เอารถออก/จอง/ปรับความเร็วเวลา) และค่าจากเซนเซอร์จริงของแต่ละช่อง
//
// Topic ทั้งหมดขึ้นต้นด้วย {topicRoot}/{deviceId}  เช่น dmt321/smartparking/PARK-01
//   .../status        (ส่งออก, retain)  JSON สรุปทั้งลาน
//   .../slot/{n}      (ส่งออก, retain)  JSON ของช่อง n (1-7)
//   .../online        (ส่งออก, retain)  "online" / "offline" (Last Will)
//   .../ack           (ส่งออก)          ผลลัพธ์ของคำสั่งแต่ละครั้ง
//   .../cmd           (รับเข้า)         คำสั่ง JSON หรือข้อความสั้น เช่น "spawn 3", "speed 10"
//   .../sensor/{n}    (รับเข้า)         1 = มีรถ, 0 = ว่าง (เช่นจาก ESP32 + เซนเซอร์อัลตราโซนิก)
[DisallowMultipleComponent]
public class MqttParkingBridge : MonoBehaviour
{
    [Header("Broker")]
    public bool connectOnStart = true;
    public string brokerHost = "broker.hivemq.com";
    public int brokerPort = 1883;
    [Tooltip("เปิดเมื่อใช้พอร์ต TLS เช่น 8883 (HiveMQ Cloud, EMQX Cloud)")]
    public bool useTls = false;
    [Tooltip("ยอมรับใบรับรองที่ตรวจสอบไม่ผ่าน (ใช้กับ broker ทดสอบในห้องแล็บเท่านั้น)")]
    public bool allowUntrustedCertificates = false;
    public string username = "";
    public string password = "";
    [Tooltip("ว่างไว้ = สุ่มให้อัตโนมัติ (ต้องไม่ซ้ำกับเครื่องอื่นที่ต่อ broker เดียวกัน)")]
    public string clientId = "";
    [Range(5, 300)] public int keepAliveSeconds = 30;

    [Header("Topic = {topicRoot}/{deviceId}/...")]
    public string topicRoot = "dmt321/smartparking";
    public string deviceId = "PARK-01";

    [Header("ส่งข้อมูลออก")]
    [Tooltip("ส่งสถานะซ้ำทุกกี่วินาที (0 = ส่งเฉพาะตอนเปลี่ยน)")]
    public float heartbeatSeconds = 5f;
    public bool publishSlotTopics = true;
    [Tooltip("ให้ broker จำข้อความล่าสุดไว้ ผู้ที่ subscribe ทีหลังจะได้สถานะทันที")]
    public bool retainState = true;

    [Header("รับข้อมูลเข้า")]
    public bool acceptCommands = true;
    public bool acceptSensorTopics = true;

    [Header("อ้างอิง")]
    public ParkingLotManager manager;
    public SlotCarSpawner spawner;
    public GateController entranceGate;
    public GateController exitGate;
    public TMP_Text statusText;
    public Image statusDot;
    [Tooltip("ให้เกมทำงานต่อแม้หน้าต่างไม่ได้โฟกัส (แดชบอร์ดจะอัปเดตตลอด)")]
    public bool keepRunningInBackground = true;
    public bool logToConsole = true;

    [Header("สีสถานะ")]
    public Color onlineColor = new Color(0.3f, 0.87f, 0.5f);
    public Color connectingColor = new Color(1f, 0.686f, 0f);
    public Color offlineColor = new Color(0.94f, 0.33f, 0.31f);
    public Color labelColor = new Color(0.62f, 0.66f, 0.72f);

    public string BaseTopic => $"{topicRoot.TrimEnd('/')}/{deviceId}";
    public MqttClientLite.ConnectionState State => client != null ? client.State : MqttClientLite.ConnectionState.Disconnected;
    public bool IsConnected => client != null && client.IsConnected;

    private MqttClientLite client;
    private MqttClientLite.ConnectionState shownState = (MqttClientLite.ConnectionState)(-1);
    private string shownError;
    private float heartbeatTimer;
    private bool statusDirty;
    private float dirtyTimer;
    private SlotSnapshot[] lastSlots = new SlotSnapshot[0];
    private float lastTimeScale = -1f;
    private string lastWarning;

    private struct SlotSnapshot
    {
        public bool occupied, reserved, overstay;
    }

    // ------------------------------------------------------------ Unity

    private void Awake()
    {
        if (manager == null) manager = FindFirstObjectByType<ParkingLotManager>();
        if (spawner == null) spawner = FindFirstObjectByType<SlotCarSpawner>();
        if (entranceGate == null)
        {
            var go = GameObject.Find("EntranceSensor");
            if (go != null) entranceGate = go.GetComponent<GateController>();
        }
        if (exitGate == null)
        {
            var go = GameObject.Find("ExitSensor");
            if (go != null) exitGate = go.GetComponent<GateController>();
        }
    }

    private void Start()
    {
        if (keepRunningInBackground) Application.runInBackground = true;
        UpdateStatusText(true);
        if (connectOnStart) Connect();
    }

    private void OnDisable()
    {
        Disconnect();
    }

    private void Update()
    {
        if (client == null)
        {
            UpdateStatusText(false);
            return;
        }

        while (client.TryReceive(out var message)) HandleMessage(message);

        bool justConnected = shownState != MqttClientLite.ConnectionState.Connected && client.IsConnected;
        UpdateStatusText(false);
        if (!client.IsConnected) return;

        if (justConnected)
        {
            client.Publish(BaseTopic + "/online", "online", true);
            PublishStatus(true);
            return;
        }

        DetectChanges();
        dirtyTimer -= Time.unscaledDeltaTime;
        if (statusDirty && dirtyTimer <= 0f) PublishStatus(false);

        if (heartbeatSeconds > 0f)
        {
            heartbeatTimer += Time.unscaledDeltaTime;
            if (heartbeatTimer >= heartbeatSeconds) PublishStatus(false, false);
        }
    }

    // ------------------------------------------------------------ Connection

    [ContextMenu("MQTT/Connect")]
    public void Connect()
    {
        Disconnect();
        string id = string.IsNullOrWhiteSpace(clientId)
            ? $"smartpark-{deviceId}-{Guid.NewGuid().ToString("N").Substring(0, 6)}"
            : clientId.Trim();

        client = new MqttClientLite(new MqttClientLite.Options
        {
            Host = brokerHost.Trim(),
            Port = brokerPort,
            UseTls = useTls,
            AllowUntrustedCertificates = allowUntrustedCertificates,
            ClientId = id,
            Username = string.IsNullOrEmpty(username) ? null : username,
            Password = string.IsNullOrEmpty(username) ? null : password,
            KeepAliveSeconds = (ushort)Mathf.Clamp(keepAliveSeconds, 5, 300),
            WillTopic = BaseTopic + "/online",
            WillPayload = "offline",
            WillRetain = true
        });
        if (acceptCommands) client.Subscribe(BaseTopic + "/cmd");
        if (acceptSensorTopics) client.Subscribe(BaseTopic + "/sensor/+");
        client.Subscribe(BaseTopic + "/gate/+");
        client.Subscribe(BaseTopic + "/esp/#"); // สถานะออนไลน์ของบอร์ด ESP8266 (แสดงใน EVENT LOG)
        client.Start();
        Log($"กำลังเชื่อมต่อ {brokerHost}:{brokerPort} (client id: {id}) topic: {BaseTopic}/#");
    }

    [ContextMenu("MQTT/Disconnect")]
    public void Disconnect()
    {
        if (client == null) return;
        if (client.IsConnected) client.Publish(BaseTopic + "/online", "offline", true);
        client.Stop();
        client = null;
        Log("ตัดการเชื่อมต่อ MQTT แล้ว");
        UpdateStatusText(true);
    }

    // ส่งข้อความไปยัง topic ใดก็ได้ (ให้สคริปต์อื่นใช้ต่อ)
    public void Publish(string topic, string payload, bool retain = false)
    {
        if (client != null) client.Publish(topic, payload, retain);
    }

    // ทดสอบวนรอบผ่าน broker: ส่งคำสั่งเข้า .../cmd ของตัวเอง แล้วรับกลับมาทำงานจริง
    public void SendCommandViaBroker(string command)
    {
        if (!IsConnected)
        {
            Debug.LogWarning("[MQTT] ยังไม่ได้เชื่อมต่อ broker");
            return;
        }
        client.Publish(BaseTopic + "/cmd", command, false);
        Log($"ส่งคำสั่งทดสอบ → {BaseTopic}/cmd : {command}");
    }

    [ContextMenu("MQTT/Test: Open Entrance Gate")]
    public void TestOpenEntrance() => OpenGate("enter", 3f);

    [ContextMenu("MQTT/Test: Open Exit Gate")]
    public void TestOpenExit() => OpenGate("exit", 3f);

    [ContextMenu("MQTT/Test: toggle Slot 1 via broker")]
    private void TestToggleSlot1() => SendCommandViaBroker("toggle 1");

    [ContextMenu("MQTT/Test: speed 10x via broker")]
    private void TestSpeed10() => SendCommandViaBroker("{\"cmd\":\"speed\",\"value\":10}");

    [ContextMenu("MQTT/Publish status now")]
    public void PublishStatusNow() => PublishStatus(true);

    // ------------------------------------------------------------ ส่งออก

    private void DetectChanges()
    {
        if (manager == null) return;
        var slots = manager.Slots;
        if (lastSlots.Length != slots.Count)
        {
            lastSlots = new SlotSnapshot[slots.Count];
            MarkDirty();
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s == null) continue;
            var now = new SlotSnapshot { occupied = s.IsOccupied, reserved = s.IsReserved, overstay = s.IsOverstay };
            var before = lastSlots[i];
            if (now.occupied != before.occupied || now.reserved != before.reserved || now.overstay != before.overstay)
            {
                lastSlots[i] = now;
                MarkDirty();
                if (publishSlotTopics) PublishSlot(i);
            }
        }

        if (!Mathf.Approximately(lastTimeScale, manager.TimeScale) || lastWarning != manager.CurrentWarning)
        {
            lastTimeScale = manager.TimeScale;
            lastWarning = manager.CurrentWarning;
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        if (!statusDirty) dirtyTimer = 0.2f; // รวบการเปลี่ยนแปลงที่เกิดติดๆ กันเป็นข้อความเดียว
        statusDirty = true;
    }

    private void PublishStatus(bool includeSlots, bool log = true)
    {
        statusDirty = false;
        heartbeatTimer = 0f;
        if (client == null || manager == null) return;

        var msg = BuildStatus();
        client.Publish(BaseTopic + "/status", JsonUtility.ToJson(msg), retainState);

        if (includeSlots && publishSlotTopics)
        {
            var slots = manager.Slots;
            lastSlots = new SlotSnapshot[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s != null) lastSlots[i] = new SlotSnapshot { occupied = s.IsOccupied, reserved = s.IsReserved, overstay = s.IsOverstay };
                PublishSlot(i, false);
            }
            ParkingEventLog.Add(ParkingEventKind.TX, $"status + slot/1-{slots.Count} (free {msg.freeSlots}/{msg.totalSlots})");
            lastTimeScale = manager.TimeScale;
            lastWarning = manager.CurrentWarning;
        }
        if (log && logToConsole) Log($"ส่งสถานะ → {BaseTopic}/status (ว่าง {msg.freeSlots}/{msg.totalSlots}, x{msg.timeScale:0.##})");
    }

    private void PublishSlot(int index, bool logEvent = true)
    {
        var s = manager.Slots[index];
        if (s == null) return;
        client.Publish($"{BaseTopic}/slot/{index + 1}", JsonUtility.ToJson(BuildSlot(index, s)), retainState);
        if (logEvent)
        {
            string state = s.IsOccupied ? "occupied" : s.IsReserved ? "reserved" : "free";
            ParkingEventLog.Add(ParkingEventKind.TX, $"slot/{index + 1} {state}{(s.IsOverstay ? " overstay" : "")}");
        }
    }

    public ParkingStatusMessage BuildStatus()
    {
        var msg = new ParkingStatusMessage
        {
            deviceId = deviceId,
            timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            timeScale = manager.TimeScale,
            overstayLimitHours = manager.OverstayLimitHours,
            warningMessage = manager.CurrentWarning
        };

        var slots = manager.Slots;
        var list = new List<SlotStatusMessage>();
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s == null) continue;
            list.Add(BuildSlot(i, s));

            msg.totalSlots++;
            if (s.IsOccupied) msg.occupiedSlots++;
            else if (s.IsReserved) msg.reservedSlots++;
            else msg.freeSlots++;

            bool occ = s.IsOccupied;
            switch (s.slotType)
            {
                case ParkingSlotType.VIP: msg.vipOccupied |= occ; break;
                case ParkingSlotType.Accessibility: msg.accessibilityOccupied |= occ; break;
                case ParkingSlotType.Maternity: msg.maternityOccupied |= occ; break;
                default:
                    if (s.slotNumber == 1) msg.slot1Occupied = occ;
                    else if (s.slotNumber == 2) msg.slot2Occupied = occ;
                    else if (s.slotNumber == 3) msg.slot3Occupied = occ;
                    else if (s.slotNumber == 4) msg.slot4Occupied = occ;
                    break;
            }
        }
        msg.slots = list.ToArray();
        msg.availabilityPercent = msg.totalSlots > 0 ? Mathf.RoundToInt(msg.freeSlots * 100f / msg.totalSlots) : 0;
        return msg;
    }

    private SlotStatusMessage BuildSlot(int index, ParkingSensor s)
    {
        int t = Mathf.FloorToInt(s.ParkedSeconds);
        return new SlotStatusMessage
        {
            slot = index + 1,
            name = s.DisplayName,
            type = s.slotType.ToString(),
            occupied = s.IsOccupied,
            reserved = s.IsReserved,
            overstay = s.IsOverstay,
            parkedSeconds = t,
            parkedTime = $"{t / 3600:00}:{t / 60 % 60:00}:{t % 60:00}",
            spawnedByApp = spawner != null && spawner.HasSpawnedCar(index)
        };
    }

    // ------------------------------------------------------------ รับเข้า

    private void HandleMessage(MqttClientLite.Message message)
    {
        string topic = message.Topic;
        string text = message.Text.Trim();

        // 0. สถานะบอร์ด ESP8266 (.../esp/gate/online, .../esp/slots-2-4/online, ...)
        string espPrefix = BaseTopic + "/esp/";
        if (topic.StartsWith(espPrefix, StringComparison.Ordinal))
        {
            string rest = topic.Substring(espPrefix.Length);
            if (rest == "online" || rest.EndsWith("/online", StringComparison.Ordinal))
            {
                string board = rest == "online" ? "esp" : rest.Substring(0, rest.Length - "/online".Length);
                bool up = text.Equals("online", StringComparison.OrdinalIgnoreCase);
                ParkingEventLog.Add(up ? ParkingEventKind.NET : ParkingEventKind.WARN, $"board {board} {(up ? "online" : "offline")}");
            }
            return;
        }

        // 1. รับคำสั่งควบคุมทั่วไป (.../cmd)
        if (topic == BaseTopic + "/cmd")
        {
            if (message.Retained) return; // ไม่ทำคำสั่งเก่าที่ค้างอยู่ใน broker ซ้ำ
            Log($"รับคำสั่ง ← {topic} : {text}");
            ParkingEventLog.Add(ParkingEventKind.RX, "cmd " + text);
            var result = ExecuteCommand(text);
            PublishAck(result);
            return;
        }

        // 2. รับคำสั่งเปิดไม้กั้น (.../gate/enter, .../gate/exit) จาก RFID หรือเซนเซอร์ทางออก
        string gatePrefix = BaseTopic + "/gate/";
        if (topic.StartsWith(gatePrefix, StringComparison.Ordinal))
        {
            string gateTarget = topic.Substring(gatePrefix.Length).Trim().ToLowerInvariant();
            Log($"ไม้กั้น ← {topic} : {text}");
            ParkingEventLog.Add(ParkingEventKind.RX, $"gate/{gateTarget} {text}");
            OpenGate(gateTarget, 3f);
            return;
        }

        // 3. รับค่าเซนเซอร์ช่องจอด (.../sensor/1..7 หรือ .../sensor/vip ...)
        string sensorPrefix = BaseTopic + "/sensor/";
        if (topic.StartsWith(sensorPrefix, StringComparison.Ordinal))
        {
            string key = topic.Substring(sensorPrefix.Length).Trim().ToLowerInvariant();
            int slotNumber = ParseSlotKey(key);
            if (slotNumber < 1 || slotNumber > 7)
            {
                Debug.LogWarning($"[MQTT] topic เซนเซอร์ไม่ถูกต้อง ({key}): {topic}");
                ParkingEventLog.Add(ParkingEventKind.WARN, $"bad sensor topic: sensor/{key}");
                return;
            }
            if (!TryParseOccupied(text, out bool occupied))
            {
                Debug.LogWarning($"[MQTT] ค่าเซนเซอร์ไม่ถูกต้อง ({text}) ใช้ 1/0, true/false, occupied/free");
                ParkingEventLog.Add(ParkingEventKind.WARN, $"bad sensor value: sensor/{slotNumber} = {text}");
                return;
            }
            Log($"เซนเซอร์ ← slot {slotNumber} = {(occupied ? "มีรถ" : "ว่าง")}");
            ParkingEventLog.Add(ParkingEventKind.RX, $"sensor/{slotNumber} = {(occupied ? "1 (car)" : "0 (empty)")}");
            var result = SetSlotFromSensor(slotNumber, occupied);
            result.cmd = "sensor";
            if (!result.ok) PublishAck(result);
        }
    }

    // รูปแบบคำสั่งที่รองรับ (JSON หรือข้อความสั้น):
    //   {"cmd":"spawn","slot":3}      หรือ  spawn 3     เสกรถลงช่อง 3
    //   {"cmd":"remove","slot":3}     หรือ  remove 3    เอารถที่เสกไว้ออก
    //   {"cmd":"toggle","slot":3}     หรือ  toggle 3
    //   {"cmd":"reserve","slot":5,"state":true}  หรือ  reserve 5 / unreserve 5
    //   {"cmd":"speed","value":10}    หรือ  speed 10    เร่งเวลาจอด (1 = ปกติ)
    //   {"cmd":"clear"}               หรือ  clear       เอารถที่เสกไว้ออกทั้งหมด
    //   {"cmd":"status"}              หรือ  status      ขอสถานะล่าสุดทันที
    public CommandResult ExecuteCommand(string text)
    {
        var c = ParseCommand(text);
        if (c == null || string.IsNullOrEmpty(c.cmd)) return CommandResult.Fail("?", 0, "อ่านคำสั่งไม่ออก: " + text);

        string cmd = c.cmd.Trim().ToLowerInvariant();
        switch (cmd)
        {
            case "spawn":
            case "add":
                return SpawnOrRemove(cmd, c.slot, true);
            case "remove":
            case "delete":
                return SpawnOrRemove(cmd, c.slot, false);
            case "toggle":
            {
                if (!CheckSlot(cmd, c.slot, out var fail)) return fail;
                bool has = spawner != null && spawner.HasSpawnedCar(c.slot - 1);
                return SpawnOrRemove(cmd, c.slot, !has);
            }
            case "reserve":
            case "unreserve":
            {
                if (!CheckSlot(cmd, c.slot, out var fail)) return fail;
                bool reserve = cmd == "reserve" && (c.state || !c.hasState);
                var sensor = manager.Slots[c.slot - 1];
                if (sensor.IsOccupied) return CommandResult.Fail(cmd, c.slot, "ช่องนี้มีรถจอดอยู่ จองไม่ได้");
                sensor.SetReserved(reserve);
                return CommandResult.Ok(cmd, c.slot, reserve ? "จองช่องแล้ว" : "ยกเลิกการจองแล้ว");
            }
            case "speed":
            case "timescale":
            case "time":
            {
                if (manager == null) return CommandResult.Fail(cmd, 0, "ไม่พบ ParkingLotManager");
                float v = c.value;
                if (float.IsNaN(v) || v < 0f) return CommandResult.Fail(cmd, 0, "ต้องระบุ value เช่น speed 10");
                manager.SetTimeScale(v);
                return CommandResult.Ok(cmd, 0, $"ความเร็วเวลาจอด x{manager.TimeScale:0.##}");
            }
            case "normal":
                if (manager == null) return CommandResult.Fail(cmd, 0, "ไม่พบ ParkingLotManager");
                manager.ResetTimeScale();
                return CommandResult.Ok(cmd, 0, "เวลาเดินปกติ (x1)");
            case "clear":
                if (spawner == null) return CommandResult.Fail(cmd, 0, "ไม่พบ SlotCarSpawner");
                spawner.RemoveAllCars();
                return CommandResult.Ok(cmd, 0, "เอารถที่เสกไว้ออกทั้งหมด");
            case "status":
                PublishStatus(true);
                return CommandResult.Ok(cmd, 0, "ส่งสถานะแล้ว");
            case "gate":
            case "open":
            {
                string target = !string.IsNullOrEmpty(c.target) ? c.target : (c.slot == 2 ? "exit" : "enter");
                OpenGate(target, 3f);
                return CommandResult.Ok(cmd, 0, $"เปิดไม้กั้น {target} 3 วินาที");
            }
            default:
                return CommandResult.Fail(cmd, c.slot, "ไม่รู้จักคำสั่งนี้");
        }
    }

    // แปลงชื่อ topic หรือหมายเลขช่องเป็นหมายเลข Slot 1-7 ใน Unity
    private int ParseSlotKey(string key)
    {
        if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int num))
            return num;
        switch (key)
        {
            case "vip":
            case "vip1":
                return 5;
            case "accessibility":
            case "access":
            case "disabled":
            case "vip2":
                return 6;
            case "maternity":
            case "vip3":
                return 7;
            case "normal1": return 1;
            case "normal2": return 2;
            case "normal3": return 3;
            case "normal4": return 4;
            default: return -1;
        }
    }

    // คำสั่งเปิดไม้กั้นทางเข้า หรือ ไม้กั้นทางออก
    public void OpenGate(string target, float duration = 3f)
    {
        target = target.Trim().ToLowerInvariant();
        if (target.Contains("enter") || target.Contains("in") || target == "1")
        {
            if (entranceGate != null)
            {
                entranceGate.TriggerOpen(duration);
                Log($"[Gate] ไม้กั้นทางเข้าเปิด {duration:0.#} วินาที");
            }
            else Debug.LogWarning("[MQTT] ไม่พบ GateController ของไม้กั้นทางเข้า (EntranceSensor)");
        }
        else if (target.Contains("exit") || target.Contains("out") || target == "2")
        {
            if (exitGate != null)
            {
                exitGate.TriggerOpen(duration);
                Log($"[Gate] ไม้กั้นทางออกเปิด {duration:0.#} วินาที");
            }
            else Debug.LogWarning("[MQTT] ไม่พบ GateController ของไม้กั้นทางออก (ExitSensor)");
        }
        else if (target == "all" || target == "both")
        {
            if (entranceGate != null) entranceGate.TriggerOpen(duration);
            if (exitGate != null) exitGate.TriggerOpen(duration);
            Log($"[Gate] ไม้กั้นทั้งสองฝั่งเปิด {duration:0.#} วินาที");
        }
    }

    private CommandResult SetSlotFromSensor(int slotNumber, bool occupied)
    {
        if (!CheckSlot("sensor", slotNumber, out var fail)) return fail;
        int index = slotNumber - 1;
        var sensor = manager.Slots[index];
        bool hasAppCar = spawner.HasSpawnedCar(index);

        if (occupied)
        {
            if (hasAppCar || sensor.IsOccupied) return CommandResult.Ok("sensor", slotNumber, "มีรถอยู่แล้ว");
            return spawner.SpawnCar(index) != null
                ? CommandResult.Ok("sensor", slotNumber, "เพิ่มรถตามเซนเซอร์")
                : CommandResult.Fail("sensor", slotNumber, "เพิ่มรถไม่สำเร็จ");
        }

        if (hasAppCar)
        {
            spawner.RemoveCar(index);
            return CommandResult.Ok("sensor", slotNumber, "เอารถออกตามเซนเซอร์");
        }
        return sensor.IsOccupied
            ? CommandResult.Fail("sensor", slotNumber, "ช่องนี้มีรถที่ผู้เล่นขับมาจอด เอาออกจากเซนเซอร์ไม่ได้")
            : CommandResult.Ok("sensor", slotNumber, "ว่างอยู่แล้ว");
    }

    private CommandResult SpawnOrRemove(string cmd, int slotNumber, bool spawn)
    {
        if (!CheckSlot(cmd, slotNumber, out var fail)) return fail;
        int index = slotNumber - 1;
        if (spawn)
        {
            if (spawner.HasSpawnedCar(index) || manager.Slots[index].IsOccupied) return CommandResult.Fail(cmd, slotNumber, "ช่องนี้มีรถจอดอยู่แล้ว");
            return spawner.SpawnCar(index) != null
                ? CommandResult.Ok(cmd, slotNumber, "เพิ่มรถแล้ว")
                : CommandResult.Fail(cmd, slotNumber, "เพิ่มรถไม่สำเร็จ");
        }
        if (!spawner.HasSpawnedCar(index)) return CommandResult.Fail(cmd, slotNumber, "ช่องนี้ไม่มีรถที่เสกไว้");
        spawner.RemoveCar(index);
        return CommandResult.Ok(cmd, slotNumber, "เอารถออกแล้ว");
    }

    private bool CheckSlot(string cmd, int slotNumber, out CommandResult fail)
    {
        fail = null;
        if (manager == null || spawner == null)
        {
            fail = CommandResult.Fail(cmd, slotNumber, "ไม่พบ ParkingLotManager หรือ SlotCarSpawner");
            return false;
        }
        int count = manager.Slots.Count;
        if (slotNumber < 1 || slotNumber > count || manager.Slots[slotNumber - 1] == null)
        {
            fail = CommandResult.Fail(cmd, slotNumber, $"หมายเลขช่องต้องเป็น 1-{count}");
            return false;
        }
        return true;
    }

    private void PublishAck(CommandResult result)
    {
        string target = result.slot > 0 ? $" slot {result.slot}" : "";
        ParkingEventLog.Add(result.ok ? ParkingEventKind.ACK : ParkingEventKind.ERR, $"{result.cmd}{target} {(result.ok ? "accepted" : "failed")}");
        if (logToConsole) Log($"ผลคำสั่ง {result.cmd}: {(result.ok ? "OK" : "ไม่สำเร็จ")} - {result.message}");
        if (client != null) client.Publish(BaseTopic + "/ack", JsonUtility.ToJson(result), false);
    }

    private static ParsedCommand ParseCommand(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var c = new ParsedCommand();

        if (text.StartsWith("{"))
        {
            CommandMessage json;
            try { json = JsonUtility.FromJson<CommandMessage>(text); }
            catch { return null; }
            if (json == null) return null;
            c.cmd = json.cmd;
            c.slot = json.slot;
            c.target = json.target;
            c.value = json.value;
            c.state = json.state;
            c.hasState = text.Contains("\"state\"");
            return c;
        }

        // ข้อความสั้น เช่น "spawn 3", "speed 10", "speed 10x", "gate enter", "open exit"
        var parts = text.Split(new[] { ' ', '\t', ',', ':', '=' }, StringSplitOptions.RemoveEmptyEntries);
        c.cmd = parts[0];
        if (parts.Length > 1)
        {
            c.target = parts[1];
            string arg = parts[1].TrimEnd('x', 'X');
            if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
            {
                c.value = number;
                c.slot = Mathf.RoundToInt(number);
            }
        }
        return c;
    }

    private static bool TryParseOccupied(string text, out bool occupied)
    {
        string t = text.Trim().Trim('"').ToLowerInvariant();
        if (t.StartsWith("{"))
        {
            t = t.Replace(" ", "");
            if (t.Contains("\"occupied\":true") || t.Contains("\"occupied\":1")) { occupied = true; return true; }
            if (t.Contains("\"occupied\":false") || t.Contains("\"occupied\":0")) { occupied = false; return true; }
            occupied = false;
            return false;
        }
        switch (t)
        {
            case "1": case "true": case "on": case "occupied": case "car": case "busy":
                occupied = true; return true;
            case "0": case "false": case "off": case "free": case "empty": case "vacant":
                occupied = false; return true;
            default:
                occupied = false; return false;
        }
    }

    // ------------------------------------------------------------ HUD

    private void UpdateStatusText(bool force)
    {
        var state = State;
        string error = client != null ? client.LastError : null;
        if (!force && state == shownState && error == shownError) return;
        var previousState = shownState;
        shownState = state;
        shownError = error;
        LogConnectionEvent(previousState, state, error);

        string word;
        Color color;
        switch (state)
        {
            case MqttClientLite.ConnectionState.Connected: word = "ONLINE"; color = onlineColor; break;
            case MqttClientLite.ConnectionState.Connecting: word = "CONNECTING"; color = connectingColor; break;
            case MqttClientLite.ConnectionState.Reconnecting: word = "RECONNECTING"; color = connectingColor; break;
            default: word = "OFF"; color = offlineColor; break;
        }

        if (statusDot != null) statusDot.color = color;
        if (statusText != null)
        {
            string c = ColorUtility.ToHtmlStringRGB(color);
            string l = ColorUtility.ToHtmlStringRGB(labelColor);
            string right = client == null ? "not connected" : $"{brokerHost}:{brokerPort}";
            statusText.text = $"<align=left>MQTT  <color=#{c}>{word}</color><line-height=0>\n<align=right><color=#{l}>{right}</color>";
        }

        if (state == MqttClientLite.ConnectionState.Connected) Log($"เชื่อมต่อแล้ว ✓ {brokerHost}:{brokerPort}  topic: {BaseTopic}/#");
        else if (state == MqttClientLite.ConnectionState.Reconnecting && !string.IsNullOrEmpty(error)) Debug.LogWarning($"[MQTT] หลุดการเชื่อมต่อ กำลังต่อใหม่... ({error})");
    }

    private void LogConnectionEvent(MqttClientLite.ConnectionState previous, MqttClientLite.ConnectionState state, string error)
    {
        if (previous == state)
        {
            if (state == MqttClientLite.ConnectionState.Reconnecting && !string.IsNullOrEmpty(error))
                ParkingEventLog.Add(ParkingEventKind.WARN, "MQTT retry: " + error);
            return;
        }
        switch (state)
        {
            case MqttClientLite.ConnectionState.Connected:
                ParkingEventLog.Add(ParkingEventKind.NET, $"MQTT connected {brokerHost}:{brokerPort}");
                break;
            case MqttClientLite.ConnectionState.Connecting:
                ParkingEventLog.Add(ParkingEventKind.NET, $"MQTT connecting {brokerHost}:{brokerPort}");
                break;
            case MqttClientLite.ConnectionState.Reconnecting:
                ParkingEventLog.Add(ParkingEventKind.WARN, "MQTT lost, reconnecting" + (string.IsNullOrEmpty(error) ? "" : ": " + error));
                break;
            default:
                if ((int)previous >= 0) ParkingEventLog.Add(ParkingEventKind.NET, "MQTT offline");
                break;
        }
    }

    private void Log(string text)
    {
        if (logToConsole) Debug.Log("[MQTT] " + text);
    }

    // ------------------------------------------------------------ รูปแบบข้อความ (JSON)

    [Serializable]
    private class CommandMessage
    {
        public string cmd;
        public int slot;
        public string target;
        public float value = float.NaN;
        public bool state;
    }

    private class ParsedCommand
    {
        public string cmd;
        public int slot;
        public string target;
        public float value = float.NaN;
        public bool state;
        public bool hasState;
    }
}

[Serializable]
public class ParkingStatusMessage
{
    public string deviceId;
    public string timestamp;
    public int totalSlots;
    public int freeSlots;
    public int occupiedSlots;
    public int reservedSlots;
    public int availabilityPercent;
    // ชื่อฟิลด์เดียวกับ Data/parking_normal.json
    public bool slot1Occupied;
    public bool slot2Occupied;
    public bool slot3Occupied;
    public bool slot4Occupied;
    public bool vipOccupied;
    public bool accessibilityOccupied;
    public bool maternityOccupied;
    public string warningMessage;
    public float timeScale;
    public float overstayLimitHours;
    public SlotStatusMessage[] slots;
}

[Serializable]
public class SlotStatusMessage
{
    public int slot;
    public string name;
    public string type;
    public bool occupied;
    public bool reserved;
    public bool overstay;
    public int parkedSeconds;
    public string parkedTime;
    public bool spawnedByApp;
}

[Serializable]
public class CommandResult
{
    public bool ok;
    public string cmd;
    public int slot;
    public string message;

    public static CommandResult Ok(string cmd, int slot, string message) => new CommandResult { ok = true, cmd = cmd, slot = slot, message = message };
    public static CommandResult Fail(string cmd, int slot, string message) => new CommandResult { ok = false, cmd = cmd, slot = slot, message = message };
}
