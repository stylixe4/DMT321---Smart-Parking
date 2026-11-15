using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SmartParkingUIManager : MonoBehaviour
{
    // ==========================================
    // 1. Title & Availability UI
    // ==========================================
    [Header("1. Title & Availability Panel")]
    [SerializeField] private TMP_Text titleText;          // Text ใน Title_Panel ("SMART PARKING TWIN")
    [SerializeField] private TMP_Text availabilityText;   // Text ใน Availability_Panel ("Availability: --%")

    // ==========================================
    // 2. Normal Slot Status UI (Normalslotstatus_panel)
    // ==========================================
    [Header("2. Normal Slot Status Panel")]
    [SerializeField] private TMP_Text titlenormalslots;   // "Normal slots"
    [SerializeField] private TMP_Text Freeslottext;       // "Free: --"
    [SerializeField] private TMP_Text Occupiedslottext;   // "Occupied: --"

    // ==========================================
    // 3. Special Slot Status UI (Specialslotstatus_panel)
    // ==========================================
    [Header("3. Special Slot Status Panel")]
    [SerializeField] private TMP_Text titlespecialslots;      // "Special slots"
    [SerializeField] private TMP_Text VIPslottext;            // "V.I.P slot: --"
    [SerializeField] private TMP_Text Accessibilityslottext;  // "Accessibility slot: --"
    [SerializeField] private TMP_Text Maternityslottext;      // "Maternity slot: --"

    // ==========================================
    // 4. Reserved Slot UI (Reserves_Panel)
    // ==========================================
    [Header("4. Reserves Panel")]
    [SerializeField] private TMP_Text Reserves_text;      // "Reserved slot: --"

    // ==========================================
    // 5. Park Timer UI (Parktimer_panel)
    // ==========================================
    [Header("5. Park Timer Panel")]
    [SerializeField] private TMP_Text Timer_text;               // หัวข้อ "Park timer" (Normal)
    [SerializeField] private TMP_Text Timer_Special_text;       // หัวข้อ "Park timer" (Special)
    [SerializeField] private TMP_Text slot1_timer;              // "Slot 1: --:--"
    [SerializeField] private TMP_Text slot2_timer;              // "Slot 2: --:--"
    [SerializeField] private TMP_Text slot3_timer;              // "Slot 3: --:--"
    [SerializeField] private TMP_Text slot4_timer;              // "Slot 4: --:--"
    [SerializeField] private TMP_Text slot_vip_timer;           // "V.I.P slot: --:--"
    [SerializeField] private TMP_Text slot_accessibility_timer; // "Accessibility slot: --:--"
    [SerializeField] private TMP_Text slot_maternity_timer;     // "Maternity slot: --:--"

    // ==========================================
    // 6. Warning UI (Warning_panel)
    // ==========================================
    [Header("6. Warning Panel")]
    [SerializeField] private Image Warning_panel;         // พื้นหลังของ Warning_panel
    [SerializeField] private TMP_Text Warning_text;       // หัวข้อ "Warning"
    [SerializeField] private TMP_Text Warning_label;      // ข้อความแจ้งเตือน "-"

    // ตัวแปรเก็บสถานะและเวลาจอดรถ (วินาที)
    private float[] normalTimers = new float[4];
    private bool[] normalOccupied = new bool[4];

    private float vipTimer = 0f;
    private bool vipOccupied = false;

    private float accessibilityTimer = 0f;
    private bool accessibilityOccupied = false;

    private float maternityTimer = 0f;
    private bool maternityOccupied = false;

    private void Start()
    {
        // ตั้งค่าเริ่มต้นให้ตรงกับหน้าจอในภาพ
        ResetAllUI();
    }

    private void Update()
    {
        // อัปเดตตัวจับเวลาจอดรถทุกเฟรม
        UpdateAllTimers();
    }

    // ------------------------------------------
    // ฟังก์ชันรีเซ็ตค่าเริ่มต้นของ UI ทุกตัว
    // ------------------------------------------
    public void ResetAllUI()
    {
        if (titleText != null) titleText.text = "SMART PARKING TWIN";
        if (availabilityText != null) availabilityText.text = "Availability: --%";

        if (Freeslottext != null) Freeslottext.text = "Free: --";
        if (Occupiedslottext != null) Occupiedslottext.text = "Occupied: --";

        if (VIPslottext != null) VIPslottext.text = "V.I.P slot: --";
        if (Accessibilityslottext != null) Accessibilityslottext.text = "Accessibility slot: --";
        if (Maternityslottext != null) Maternityslottext.text = "Maternity slot: --";

        if (Reserves_text != null) Reserves_text.text = "Reserved slot: --";

        slot1_timer.text = "Slot 1: --:--";
        slot2_timer.text = "Slot 2: --:--";
        slot3_timer.text = "Slot 3: --:--";
        slot4_timer.text = "Slot 4: --:--";
        slot_vip_timer.text = "V.I.P slot: --:--";
        slot_accessibility_timer.text = "Accessibility slot: --:--";
        slot_maternity_timer.text = "Maternity slot: --:--";

        SetWarning("-", false);
    }

    // ------------------------------------------
    // 1. อัปเดตเปอร์เซ็นต์ช่องจอดว่าง (Availability_Panel)
    // ------------------------------------------
    public void UpdateAvailability(int freeSlots, int totalSlots)
    {
        if (totalSlots <= 0)
        {
            availabilityText.text = "Availability: --%";
            return;
        }

        float percentage = ((float)freeSlots / totalSlots) * 100f;
        availabilityText.text = $"Availability: {percentage:F0}%";
    }

    // ------------------------------------------
    // 2. อัปเดตสถานะช่องจอดปกติ (Normalslotstatus_panel)
    // ------------------------------------------
    public void UpdateNormalSlots(int freeCount, int occupiedCount)
    {
        Freeslottext.text = $"Free: {freeCount}";
        Occupiedslottext.text = $"Occupied: {occupiedCount}";
    }

    // ------------------------------------------
    // 3. อัปเดตสถานะช่องจอดพิเศษ (Specialslotstatus_panel)
    // ------------------------------------------
    public void UpdateSpecialSlots(bool isVipBusy, bool isAccessBusy, bool isMaternityBusy)
    {
        vipOccupied = isVipBusy;
        accessibilityOccupied = isAccessBusy;
        maternityOccupied = isMaternityBusy;

        VIPslottext.text = $"V.I.P slot: {(isVipBusy ? "Occupied" : "Free")}";
        Accessibilityslottext.text = $"Accessibility slot: {(isAccessBusy ? "Occupied" : "Free")}";
        Maternityslottext.text = $"Maternity slot: {(isMaternityBusy ? "Occupied" : "Free")}";

        // รีเซ็ตเวลาถ้าช่องว่าง
        if (!vipOccupied) vipTimer = 0f;
        if (!accessibilityOccupied) accessibilityTimer = 0f;
        if (!maternityOccupied) maternityTimer = 0f;
    }

    // ------------------------------------------
    // 4. อัปเดตสถานะช่องจอง (Reserves_Panel)
    // ------------------------------------------
    public void UpdateReservedSlot(int reservedCount)
    {
        Reserves_text.text = $"Reserved slot: {reservedCount}";
    }

    // ------------------------------------------
    // 5. ควบคุมสถานะและเวลาของแต่ละช่อง (Parktimer_panel)
    // ------------------------------------------
    public void SetNormalSlotOccupied(int slotIndex, bool isOccupied)
    {
        // slotIndex: 0 = Slot 1, 1 = Slot 2, 2 = Slot 3, 3 = Slot 4
        if (slotIndex < 0 || slotIndex >= 4) return;

        normalOccupied[slotIndex] = isOccupied;
        if (!isOccupied)
        {
            normalTimers[slotIndex] = 0f; // รีเซ็ตเวลาเมื่อรถออก
        }
    }

    private void UpdateAllTimers()
    {
        // อัปเดตเวลา Normal Slots (Slot 1 - 4)
        for (int i = 0; i < 4; i++)
        {
            if (normalOccupied[i])
            {
                normalTimers[i] += Time.deltaTime;
            }
        }

        slot1_timer.text = $"Slot 1: {(normalOccupied[0] ? FormatTime(normalTimers[0]) : "--:--")}";
        slot2_timer.text = $"Slot 2: {(normalOccupied[1] ? FormatTime(normalTimers[1]) : "--:--")}";
        slot3_timer.text = $"Slot 3: {(normalOccupied[2] ? FormatTime(normalTimers[2]) : "--:--")}";
        slot4_timer.text = $"Slot 4: {(normalOccupied[3] ? FormatTime(normalTimers[3]) : "--:--")}";

        // อัปเดตเวลา Special Slots
        if (vipOccupied) vipTimer += Time.deltaTime;
        if (accessibilityOccupied) accessibilityTimer += Time.deltaTime;
        if (maternityOccupied) maternityTimer += Time.deltaTime;

        slot_vip_timer.text = $"V.I.P slot: {(vipOccupied ? FormatTime(vipTimer) : "--:--")}";
        slot_accessibility_timer.text = $"Accessibility slot: {(accessibilityOccupied ? FormatTime(accessibilityTimer) : "--:--")}";
        slot_maternity_timer.text = $"Maternity slot: {(maternityOccupied ? FormatTime(maternityTimer) : "--:--")}";
    }

    // แปลงวินาทีเป็นรูปแบบ นาที:วินาที (MM:SS)
    private string FormatTime(float timeInSeconds)
    {
        int minutes = Mathf.FloorToInt(timeInSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60f);
        return $"{minutes:00}:{seconds:00}";
    }

    // ------------------------------------------
    // 6. แสดงข้อความแจ้งเตือน (Warning_panel)
    // ------------------------------------------
    public void SetWarning(string message, bool isAlert)
    {
        Warning_label.text = string.IsNullOrEmpty(message) ? "-" : message;

        if (Warning_panel != null)
        {
            // เปลี่ยนความเข้มสีพื้นหลังเมื่อมีแจ้งเตือน
            Warning_panel.color = isAlert
                ? new Color(0.8f, 0.1f, 0.1f, 0.85f)  // สีแดงเข้มเมื่อมี Warning
                : new Color(0.5f, 0.15f, 0.15f, 0.45f); // สีแดงจางเมื่อปกติ
        }
    }
}