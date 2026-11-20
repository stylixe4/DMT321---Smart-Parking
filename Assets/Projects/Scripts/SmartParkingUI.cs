using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// หน้าจอ Dashboard ของลานจอด (สไตล์ SlimUI Modern Menu)
// ตัวเลข/สถานะทั้งหมดถูกส่งมาจาก ParkingLotManager — สคริปต์นี้มีหน้าที่ "แสดงผล" อย่างเดียว
// หมายเหตุ: ชื่อคลาสต้องตรงกับชื่อไฟล์ (SmartParkingUI.cs) ไม่อย่างนั้น Unity จะลากใส่ GameObject ไม่ได้
public class SmartParkingUI : MonoBehaviour
{
    // ==========================================
    // 1. Title & Availability UI
    // ==========================================
    [Header("1. Title & Availability Panel")]
    [SerializeField] private TMP_Text titleText;               // "SMART PARKING TWIN"
    [SerializeField] private TMP_Text availabilityText;        // ตัวเลขใหญ่ "71%"
    [SerializeField] private TMP_Text availabilityDetailText;  // "5 / 7 SLOTS FREE"
    [SerializeField] private RectTransform availabilityBarFill;// แถบเปอร์เซ็นต์ช่องว่าง
    [SerializeField] private TMP_Text clockText;               // นาฬิกาเวลาจริง
    [SerializeField] private Graphic liveIndicator;            // จุด LIVE กระพริบ

    // ==========================================
    // 2. Normal Slot Status UI (Normalslotstatus_panel)
    // ==========================================
    [Header("2. Normal Slot Status Panel")]
    [SerializeField] private TMP_Text titlenormalslots;   // "NORMAL SLOTS"
    [SerializeField] private TMP_Text Freeslottext;       // FREE        4
    [SerializeField] private TMP_Text Occupiedslottext;   // OCCUPIED    0

    // ==========================================
    // 3. Special Slot Status UI (Specialslotstatus_panel)
    // ==========================================
    [Header("3. Special Slot Status Panel")]
    [SerializeField] private TMP_Text titlespecialslots;      // "SPECIAL SLOTS"
    [SerializeField] private TMP_Text VIPslottext;            // V.I.P          FREE
    [SerializeField] private TMP_Text Accessibilityslottext;  // ACCESSIBILITY  RESERVED
    [SerializeField] private TMP_Text Maternityslottext;      // MATERNITY      OCCUPIED

    // ==========================================
    // 4. Reserved Slot UI (Reserves_Panel)
    // ==========================================
    [Header("4. Reserves Panel")]
    [SerializeField] private TMP_Text Reserves_text;      // RESERVED SLOTS  2

    // ==========================================
    // 5. Park Timer UI (Parktimer_panel)
    // ==========================================
    [Header("5. Park Timer Panel")]
    [SerializeField] private TMP_Text Timer_text;               // หัวข้อ (Normal)
    [SerializeField] private TMP_Text Timer_Special_text;       // หัวข้อ (Special)
    [SerializeField] private TMP_Text slot1_timer;
    [SerializeField] private TMP_Text slot2_timer;
    [SerializeField] private TMP_Text slot3_timer;
    [SerializeField] private TMP_Text slot4_timer;
    [SerializeField] private TMP_Text slot_vip_timer;
    [SerializeField] private TMP_Text slot_accessibility_timer;
    [SerializeField] private TMP_Text slot_maternity_timer;

    // ==========================================
    // 6. Warning UI (Warning_panel)
    // ==========================================
    [Header("6. Warning Panel")]
    [SerializeField] private Image Warning_panel;         // พื้นหลังของ Warning_panel
    [SerializeField] private TMP_Text Warning_text;       // หัวข้อ "WARNING"
    [SerializeField] private TMP_Text Warning_label;      // ข้อความแจ้งเตือน

    // ==========================================
    // 7. สีของ UI (ธีม SlimUI Modern Menu)
    // ==========================================
    [Header("7. Colors (Modern Menu theme)")]
    public Color accentColor = new Color(1f, 0.686f, 0f);
    public Color labelColor = new Color(0.62f, 0.66f, 0.72f);
    public Color freeColor = new Color(0.30f, 0.87f, 0.50f);
    public Color occupiedColor = new Color(0.97f, 0.44f, 0.44f);
    public Color reservedColor = new Color(0.22f, 0.74f, 0.97f);
    public Color overstayColor = new Color(1f, 0.23f, 0.23f);
    public Color alertPanelColor = new Color(0.62f, 0.07f, 0.07f, 0.92f);

    // ข้อมูลเวลาจอดของแต่ละช่อง (ส่งมาจาก ParkingLotManager)
    private struct SlotTimer
    {
        public bool occupied;
        public float seconds;
        public bool overstay;
    }

    private readonly SlotTimer[] normalTimers = new SlotTimer[4];
    private SlotTimer vipTimer, accessibilityTimer, maternityTimer;

    private float barTarget, barShown;
    private Color barColor;
    private Image barImage;
    private bool warningAlert;
    private Color warningNormalColor = new Color(0.055f, 0.063f, 0.082f, 0.86f);

    private void Awake()
    {
        if (Warning_panel != null) warningNormalColor = Warning_panel.color;
        if (availabilityBarFill != null) barImage = availabilityBarFill.GetComponent<Image>();
        barColor = freeColor;
        ResetAllUI();
    }

    private void Update()
    {
        UpdateTimerTexts();
        UpdateClock();
        AnimateAvailabilityBar();
        AnimateWarning();
        AnimateLiveIndicator();
    }

    // ------------------------------------------
    // ตัวช่วยจัดรูปแบบข้อความ
    // ------------------------------------------
    private static void SetText(TMP_Text target, string value)
    {
        if (target != null) target.text = value;
    }

    private static string Paint(string text, Color color)
    {
        return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";
    }

    // แถวแบบ "ชื่อชิดซ้าย ........ ค่าชิดขวา" ใน Text ตัวเดียว
    private string Row(string label, string value)
    {
        return $"<align=left>{Paint(label, labelColor)}<line-height=0>\n<align=right>{value}";
    }

    // ------------------------------------------
    // ฟังก์ชันรีเซ็ตค่าเริ่มต้นของ UI ทุกตัว
    // ------------------------------------------
    public void ResetAllUI()
    {
        SetText(titleText, $"SMART PARKING {Paint("TWIN", accentColor)}");
        SetText(availabilityText, $"--<size=50%>{Paint("%", accentColor)}</size>");
        SetText(availabilityDetailText, Paint("WAITING FOR SENSORS", labelColor));
        barTarget = barShown = 0f;
        ApplyBar();

        string dash = Paint("--", labelColor);
        SetText(Freeslottext, Row("FREE", dash));
        SetText(Occupiedslottext, Row("OCCUPIED", dash));

        SetText(VIPslottext, Row("V.I.P", dash));
        SetText(Accessibilityslottext, Row("ACCESSIBILITY", dash));
        SetText(Maternityslottext, Row("MATERNITY", dash));

        SetText(Reserves_text, Row("RESERVED SLOTS", dash));

        for (int i = 0; i < normalTimers.Length; i++) normalTimers[i] = default;
        vipTimer = accessibilityTimer = maternityTimer = default;
        UpdateTimerTexts();

        SetWarning("-", false);
    }

    // ------------------------------------------
    // 1. อัปเดตเปอร์เซ็นต์ช่องจอดว่าง (Availability_Panel)
    // ------------------------------------------
    public void UpdateAvailability(int freeSlots, int totalSlots)
    {
        if (totalSlots <= 0)
        {
            SetText(availabilityText, $"--<size=50%>{Paint("%", accentColor)}</size>");
            SetText(availabilityDetailText, Paint("NO SLOTS", labelColor));
            barTarget = 0f;
            return;
        }

        float ratio = (float)freeSlots / totalSlots;
        int percentage = Mathf.RoundToInt(ratio * 100f);
        barTarget = ratio;
        barColor = ratio > 0.5f ? freeColor : (ratio > 0.2f ? accentColor : occupiedColor);

        SetText(availabilityText, $"{percentage}<size=50%>{Paint("%", barColor)}</size>");
        SetText(availabilityDetailText, $"{freeSlots}{Paint($" / {totalSlots}  SLOTS FREE", labelColor)}");
    }

    // ------------------------------------------
    // 2. อัปเดตสถานะช่องจอดปกติ (Normalslotstatus_panel)
    // ------------------------------------------
    public void UpdateNormalSlots(int freeCount, int occupiedCount)
    {
        SetText(Freeslottext, Row("FREE", Paint(freeCount.ToString(), freeColor)));
        SetText(Occupiedslottext, Row("OCCUPIED", Paint(occupiedCount.ToString(), occupiedColor)));
    }

    // ------------------------------------------
    // 3. อัปเดตสถานะช่องจอดพิเศษ (Specialslotstatus_panel)
    //    แสดง OCCUPIED / RESERVED / FREE
    // ------------------------------------------
    public void UpdateSpecialSlots(bool isVipBusy, bool isAccessBusy, bool isMaternityBusy,
                                   bool isVipReserved = false, bool isAccessReserved = false, bool isMaternityReserved = false)
    {
        SetText(VIPslottext, Row("V.I.P", SpecialStatus(isVipBusy, isVipReserved)));
        SetText(Accessibilityslottext, Row("ACCESSIBILITY", SpecialStatus(isAccessBusy, isAccessReserved)));
        SetText(Maternityslottext, Row("MATERNITY", SpecialStatus(isMaternityBusy, isMaternityReserved)));
    }

    private string SpecialStatus(bool busy, bool reserved)
    {
        if (busy) return Paint("OCCUPIED", occupiedColor);
        if (reserved) return Paint("RESERVED", reservedColor);
        return Paint("FREE", freeColor);
    }

    // ------------------------------------------
    // 4. อัปเดตสถานะช่องจอง (Reserves_Panel)
    // ------------------------------------------
    public void UpdateReservedSlot(int reservedCount)
    {
        SetText(Reserves_text, Row("RESERVED SLOTS", Paint(reservedCount.ToString(), reservedColor)));
    }

    // ------------------------------------------
    // 5. เวลาจอดของแต่ละช่อง (Parktimer_panel) — ParkingLotManager เรียกทุกเฟรม
    // ------------------------------------------
    public void SetSlotTimer(ParkingSlotType type, int slotNumber, bool occupied, float seconds, bool overstay)
    {
        var timer = new SlotTimer { occupied = occupied, seconds = seconds, overstay = overstay };
        switch (type)
        {
            case ParkingSlotType.Normal:
                int index = slotNumber - 1; // Slot 1 = index 0
                if (index >= 0 && index < normalTimers.Length) normalTimers[index] = timer;
                break;
            case ParkingSlotType.VIP: vipTimer = timer; break;
            case ParkingSlotType.Accessibility: accessibilityTimer = timer; break;
            case ParkingSlotType.Maternity: maternityTimer = timer; break;
        }
    }

    private void UpdateTimerTexts()
    {
        SetText(slot1_timer, Row("SLOT 1", TimerValue(normalTimers[0])));
        SetText(slot2_timer, Row("SLOT 2", TimerValue(normalTimers[1])));
        SetText(slot3_timer, Row("SLOT 3", TimerValue(normalTimers[2])));
        SetText(slot4_timer, Row("SLOT 4", TimerValue(normalTimers[3])));
        SetText(slot_vip_timer, Row("V.I.P", TimerValue(vipTimer)));
        SetText(slot_accessibility_timer, Row("ACCESSIBILITY", TimerValue(accessibilityTimer)));
        SetText(slot_maternity_timer, Row("MATERNITY", TimerValue(maternityTimer)));
    }

    private string TimerValue(SlotTimer timer)
    {
        if (!timer.occupied) return Paint("--:--:--", labelColor * 0.75f);

        string time = FormatTime(timer.seconds);
        if (!timer.overstay) return Paint(time, Color.white);

        // จอดเกินเวลา: ตัวเลขกระพริบแดง-ขาว พร้อมป้าย OVERSTAY
        bool blinkOn = Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f;
        return $"<size=65%>{Paint("OVERSTAY  ", overstayColor)}</size>{Paint(time, blinkOn ? overstayColor : Color.white)}";
    }

    // แปลงวินาทีเป็นรูปแบบ ชั่วโมง:นาที:วินาที (HH:MM:SS)
    private static string FormatTime(float timeInSeconds)
    {
        int total = Mathf.FloorToInt(timeInSeconds);
        int hours = total / 3600;
        int minutes = (total % 3600) / 60;
        int seconds = total % 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
    }

    // ------------------------------------------
    // 6. แสดงข้อความแจ้งเตือน (Warning_panel)
    // ------------------------------------------
    public void SetWarning(string message, bool isAlert)
    {
        warningAlert = isAlert;
        bool empty = string.IsNullOrEmpty(message) || message == "-";
        SetText(Warning_label, empty ? Paint("ALL SYSTEMS NORMAL", freeColor) : message.ToUpperInvariant());

        if (!isAlert && Warning_panel != null) Warning_panel.color = warningNormalColor;
    }

    // ------------------------------------------
    // แอนิเมชันเล็กๆ ให้ Dashboard ดูมีชีวิต
    // ------------------------------------------
    private void AnimateWarning()
    {
        if (!warningAlert || Warning_panel == null) return;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
        Warning_panel.color = Color.Lerp(warningNormalColor, alertPanelColor, 0.45f + 0.55f * pulse);
    }

    private void AnimateAvailabilityBar()
    {
        barShown = Mathf.MoveTowards(barShown, barTarget, Time.unscaledDeltaTime * 1.5f);
        ApplyBar();
    }

    private void ApplyBar()
    {
        if (availabilityBarFill == null) return;
        availabilityBarFill.anchorMin = Vector2.zero;
        availabilityBarFill.anchorMax = new Vector2(Mathf.Clamp01(barShown), 1f);
        availabilityBarFill.offsetMin = Vector2.zero;
        availabilityBarFill.offsetMax = Vector2.zero;
        if (barImage != null) barImage.color = barColor;
    }

    private void UpdateClock()
    {
        if (clockText == null) return;
        DateTime now = DateTime.Now;
        string time = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        string date = now.ToString("dd MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        clockText.text = $"{time}\n<size=55%>{Paint(date, labelColor)}</size>";
    }

    private void AnimateLiveIndicator()
    {
        if (liveIndicator == null) return;
        Color c = freeColor;
        c.a = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f));
        liveIndicator.color = c;
    }
}
