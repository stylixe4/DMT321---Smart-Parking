using UnityEngine;
using UnityEngine.InputSystem; // 1. เพิ่ม namespace สำหรับ New Input System
using TMPro;

public class SmartParkingUI : MonoBehaviour
{
    [Header("Availability & Slots")]
    public TextMeshProUGUI availabilityText;
    public TextMeshProUGUI freeSlotsText;
    public TextMeshProUGUI occupiedSlotsText;

    [Header("Timers")]
    public TextMeshProUGUI slot1TimerText;
    public TextMeshProUGUI slot2TimerText;

    [Header("Warning Panel")]
    public TextMeshProUGUI warningLabelText;

    // ตัวแปรสำหรับทดสอบเก็บข้อมูล
    private int totalSlots = 10;
    private int occupiedSlots = 3;

    void Start()
    {
        // อัปเดตข้อมูลครั้งแรกเมื่อเริ่มเกม
        UpdateParkingData();
    }

    void Update()
    {
        // 2. เปลี่ยนการตรวจจับปุ่ม Spacebar มาใช้ Keyboard.current
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            occupiedSlots++;
            UpdateParkingData();
        }
    }

    // ฟังก์ชันสำหรับอัปเดตข้อความบนจอ UI
    public void UpdateParkingData()
    {
        int freeSlots = totalSlots - occupiedSlots;
        float availabilityPercentage = ((float)freeSlots / totalSlots) * 100f;

        if (availabilityText != null)
            availabilityText.text = $"Availability: {availabilityPercentage:F0}%";

        if (freeSlotsText != null)
            freeSlotsText.text = $"Free: {freeSlots}";

        if (occupiedSlotsText != null)
            occupiedSlotsText.text = $"Occupied: {occupiedSlots}";
    }

    // ฟังก์ชันเปลี่ยนเวลาตัวจับเวลา (ตัวอย่างรูปแบบ 00:00)
    public void UpdateSlotTimer(TextMeshProUGUI timerText, float timeInSeconds)
    {
        int minutes = Mathf.FloorToInt(timeInSeconds / 60);
        int seconds = Mathf.FloorToInt(timeInSeconds % 60);
        
        if (timerText != null)
            timerText.text = string.Format("Slot: {0:00}:{1:00}", minutes, seconds);
    }
}