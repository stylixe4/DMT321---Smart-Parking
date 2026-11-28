using System;
using System.Collections.Generic;
using UnityEngine;

// ประเภทเหตุการณ์ที่แสดงในแผง EVENT LOG
public enum ParkingEventKind
{
    SYS,   // ระบบ / เปลี่ยนความเร็วเวลา
    RX,    // รับข้อความจาก MQTT (บอร์ดจริง / คำสั่งภายนอก)
    TX,    // ส่งข้อความออก MQTT
    ACK,   // ผลคำสั่งสำเร็จ
    ERR,   // ผลคำสั่งไม่สำเร็จ
    WARN,  // คำเตือน เช่น จอดเกินเวลา / ข้อความผิดรูปแบบ
    NET,   // สถานะการเชื่อมต่อ MQTT และบอร์ด ESP8266
    PARK,  // รถเข้า/ออกช่องจอด
    GATE   // ไม้กั้นเปิด/ปิด
}

// ศูนย์รวมเหตุการณ์ของระบบ ให้สคริปต์ไหนก็ส่งเข้ามาได้: ParkingEventLog.Add(ParkingEventKind.RX, "sensor 3 = 1");
// แผง EventLogPanel จะดึงไปแสดง (เก็บไว้ล่าสุด Capacity รายการ)
// เรียกจาก Main Thread เท่านั้น (MqttParkingBridge ประมวลผลข้อความใน Update อยู่แล้ว)
public static class ParkingEventLog
{
    public struct Entry
    {
        public DateTime time;
        public ParkingEventKind kind;
        public string message;
    }

    public const int Capacity = 200;

    private static readonly List<Entry> entries = new List<Entry>(Capacity);

    public static event Action Changed;

    public static IReadOnlyList<Entry> Entries => entries;

    public static void Add(ParkingEventKind kind, string message)
    {
        if (entries.Count >= Capacity) entries.RemoveAt(0);
        entries.Add(new Entry { time = DateTime.Now, kind = kind, message = message ?? "" });
        Changed?.Invoke();
    }

    public static void Clear()
    {
        entries.Clear();
        Changed?.Invoke();
    }

    // ล้างของเก่าทุกครั้งที่กด Play (กรณีปิด Domain Reload ไว้)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        entries.Clear();
        Changed = null;
    }
}
