using System.Collections.Generic;
using UnityEngine;

// ตัวกลางเชื่อม "ช่องจอดในเกม" (ParkingSensor) เข้ากับ "หน้าจอ UI" (SmartParkingUI)
// - ฟังสถานะจากทุกช่อง เมื่อมีรถเข้า/ออก หรือมีการจอง จะคำนวณใหม่แล้วส่งค่าไปแสดงที่ UI
// - นับเวลาจอดของแต่ละช่อง ถ้าจอดเกินเวลาที่กำหนด (ค่าเริ่มต้น 2 ชั่วโมง) ไฟช่องนั้นจะกระพริบ
public class ParkingLotManager : MonoBehaviour
{
    [Header("UI ที่ใช้แสดงผล")]
    [SerializeField] private SmartParkingUI ui;

    [Header("ช่องจอดทั้งหมด (ลาก SlotSensor มาใส่ / ถ้าเว้นว่างจะค้นหาในฉากให้อัตโนมัติ)")]
    [SerializeField] private List<ParkingSensor> slots = new List<ParkingSensor>();

    [Header("เวลาจอด")]
    [Tooltip("จอดเกินกี่ชั่วโมงถึงจะเตือน (ไฟช่องนั้นกระพริบ)")]
    [SerializeField] private float overstayLimitHours = 2f;
    [Tooltip("ตัวคูณเวลาสำหรับทดสอบ: 1 = เวลาจริง, 60 = 1 วินาทีจริงเท่ากับ 1 นาที, 3600 = 1 วินาทีจริงเท่ากับ 1 ชั่วโมง")]
    [SerializeField] private float timeScale = 1f;

    [Header("ข้อความแจ้งเตือน")]
    [Tooltip("เมื่อช่องว่างเหลือเท่านี้หรือน้อยกว่า จะขึ้นเตือนว่าใกล้เต็ม")]
    [SerializeField] private int almostFullThreshold = 1;
    [SerializeField] private string fullMessage = "PARKING FULL";
    [SerializeField] private string almostFullMessage = "Almost full: {0} slot left";
    [SerializeField] private string overstayMessage = "Overstay over {0}h: {1}";

    // ---------- ข้อมูลสำหรับระบบอื่น (ปุ่มเร่งเวลา, MQTT) ----------
    public IReadOnlyList<ParkingSensor> Slots => slots;
    public float TimeScale => timeScale;
    public float OverstayLimitHours => overstayLimitHours;
    public string CurrentWarning { get; private set; } = "-";

    // ปรับความเร็วเวลาจอดรถ (1 = ปกติ, 5 = เร็วขึ้น 5 เท่า ...) — ใช้กับปุ่ม TIME SPEED และคำสั่ง MQTT
    public void SetTimeScale(float scale)
    {
        timeScale = Mathf.Clamp(scale, 0f, 1000f);
    }

    public void ResetTimeScale()
    {
        timeScale = 1f;
    }

    private void Awake()
    {
        if (ui == null) ui = FindFirstObjectByType<SmartParkingUI>();

        slots.RemoveAll(s => s == null);
        if (slots.Count == 0)
        {
            slots.AddRange(FindObjectsByType<ParkingSensor>(FindObjectsSortMode.None));
        }

        if (ui == null) Debug.LogWarning("[ParkingLotManager] ไม่พบ SmartParkingUI ในฉาก", this);
    }

    private void OnEnable()
    {
        foreach (var slot in slots)
        {
            if (slot != null) slot.StatusChanged += OnSlotChanged;
        }
    }

    private void OnDisable()
    {
        foreach (var slot in slots)
        {
            if (slot != null) slot.StatusChanged -= OnSlotChanged;
        }
    }

    private void Start()
    {
        RefreshUI();
    }

    private void Update()
    {
        float dt = Time.deltaTime * Mathf.Max(0f, timeScale);
        float limitSeconds = overstayLimitHours * 3600f;
        bool overstayChanged = false;

        foreach (var slot in slots)
        {
            if (slot == null) continue;

            slot.AddParkedTime(dt);

            bool overstay = slot.IsOccupied && slot.ParkedSeconds >= limitSeconds;
            if (overstay != slot.IsOverstay)
            {
                slot.SetOverstay(overstay); // เริ่ม/หยุดไฟกระพริบ
                overstayChanged = true;
            }

            if (ui != null) ui.SetSlotTimer(slot.slotType, slot.slotNumber, slot.IsOccupied, slot.ParkedSeconds, overstay);
        }

        if (overstayChanged) RefreshUI(); // อัปเดตข้อความเตือน
    }

    private void OnSlotChanged(ParkingSensor slot)
    {
        RefreshUI();
    }

    // คำนวณสถานะลานจอดทั้งหมด แล้วส่งไปแสดงผลที่ UI
    public void RefreshUI()
    {
        if (ui == null) return;

        int total = 0, totalFree = 0, reservedCount = 0;
        int normalFree = 0, normalOccupied = 0;
        bool vipBusy = false, accessBusy = false, maternityBusy = false;
        bool vipReserved = false, accessReserved = false, maternityReserved = false;
        var overstaySlots = new List<string>();

        foreach (var slot in slots)
        {
            if (slot == null) continue;

            bool occupied = slot.IsOccupied;
            bool reserved = slot.IsReserved; // จองอยู่และยังไม่มีรถจอด

            total++;
            if (reserved) reservedCount++;
            if (!occupied && !reserved) totalFree++;
            if (slot.IsOverstay) overstaySlots.Add(slot.DisplayName);

            switch (slot.slotType)
            {
                case ParkingSlotType.Normal:
                    if (occupied) normalOccupied++;
                    else if (!reserved) normalFree++;
                    break;
                case ParkingSlotType.VIP:
                    vipBusy |= occupied; vipReserved |= reserved;
                    break;
                case ParkingSlotType.Accessibility:
                    accessBusy |= occupied; accessReserved |= reserved;
                    break;
                case ParkingSlotType.Maternity:
                    maternityBusy |= occupied; maternityReserved |= reserved;
                    break;
            }
        }

        ui.UpdateAvailability(totalFree, total);
        ui.UpdateNormalSlots(normalFree, normalOccupied);
        ui.UpdateSpecialSlots(vipBusy, accessBusy, maternityBusy, vipReserved, accessReserved, maternityReserved);
        ui.UpdateReservedSlot(reservedCount);

        // ลำดับความสำคัญของคำเตือน: จอดเกินเวลา > เต็ม > ใกล้เต็ม
        bool alert = true;
        if (overstaySlots.Count > 0)
            CurrentWarning = string.Format(overstayMessage, overstayLimitHours, string.Join(", ", overstaySlots));
        else if (total > 0 && totalFree == 0)
            CurrentWarning = fullMessage;
        else if (total > 0 && totalFree <= almostFullThreshold)
            CurrentWarning = string.Format(almostFullMessage, totalFree);
        else
        {
            CurrentWarning = "-";
            alert = false;
        }
        ui.SetWarning(CurrentWarning, alert);
    }

    // ให้ปุ่ม UI หรือระบบอื่นเรียกจองช่องได้ เช่น ReserveSlot(ParkingSlotType.VIP, 0)
    public void ReserveSlot(ParkingSlotType type, int slotNumber, bool reserve = true)
    {
        foreach (var slot in slots)
        {
            if (slot == null || slot.slotType != type) continue;
            if (type == ParkingSlotType.Normal && slot.slotNumber != slotNumber) continue;
            slot.SetReserved(reserve);
            return;
        }
    }
}
