using UnityEngine;

public class GateController : MonoBehaviour
{
    [Header("ตั้งค่าไม้กั้น")]
    public Transform gateArm;          // ลากตัวแขนไม้กั้น (หรือ GatePivot) มาใส่
    public Vector3 openRotation = new Vector3(0, 0, 90); // องศาตอนเปิด (ปรับแกน X, Y, Z ตามโมเดลของคุณ)
    public float smoothSpeed = 5f;     // ความเร็วในการยกขึ้น-ลง

    private Quaternion closedRot;
    private Quaternion openRot;
    private int carsInZone = 0;        // นับจำนวนรถที่อยู่ในระยะเซนเซอร์

    private void Start()
    {
        if (gateArm != null)
        {
            closedRot = gateArm.localRotation; // จำท่าปิดเริ่มต้นไว้
            openRot = closedRot * Quaternion.Euler(openRotation); // คำนวณท่าเปิด
        }
    }

    private void Update()
    {
        if (gateArm == null) return;

        // ถ้ารถอยู่ในระยะเซนเซอร์อย่างน้อย 1 คัน ให้ยกขึ้น ไม่อย่างนั้นให้ลดลง
        Quaternion targetRot = (carsInZone > 0) ? openRot : closedRot;
        gateArm.localRotation = Quaternion.Slerp(gateArm.localRotation, targetRot, Time.deltaTime * smoothSpeed);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Car"))
        {
            carsInZone++;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Car"))
        {
            carsInZone = Mathf.Max(0, carsInZone - 1);
        }
    }
}