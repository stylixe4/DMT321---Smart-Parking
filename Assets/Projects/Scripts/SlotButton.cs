using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ปุ่ม Slot 1-7: กด = เสกรถมาจอด / กดอีกที = รถหายไป
// ปุ่มจะเปลี่ยนสีและข้อความตามสถานะของช่องจอดเอง
[RequireComponent(typeof(Button))]
public class SlotButton : MonoBehaviour
{
    public SlotCarSpawner spawner;
    [Min(1)] public int slotNumber = 1;

    [Header("ส่วนประกอบของปุ่ม")]
    public Image fill;          // พื้นปุ่ม
    public Image frame;         // กรอบปุ่ม
    public TMP_Text label;      // "SLOT 1"
    public TMP_Text typeLabel;  // "NORMAL" / "V.I.P" ...
    public TMP_Text status;     // "ADD CAR" / "REMOVE CAR" / "OCCUPIED"

    [Header("สี (ธีม Modern Menu)")]
    public Color accentColor = new Color(1f, 0.686f, 0f);
    public Color idleFill = new Color(0.08f, 0.09f, 0.11f, 0.92f);
    public Color idleFrame = new Color(1f, 1f, 1f, 0.45f);
    public Color labelColor = new Color(0.62f, 0.66f, 0.72f);
    public Color reservedColor = new Color(0.22f, 0.74f, 0.97f);
    public Color occupiedColor = new Color(0.97f, 0.44f, 0.44f);

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (label != null) label.text = $"SLOT {slotNumber}";
    }

    private void Start()
    {
        if (typeLabel != null && spawner != null) typeLabel.text = TypeName(spawner.GetSlot(slotNumber - 1));
    }

    // ผูกกับ Button.onClick (คลิกขวาที่ Component นี้ใน Inspector > Toggle ก็ทดสอบได้ตอน Play)
    [ContextMenu("Toggle")]
    public void Toggle()
    {
        if (spawner != null) spawner.ToggleSlotByNumber(slotNumber);

        // ไม่ให้ปุ่มค้างโฟกัส กันปุ่ม Space/Enter/ลูกศร ไปกดซ้ำตอนขับรถ
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void Update()
    {
        if (spawner == null) return;

        int index = slotNumber - 1;
        var slot = spawner.GetSlot(index);
        bool hasMyCar = spawner.HasSpawnedCar(index);
        bool occupiedByOther = slot != null && slot.IsOccupied && !hasMyCar;
        bool reserved = slot != null && slot.IsReserved;

        if (button != null) button.interactable = slot != null && !occupiedByOther;

        if (hasMyCar)
            Apply(new Color(accentColor.r, accentColor.g, accentColor.b, 0.32f), accentColor, "REMOVE CAR", accentColor);
        else if (occupiedByOther)
            Apply(new Color(0.55f, 0.1f, 0.1f, 0.6f), occupiedColor, "OCCUPIED", occupiedColor);
        else if (reserved)
            Apply(idleFill, reservedColor, "RESERVED", reservedColor);
        else
            Apply(idleFill, idleFrame, "ADD CAR", labelColor);
    }

    private void Apply(Color fillColor, Color frameColor, string statusText, Color statusColor)
    {
        if (fill != null) fill.color = fillColor;
        if (frame != null) frame.color = frameColor;
        if (status != null)
        {
            status.text = statusText;
            status.color = statusColor;
        }
    }

    public static string TypeName(ParkingSensor slot)
    {
        if (slot == null) return "-";
        switch (slot.slotType)
        {
            case ParkingSlotType.VIP: return "V.I.P";
            case ParkingSlotType.Accessibility: return "ACCESSIBLE";
            case ParkingSlotType.Maternity: return "MATERNITY";
            default: return "NORMAL";
        }
    }
}
