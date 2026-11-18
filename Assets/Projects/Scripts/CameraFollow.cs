using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // เป้าหมายรถคันที่กล้องจะตาม
    public Vector3 offset = new Vector3(0f, 8f, -10f); // ระยะห่างมุมมอง (สูง 8, ถอยหลัง 10)
    public float smoothSpeed = 5f;

    private void LateUpdate()
    {
        if (target == null) return;

        // คำนวณตำแหน่งกล้องให้อยู่ด้านหลังรถเสมอ
        Vector3 desiredPosition = target.TransformPoint(offset);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);

        // หันกล้องมองไปที่ตัวรถ
        transform.LookAt(target.position + Vector3.up * 1.5f);
    }
}