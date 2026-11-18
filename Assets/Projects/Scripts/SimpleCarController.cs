using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SimpleCarController : MonoBehaviour
{
    public float moveSpeed = 10f;   // ความเร็วเดินหน้า-ถอยหลัง
    public float turnSpeed = 80f;   // ความเร็วในการเลี้ยว
    public bool isControlled = false; // ตัวจัดการรถจะเป็นคนเปิด-ปิดค่านี้ให้เอง

    private Rigidbody rb;
    private float moveInput;
    private float turnInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        // ถ้าไม่ได้เลือกขับคันนี้อยู่ ให้หยุดรับค่าปุ่มกด
        if (!isControlled)
        {
            moveInput = 0f;
            turnInput = 0f;
            return;
        }

        moveInput = Input.GetAxis("Vertical");
        turnInput = Input.GetAxis("Horizontal");
    }

    private void FixedUpdate()
    {
        if (!isControlled) return;

        // เคลื่อนที่เดินหน้า-ถอยหลัง
        Vector3 movement = transform.forward * moveInput * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + movement);

        // เลี้ยวซ้าย-ขวา (จะเลี้ยวได้เฉพาะตอนรถขยับเท่านั้น)
        if (Mathf.Abs(moveInput) > 0.1f)
        {
            float direction = (moveInput >= 0) ? 1f : -1f; // ถอยหลังให้เลี้ยวกลับด้านเหมือนรถจริง
            float turn = turnInput * turnSpeed * direction * Time.fixedDeltaTime;
            Quaternion turnRotation = Quaternion.Euler(0f, turn, 0f);
            rb.MoveRotation(rb.rotation * turnRotation);
        }
    }
}