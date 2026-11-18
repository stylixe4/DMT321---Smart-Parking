using UnityEngine;

public class ParkingSensor : MonoBehaviour
{
    [Header("ตั้งค่าหลอดไฟและสี")]
    public Renderer lightRenderer;
    public Material greenMaterial; // สีเขียว = ว่าง
    public Material redMaterial;   // สีแดง = มีรถจอด
    public Material blueMaterial;  // สีฟ้า = ถูกจองแล้ว

    [Header("สถานะการจอง")]
    public bool isReserved = false; // ติ๊กถูกใน Inspector เพื่อทดสอบสถานะ "จอง"

    private bool isOccupied = false; // เช็กว่ามีรถจอดอยู่จริงไหม

    private void Start()
    {
        UpdateLightColor();
    }

    // ทำงานเมื่อมีรถขับเข้ามาในช่อง
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Car"))
        {
            isOccupied = true;
            isReserved = false; // เมื่อรถที่จองขับเข้ามาจอดแล้ว ให้เคลียร์สถานะจองออก
            UpdateLightColor();
        }
    }

    // ทำงานเมื่อรถขับออกจากช่อง
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Car"))
        {
            isOccupied = false;
            UpdateLightColor();
        }
    }

    // ฟังก์ชันอัปเดตสีไฟตามลำดับความสำคัญ
    public void UpdateLightColor()
    {
        if (lightRenderer == null) return;

        if (isOccupied)
        {
            lightRenderer.material = redMaterial;   // 1. มีรถจอด = สีแดง
        }
        else if (isReserved)
        {
            lightRenderer.material = blueMaterial;  // 2. ถูกจองไว้ = สีฟ้า
        }
        else
        {
            lightRenderer.material = greenMaterial; // 3. ว่างปกติ = สีเขียว
        }
    }

    // ฟังก์ชันสำหรับให้ปุ่ม UI หรือระบบอื่นเรียกใช้เพื่อสั่ง "จอง" ช่องนี้
    public void SetReserved(bool reserveStatus)
    {
        if (!isOccupied) // จองได้เฉพาะตอนที่ยังไม่มีรถจอดอยู่เท่านั้น
        {
            isReserved = reserveStatus;
            UpdateLightColor();
        }
    }
}