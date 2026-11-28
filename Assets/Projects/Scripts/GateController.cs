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
    private float externalOpenTimer = 0f; // เวลาเปิดค้างตามคำสั่งภายนอก (RFID / MQTT)
    private bool initialized = false;
    private bool wasOpen = false;      // ใช้บันทึกลง EVENT LOG ตอนไม้กั้นเปลี่ยนสถานะ

    private void Awake()
    {
        InitRotations();
    }

    private void Start()
    {
        InitRotations();
    }

    private void InitRotations()
    {
        if (initialized || gateArm == null) return;
        closedRot = gateArm.localRotation; // จำท่าปิดเริ่มต้นไว้
        openRot = closedRot * Quaternion.Euler(openRotation); // คำนวณท่าเปิด
        initialized = true;
    }

    private void Update()
    {
        if (gateArm == null) return;

        if (externalOpenTimer > 0f)
        {
            externalOpenTimer -= Time.deltaTime;
        }

        // ถ้ารถอยู่ในระยะเซนเซอร์ หรือได้รับคำสั่งเปิดจาก RFID/MQTT ให้ยกขึ้น ไม่อย่างนั้นให้ลดลง
        bool shouldOpen = carsInZone > 0 || externalOpenTimer > 0f;
        if (shouldOpen != wasOpen)
        {
            wasOpen = shouldOpen;
            string source = carsInZone > 0 ? "car detected" : "RFID/MQTT";
            ParkingEventLog.Add(ParkingEventKind.GATE, shouldOpen ? $"{GateName()} gate open ({source})" : $"{GateName()} gate closed");
        }
        Quaternion targetRot = shouldOpen ? openRot : closedRot;
        gateArm.localRotation = Quaternion.Slerp(gateArm.localRotation, targetRot, Time.deltaTime * smoothSpeed);
    }

    // สั่งเปิดไม้กั้นเป็นเวลาชั่วคราว (เช่น แตะบัตร RFID ทางเข้า หรือ เซนเซอร์ทางออกตรวจจับได้)
    public void TriggerOpen(float duration = 3f)
    {
        InitRotations();
        externalOpenTimer = Mathf.Max(externalOpenTimer, duration);
    }

    private string GateName()
    {
        string n = gameObject.name.ToLowerInvariant();
        if (n.Contains("entr") || n.Contains("enter")) return "entrance";
        if (n.Contains("exit")) return "exit";
        return gameObject.name;
    }

    [ContextMenu("Test Open (3s)")]
    private void TestOpen() => TriggerOpen(3f);

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