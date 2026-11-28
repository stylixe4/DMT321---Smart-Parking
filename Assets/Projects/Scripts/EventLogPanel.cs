using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

// แผง EVENT LOG (ล่าสุดอยู่บนสุด) แบบเดียวกับหน้า Dashboard IoT
//   09:47:28  RX    sensor 3 = 1 (car)
//   09:47:29  PARK  Slot 3 car parked
//   09:47:29  TX    slot/3 occupied
// เหตุการณ์ MQTT/บอร์ด/ไม้กั้นมาจาก ParkingEventLog ส่วนรถเข้า-ออกช่อง/จอดเกินเวลา/เปลี่ยนความเร็วเวลา
// แผงนี้เฝ้าดูจาก ParkingLotManager เอง (จึงขึ้น log แม้ไม่ได้ต่อ MQTT)
// สร้างโดยเมนู Tools > Smart Parking > Build Modern Menu HUD
public class EventLogPanel : MonoBehaviour
{
    [Header("อ้างอิง")]
    public TMP_Text body;
    public ParkingLotManager manager;

    [Header("การแสดงผล")]
    [Range(5, 150)] public int maxLines = 60;
    [Tooltip("ตัดข้อความที่ยาวเกินจำนวนตัวอักษรนี้")]
    public int maxMessageLength = 70;
    [Tooltip("บันทึกรถเข้า-ออก / จอดเกินเวลา / จอง / ความเร็วเวลา จาก ParkingLotManager")]
    public bool watchSlots = true;

    [Header("สี")]
    public Color timeColor = new Color(0.50f, 0.53f, 0.58f);
    public Color messageColor = new Color(0.90f, 0.92f, 0.95f);
    public Color sysColor = new Color(0.69f, 0.75f, 0.77f);
    public Color rxColor = new Color(0.31f, 0.76f, 0.97f);
    public Color txColor = new Color(0.70f, 0.53f, 0.98f);
    public Color ackColor = new Color(0.40f, 0.73f, 0.42f);
    public Color errColor = new Color(0.94f, 0.33f, 0.31f);
    public Color warnColor = new Color(1.00f, 0.65f, 0.15f);
    public Color netColor = new Color(0.15f, 0.78f, 0.85f);
    public Color parkColor = new Color(1.00f, 0.84f, 0.31f);
    public Color gateColor = new Color(0.56f, 0.79f, 0.98f);

    private struct SlotState
    {
        public bool occupied, reserved, overstay;
    }

    private readonly StringBuilder sb = new StringBuilder(4096);
    private readonly List<SlotState> lastSlots = new List<SlotState>();
    private float lastTimeScale = -1f;
    private bool watching;
    private bool dirty = true;

    private void OnEnable()
    {
        ParkingEventLog.Changed += MarkDirty;
        dirty = true;
    }

    private void OnDisable()
    {
        ParkingEventLog.Changed -= MarkDirty;
    }

    private void Start()
    {
        if (manager == null) manager = FindFirstObjectByType<ParkingLotManager>();
        ParkingEventLog.Add(ParkingEventKind.SYS, "dashboard started");
    }

    private void Update()
    {
        if (watchSlots) WatchSlots();
        if (dirty) Rebuild();
    }

    private void MarkDirty() => dirty = true;

    // ปุ่ม CLEAR บนหัวแผง
    public void Clear()
    {
        ParkingEventLog.Clear();
        ParkingEventLog.Add(ParkingEventKind.SYS, "log cleared");
    }

    // ------------------------------------------------------------ เฝ้าดูช่องจอด

    private void WatchSlots()
    {
        if (manager == null) return;
        var slots = manager.Slots;
        if (slots == null) return;

        // รอบแรก: จำสถานะไว้เฉยๆ ไม่ขึ้น log
        if (!watching || lastSlots.Count != slots.Count)
        {
            lastSlots.Clear();
            for (int i = 0; i < slots.Count; i++) lastSlots.Add(Read(slots[i]));
            lastTimeScale = manager.TimeScale;
            watching = true;
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s == null) continue;
            var now = Read(s);
            var before = lastSlots[i];
            if (now.occupied == before.occupied && now.reserved == before.reserved && now.overstay == before.overstay) continue;
            lastSlots[i] = now;

            string name = s.DisplayName;
            if (now.occupied != before.occupied)
                ParkingEventLog.Add(ParkingEventKind.PARK, now.occupied ? $"{name} car parked" : $"{name} now free");
            if (now.overstay && !before.overstay)
                ParkingEventLog.Add(ParkingEventKind.WARN, $"{name} overstay > {manager.OverstayLimitHours:0.#}h");
            if (now.reserved != before.reserved)
                ParkingEventLog.Add(ParkingEventKind.SYS, now.reserved ? $"{name} reserved" : $"{name} reservation off");
        }

        if (!Mathf.Approximately(lastTimeScale, manager.TimeScale))
        {
            lastTimeScale = manager.TimeScale;
            ParkingEventLog.Add(ParkingEventKind.SYS, $"time speed x{manager.TimeScale:0.##}");
        }
    }

    // ใช้ค่า isReserved (ค่าที่ตั้งไว้) ไม่ใช่ IsReserved ที่จะเป็น false ตอนมีรถจอด เพื่อไม่ให้ขึ้น log ซ้ำทุกครั้งที่รถเข้า-ออก
    private static SlotState Read(ParkingSensor s)
    {
        if (s == null) return default;
        return new SlotState { occupied = s.IsOccupied, reserved = s.isReserved, overstay = s.IsOverstay };
    }

    // ------------------------------------------------------------ วาดข้อความ

    private void Rebuild()
    {
        dirty = false;
        if (body == null) return;

        var entries = ParkingEventLog.Entries;
        if (entries.Count == 0)
        {
            body.text = $"<color=#{Hex(timeColor)}>waiting for events...</color>";
            return;
        }

        string time = Hex(timeColor);
        string msgColor = Hex(messageColor);
        sb.Clear();
        int shown = 0;
        for (int i = entries.Count - 1; i >= 0 && shown < maxLines; i--, shown++)
        {
            var e = entries[i];
            string msg = e.message;
            if (msg.Length > maxMessageLength) msg = msg.Substring(0, maxMessageLength - 3) + "...";

            sb.Append("<color=#").Append(time).Append('>').Append(e.time.ToString("HH:mm:ss")).Append("</color>");
            sb.Append("<pos=5.4em><color=#").Append(Hex(KindColor(e.kind))).Append('>').Append(e.kind.ToString()).Append("</color>");
            sb.Append("<indent=9.4em><color=#").Append(msgColor).Append("><noparse>").Append(msg).Append("</noparse></color></indent>");
            sb.Append('\n');
        }
        body.text = sb.ToString();
    }

    private Color KindColor(ParkingEventKind kind)
    {
        switch (kind)
        {
            case ParkingEventKind.RX: return rxColor;
            case ParkingEventKind.TX: return txColor;
            case ParkingEventKind.ACK: return ackColor;
            case ParkingEventKind.ERR: return errColor;
            case ParkingEventKind.WARN: return warnColor;
            case ParkingEventKind.NET: return netColor;
            case ParkingEventKind.PARK: return parkColor;
            case ParkingEventKind.GATE: return gateColor;
            default: return sysColor;
        }
    }

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
}
