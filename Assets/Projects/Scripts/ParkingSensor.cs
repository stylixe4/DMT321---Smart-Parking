using System;
using System.Collections.Generic;
using UnityEngine;

// ประเภทช่องจอด ใช้บอก ParkingLotManager ว่าช่องนี้ต้องไปแสดงที่ส่วนไหนของ UI
public enum ParkingSlotType
{
    Normal,         // ช่องจอดปกติ (Slot 1-4)
    VIP,            // V.I.P slot
    Accessibility,  // Accessibility slot (ผู้พิการ)
    Maternity       // Maternity slot (หญิงตั้งครรภ์)
}

public class ParkingSensor : MonoBehaviour
{
    [Header("ข้อมูลช่องจอด (ใช้เชื่อมกับ UI)")]
    public ParkingSlotType slotType = ParkingSlotType.Normal;
    [Tooltip("ลำดับของช่องจอดปกติ 1-4 (ตรงกับ Slot 1-4 ใน Park timer) — ช่องพิเศษไม่ต้องใส่")]
    public int slotNumber = 1;

    [Header("ตั้งค่าหลอดไฟและสี")]
    public Renderer lightRenderer;
    public Material greenMaterial; // สีเขียว = ว่าง
    public Material redMaterial;   // สีแดง = มีรถจอด
    public Material blueMaterial;  // สีฟ้า = ถูกจองแล้ว

    [Header("ไฟกระพริบเมื่อจอดเกินเวลา")]
    [Tooltip("ความเร็วกระพริบ (วินาทีต่อครั้ง)")]
    public float blinkInterval = 0.5f;
    [Tooltip("สีตอนไฟดับระหว่างกระพริบ — ถ้าเว้นว่าง หลอดไฟจะหายไปแทน")]
    public Material blinkOffMaterial;

    [Header("สถานะการจอง")]
    public bool isReserved = false; // ติ๊กถูกใน Inspector เพื่อทดสอบสถานะ "จอง" (กดตอน Play ได้ UI จะอัปเดตทันที)

    // แจ้งเตือนทุกครั้งที่สถานะช่องนี้เปลี่ยน (มีรถเข้า/ออก, จอง/ยกเลิกจอง)
    public event Action<ParkingSensor> StatusChanged;

    // เก็บ Collider ของรถที่อยู่ในช่อง (นับเป็นชุด เผื่อมีรถมากกว่า 1 คันชนเซนเซอร์พร้อมกัน)
    private readonly HashSet<Collider> carsInside = new HashSet<Collider>();

    private float blinkTimer;
    private bool blinkOn = true;

    public bool IsOccupied => carsInside.Count > 0;
    public bool IsReserved => isReserved && !IsOccupied;

    // เวลาที่รถคันปัจจุบันจอดอยู่ (วินาที) — ParkingLotManager เป็นคนเพิ่มเวลาให้
    public float ParkedSeconds { get; private set; }
    // จอดเกินเวลาที่กำหนดหรือยัง (ถ้าใช่ ไฟจะกระพริบ)
    public bool IsOverstay { get; private set; }

    private void Start()
    {
        UpdateLightColor();
    }

    private void Update()
    {
        if (!IsOverstay || lightRenderer == null) return;

        blinkTimer += Time.deltaTime;
        if (blinkTimer >= blinkInterval)
        {
            blinkTimer -= blinkInterval;
            blinkOn = !blinkOn;
            ApplyBlink();
        }
    }

    // รถที่ถูกลบ/ปิดไปขณะจอดอยู่ (เช่น กดปุ่ม Slot ให้รถหาย) Unity จะไม่เรียก OnTriggerExit
    // จึงต้องเช็กแล้วเอาออกเอง ไม่อย่างนั้นช่องจะค้างสถานะ "มีรถ"
    private void FixedUpdate()
    {
        if (carsInside.Count == 0) return;
        int removed = carsInside.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
        if (removed == 0) return;

        if (!IsOccupied)
        {
            ParkedSeconds = 0f;
            IsOverstay = false;
        }
        UpdateLightColor();
        NotifyChanged();
    }

    // ทำงานเมื่อมีรถขับเข้ามาในช่อง
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Car")) return;

        bool wasEmpty = !IsOccupied;
        if (carsInside.Add(other))
        {
            if (wasEmpty) ParkedSeconds = 0f; // เริ่มนับเวลาจอดใหม่
            isReserved = false; // เมื่อรถที่จองขับเข้ามาจอดแล้ว ให้เคลียร์สถานะจองออก
            UpdateLightColor();
            NotifyChanged();
        }
    }

    // ทำงานเมื่อรถขับออกจากช่อง
    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Car")) return;

        if (carsInside.Remove(other))
        {
            if (!IsOccupied)
            {
                ParkedSeconds = 0f;
                IsOverstay = false; // รถออกแล้ว หยุดกระพริบ
            }
            UpdateLightColor();
            NotifyChanged();
        }
    }

    // เพิ่มเวลาจอด (เรียกจาก ParkingLotManager ทุกเฟรม)
    public void AddParkedTime(float seconds)
    {
        if (IsOccupied) ParkedSeconds += seconds;
    }

    // เปิด/ปิดโหมดจอดเกินเวลา (ไฟกระพริบ)
    public void SetOverstay(bool overstay)
    {
        if (IsOverstay == overstay) return;
        IsOverstay = overstay;
        blinkTimer = 0f;
        blinkOn = true;
        UpdateLightColor();
    }

    // ฟังก์ชันอัปเดตสีไฟตามลำดับความสำคัญ
    public void UpdateLightColor()
    {
        if (lightRenderer == null) return;

        lightRenderer.enabled = true;

        if (IsOccupied)
        {
            lightRenderer.sharedMaterial = redMaterial;   // 1. มีรถจอด = สีแดง (ถ้าจอดเกินเวลาจะกระพริบ)
        }
        else if (isReserved)
        {
            lightRenderer.sharedMaterial = blueMaterial;  // 2. ถูกจองไว้ = สีฟ้า
        }
        else
        {
            lightRenderer.sharedMaterial = greenMaterial; // 3. ว่างปกติ = สีเขียว
        }
    }

    private void ApplyBlink()
    {
        if (blinkOffMaterial != null)
        {
            lightRenderer.enabled = true;
            lightRenderer.sharedMaterial = blinkOn ? redMaterial : blinkOffMaterial;
        }
        else
        {
            lightRenderer.enabled = blinkOn;
        }
    }

    // ฟังก์ชันสำหรับให้ปุ่ม UI หรือระบบอื่นเรียกใช้เพื่อสั่ง "จอง" ช่องนี้
    public void SetReserved(bool reserveStatus)
    {
        if (!IsOccupied) // จองได้เฉพาะตอนที่ยังไม่มีรถจอดอยู่เท่านั้น
        {
            isReserved = reserveStatus;
            UpdateLightColor();
            NotifyChanged();
        }
    }

    // ชื่อที่ใช้แสดงผล เช่น "Slot 1" หรือ "V.I.P slot"
    public string DisplayName
    {
        get
        {
            switch (slotType)
            {
                case ParkingSlotType.VIP: return "V.I.P slot";
                case ParkingSlotType.Accessibility: return "Accessibility slot";
                case ParkingSlotType.Maternity: return "Maternity slot";
                default: return $"Slot {slotNumber}";
            }
        }
    }

    private void NotifyChanged()
    {
        StatusChanged?.Invoke(this);
    }

    // ติ๊ก isReserved ใน Inspector ระหว่าง Play แล้วไฟกับ UI จะอัปเดตตาม
    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        UpdateLightColor();
        NotifyChanged();
    }
}
