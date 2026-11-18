using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// เสก/ลบ รถในช่องจอดตามปุ่ม Slot 1-7
// - กดครั้งแรก: สุ่มรถจาก carPrefabs มาจอดตรงกลางช่อง (หันหัวเข้าช่อง) พร้อมแอนิเมชันเด้งขึ้นมา
// - กดอีกครั้ง: รถคันนั้นหดหายไป
// รถที่เสกมามี Tag "Car" จาก Prefab อยู่แล้ว เซนเซอร์ของช่องจะนับว่ามีรถจอด และ UI จะอัปเดตเอง
public class SlotCarSpawner : MonoBehaviour
{
    [Header("ช่องจอด เรียงตามลำดับปุ่ม (Slot 1 = ช่องแรก)")]
    public List<ParkingSensor> slots = new List<ParkingSensor>();

    [Header("รถที่จะสุ่มมาจอด (ลาก Prefab รถมาใส่)")]
    public List<GameObject> carPrefabs = new List<GameObject>();

    [Header("การวางรถ")]
    [Tooltip("เปิด = หันหัวรถเข้าหากำแพง/หลอดไฟของช่องจอด, ปิด = ถอยท้ายเข้า")]
    public bool noseIn = true;
    [Tooltip("ความยาวแอนิเมชันตอนรถโผล่/หาย (วินาที)")]
    public float popDuration = 0.3f;

    private readonly Dictionary<int, GameObject> spawnedCars = new Dictionary<int, GameObject>();
    private int lastPrefabIndex = -1;

    public int SlotCount => slots.Count;

    public ParkingSensor GetSlot(int index)
    {
        return index >= 0 && index < slots.Count ? slots[index] : null;
    }

    public bool HasSpawnedCar(int index)
    {
        return spawnedCars.TryGetValue(index, out var car) && car != null;
    }

    // ใช้กับปุ่ม: slotNumber เริ่มที่ 1
    public void ToggleSlotByNumber(int slotNumber)
    {
        ToggleSlot(slotNumber - 1);
    }

    public void ToggleSlot(int index)
    {
        if (HasSpawnedCar(index)) RemoveCar(index);
        else SpawnCar(index);
    }

    public GameObject SpawnCar(int index)
    {
        var slot = GetSlot(index);
        if (slot == null || carPrefabs.Count == 0) return null;
        if (slot.IsOccupied)
        {
            Debug.Log($"[SlotCarSpawner] Slot {index + 1} มีรถจอดอยู่แล้ว");
            return null;
        }

        // สุ่มรุ่นรถ (ไม่ให้ซ้ำคันก่อนหน้า)
        int pick = Random.Range(0, carPrefabs.Count);
        if (carPrefabs.Count > 1 && pick == lastPrefabIndex) pick = (pick + 1) % carPrefabs.Count;
        lastPrefabIndex = pick;
        var prefab = carPrefabs[pick];
        if (prefab == null) return null;

        Vector3 center = slot.transform.position;
        float groundY = FindGroundY(center);
        var car = Instantiate(prefab, new Vector3(center.x, groundY + 3f, center.z), Quaternion.identity);
        car.name = $"{prefab.name} (Slot {index + 1})";

        // หันรถให้ตรงช่องจอด
        var controller = car.GetComponent<SimpleCarController>();
        if (controller != null) controller.isControlled = false;
        Vector3 carForward = controller != null ? controller.Forward : Vector3.ProjectOnPlane(car.transform.right, Vector3.up).normalized;
        float yaw = Vector3.SignedAngle(carForward, SlotNoseDirection(slot), Vector3.up);
        car.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * car.transform.rotation;

        // วางให้ล้อแตะพื้นพอดี และอยู่กลางช่อง
        Bounds bounds = RendererBounds(car);
        car.transform.position += new Vector3(center.x - bounds.center.x, groundY - bounds.min.y + 0.02f, center.z - bounds.center.z);

        var rb = car.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true; // ล็อกไว้ระหว่างแอนิเมชัน

        spawnedCars[index] = car;
        StartCoroutine(PopIn(car.transform, rb));
        Debug.Log($"[SlotCarSpawner] Slot {index + 1}: เพิ่มรถ {prefab.name}");
        return car;
    }

    public void RemoveCar(int index)
    {
        if (!spawnedCars.TryGetValue(index, out var car)) return;
        spawnedCars.Remove(index);
        if (car != null) StartCoroutine(PopOutAndDestroy(car));
        Debug.Log($"[SlotCarSpawner] Slot {index + 1}: เอารถออก");
    }

    // ลบรถที่เสกไว้ทั้งหมด
    public void RemoveAllCars()
    {
        foreach (int index in new List<int>(spawnedCars.Keys)) RemoveCar(index);
    }

    // ------------------------------------------
    // แอนิเมชัน
    // ------------------------------------------
    private IEnumerator PopIn(Transform car, Rigidbody rb)
    {
        Vector3 fullScale = car.localScale;
        float duration = Mathf.Max(0.01f, popDuration);
        float t = 0f;
        while (t < duration && car != null)
        {
            t += Time.deltaTime;
            car.localScale = fullScale * Mathf.Max(0.05f, EaseOutBack(Mathf.Clamp01(t / duration)));
            yield return null;
        }
        if (car == null) yield break;
        car.localScale = fullScale;
        if (rb != null) rb.isKinematic = false;
    }

    private IEnumerator PopOutAndDestroy(GameObject car)
    {
        var rb = car.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        Vector3 fullScale = car.transform.localScale;
        float duration = Mathf.Max(0.01f, popDuration * 0.7f);
        float t = 0f;
        while (t < duration && car != null)
        {
            t += Time.deltaTime;
            car.transform.localScale = fullScale * Mathf.Lerp(1f, 0.05f, t / duration);
            yield return null;
        }
        if (car != null) Destroy(car);
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // ------------------------------------------
    // ตัวช่วยคำนวณตำแหน่ง
    // ------------------------------------------

    // ทิศ "หัวรถ" ในช่องจอด: ตามแนวยาวของเซนเซอร์ ชี้ไปทางหลอดไฟ (หลังช่อง)
    private Vector3 SlotNoseDirection(ParkingSensor slot)
    {
        Vector3 axisX = Vector3.ProjectOnPlane(slot.transform.right, Vector3.up).normalized;
        Vector3 axisZ = Vector3.ProjectOnPlane(slot.transform.forward, Vector3.up).normalized;
        Vector3 size = Vector3.one;
        var box = slot.GetComponent<BoxCollider>();
        if (box != null) size = Vector3.Scale(box.size, slot.transform.lossyScale);
        Vector3 axis = Mathf.Abs(size.x) >= Mathf.Abs(size.z) ? axisX : axisZ;

        if (slot.lightRenderer != null)
        {
            Vector3 toBack = slot.lightRenderer.transform.position - slot.transform.position;
            if (Vector3.Dot(axis, toBack) < 0f) axis = -axis;
        }
        return noseIn ? axis : -axis;
    }

    private static float FindGroundY(Vector3 point)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 5f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var hit in hits)
        {
            if (hit.collider.attachedRigidbody != null) continue; // ข้ามรถคันอื่น
            if (hit.point.y > best) best = hit.point.y;
        }
        return best > float.NegativeInfinity ? best : point.y - 0.75f;
    }

    private static Bounds RendererBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
