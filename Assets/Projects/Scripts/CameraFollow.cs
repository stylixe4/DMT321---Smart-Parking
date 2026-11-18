using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // เป้าหมายรถคันที่กล้องจะตาม
    [Tooltip("ระยะกล้องเทียบกับรถ: x = ซ้าย/ขวา, y = ความสูง, z = หน้า/หลัง (ติดลบ = อยู่ด้านหลังรถ)")]
    public Vector3 offset = new Vector3(0f, 8f, -10f);
    public float smoothSpeed = 5f;
    [Tooltip("จุดที่กล้องมอง (สูงจากตัวรถกี่เมตร)")]
    public float lookHeight = 1.5f;

    private Transform cachedTarget;
    private SimpleCarController car;

    private void LateUpdate()
    {
        if (target == null) return;

        if (target != cachedTarget)
        {
            cachedTarget = target;
            car = target.GetComponent<SimpleCarController>();
        }

        // ใช้ทิศหน้ารถจริงจาก SimpleCarController (โมเดลรถหันหน้าไปทาง +X ไม่ใช่ +Z)
        Vector3 forward = car != null
            ? car.Forward
            : Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        // คำนวณตำแหน่งกล้องให้อยู่ด้านหลังรถเสมอ
        Vector3 desiredPosition = target.position + right * offset.x + Vector3.up * offset.y + forward * offset.z;
        float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime); // นุ่มเท่ากันทุก frame rate
        transform.position = Vector3.Lerp(transform.position, desiredPosition, t);

        // หันกล้องมองไปที่ตัวรถ
        transform.LookAt(target.position + Vector3.up * lookHeight);
    }
}
