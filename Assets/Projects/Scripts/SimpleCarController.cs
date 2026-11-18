using UnityEngine;

// ควบคุมรถแบบง่าย (W/S หรือ ลูกศรขึ้น/ลง = เดินหน้า/ถอยหลัง, A/D หรือ ลูกศรซ้าย/ขวา = เลี้ยว)
// แก้บั๊กเดิม: โมเดลรถชุดนี้หันหน้าไปทางแกน +X แต่สคริปต์เดิมสั่งวิ่งตามแกน +Z (transform.forward)
// รถจึงวิ่งออกด้านข้าง + Rigidbody ไม่ได้ล็อกการหมุน ทำให้รถเอียง/พลิกเวลาชน
[RequireComponent(typeof(Rigidbody))]
public class SimpleCarController : MonoBehaviour
{
    public enum ForwardAxis { Auto, PositiveX, NegativeX, PositiveZ, NegativeZ }

    [Header("การขับ")]
    public float moveSpeed = 10f;          // ความเร็วสูงสุดเดินหน้า (เมตร/วินาที)
    public float reverseSpeed = 5f;        // ความเร็วสูงสุดถอยหลัง
    public float acceleration = 8f;        // อัตราเร่ง
    public float brakeDeceleration = 16f;  // อัตราเบรก/ชะลอเมื่อปล่อยคันเร่ง
    public float turnSpeed = 80f;          // ความเร็วในการเลี้ยว (องศา/วินาที)
    [Tooltip("ความเร็วที่พวงมาลัยเลี้ยวได้เต็มที่ (ช้ากว่านี้จะเลี้ยวได้น้อยลง เหมือนรถจริง)")]
    public float fullSteerSpeed = 3f;

    [Header("ทิศหน้ารถของโมเดล")]
    [Tooltip("Auto = หาเองจากตำแหน่งไฟหน้า (Front lights) — รถในโปรเจคนี้หันหน้าไปทาง +X")]
    public ForwardAxis forwardAxis = ForwardAxis.Auto;

    public bool isControlled = false; // CarSwitcher จะเป็นคนเปิด-ปิดค่านี้ให้เอง

    private Rigidbody rb;
    private float moveInput;
    private float turnInput;
    private Vector3 localForward = Vector3.forward;

    // ทิศหน้ารถในโลก (แนวราบ) — CameraFollow ใช้ค่านี้วางกล้องไว้ด้านหลังรถ
    public Vector3 Forward
    {
        get
        {
            Vector3 f = Vector3.ProjectOnPlane(transform.TransformDirection(localForward), Vector3.up);
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }
    }

    public float CurrentSpeed { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        localForward = ResolveLocalForward();

        // กันรถพลิก/เอียง และให้ภาพลื่นตอนกล้องตาม
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.mass = Mathf.Max(rb.mass, 1200f);

        // ตัวรถประกอบจาก Collider หลายชิ้น (ล้อเป็น Capsule) — ปิดแรงเสียดทานไม่ให้ล้อครูดพื้นจนรถกระตุก
        // ความเร็วและการหยุดรถถูกควบคุมจากสคริปต์นี้แทน
        var noFriction = new PhysicsMaterial("CarNoFriction")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            if (!col.isTrigger) col.sharedMaterial = noFriction;
        }
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
        if (rb.isKinematic) return; // ระหว่างแอนิเมชันเสกรถ (SlotCarSpawner) ไม่ต้องคุมความเร็ว

        Vector3 forward = Forward;
        Vector3 velocity = rb.linearVelocity;
        float speed = Vector3.Dot(velocity, forward);

        // คำนวณความเร็วเป้าหมาย (ไม่ได้ขับ/ไม่กดคันเร่ง = ค่อยๆ เบรกจนหยุด)
        float targetSpeed = 0f;
        float rate = brakeDeceleration;
        if (isControlled && Mathf.Abs(moveInput) > 0.01f)
        {
            targetSpeed = moveInput > 0f ? moveInput * moveSpeed : moveInput * reverseSpeed;
            bool sameDirection = Mathf.Abs(speed) < 0.1f || Mathf.Sign(targetSpeed) == Mathf.Sign(speed);
            rate = sameDirection ? acceleration : brakeDeceleration; // กดสวนทาง = เบรกก่อน
        }

        speed = Mathf.MoveTowards(speed, targetSpeed, rate * Time.fixedDeltaTime);
        CurrentSpeed = speed;

        // วิ่งตามทิศหน้ารถเท่านั้น (ไม่ไถลออกด้านข้าง) และคงแรงโน้มถ่วงไว้
        Vector3 newVelocity = forward * speed;
        newVelocity.y = velocity.y;
        rb.linearVelocity = newVelocity;
        rb.angularVelocity = Vector3.zero; // ไม่ให้รถหมุนติ้วเวลาโดนชน

        // เลี้ยวได้เฉพาะตอนรถขยับ (ถอยหลังจะเลี้ยวกลับด้านเหมือนรถจริง)
        if (isControlled && Mathf.Abs(turnInput) > 0.01f && Mathf.Abs(speed) > 0.05f)
        {
            float steer = Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(0.01f, fullSteerSpeed));
            float direction = speed >= 0f ? 1f : -1f;
            float turn = turnInput * turnSpeed * steer * direction * Time.fixedDeltaTime;
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, turn, 0f));
        }
    }

    private Vector3 ResolveLocalForward()
    {
        switch (forwardAxis)
        {
            case ForwardAxis.PositiveX: return Vector3.right;
            case ForwardAxis.NegativeX: return Vector3.left;
            case ForwardAxis.PositiveZ: return Vector3.forward;
            case ForwardAxis.NegativeZ: return Vector3.back;
        }

        // Auto: ดูว่าไฟหน้า ("Front ...") อยู่ฝั่งไหนของตัวรถ
        foreach (Transform part in GetComponentsInChildren<Transform>())
        {
            if (part == transform || !part.name.StartsWith("Front")) continue;
            Vector3 p = transform.InverseTransformPoint(part.position);
            if (Mathf.Abs(p.x) > Mathf.Abs(p.z)) return p.x >= 0f ? Vector3.right : Vector3.left;
            return p.z >= 0f ? Vector3.forward : Vector3.back;
        }

        // ไม่เจอไฟหน้า: ใช้ด้านที่ยาวที่สุดของ BoxCollider
        var box = GetComponent<BoxCollider>();
        if (box != null && box.size.x > box.size.z) return Vector3.right;
        return Vector3.forward;
    }
}
