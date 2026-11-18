using UnityEngine;

public class CarSwitcher : MonoBehaviour
{
    [Header("รายชื่อรถทั้งหมดในฉาก")]
    public SimpleCarController[] cars; // ใส่รถทั้ง 3 คันที่นี่
    public CameraFollow mainCamera;    // ลาก Main Camera มาใส่

    private int currentCarIndex = 0;

    private void Start()
    {
        if (cars.Length > 0)
        {
            SelectCar(0); // เริ่มเกมมาให้ควบคุมรถคันแรกทันที
        }
    }

    private void Update()
    {
        // กดปุ่ม C เพื่อสลับไปรถคันถัดไป
        if (Input.GetKeyDown(KeyCode.C))
        {
            int nextIndex = (currentCarIndex + 1) % cars.Length;
            SelectCar(nextIndex);
        }

        // หรือกดปุ่มตัวเลข 1, 2, 3 เพื่อเลือกคันที่ต้องการโดยตรง
        if (Input.GetKeyDown(KeyCode.Alpha1) && cars.Length >= 1) SelectCar(0);
        if (Input.GetKeyDown(KeyCode.Alpha2) && cars.Length >= 2) SelectCar(1);
        if (Input.GetKeyDown(KeyCode.Alpha3) && cars.Length >= 3) SelectCar(2);
    }

    public void SelectCar(int index)
    {
        currentCarIndex = index;

        // เปิดการควบคุมเฉพาะคันที่เลือก และปิดคันอื่น
        for (int i = 0; i < cars.Length; i++)
        {
            cars[i].isControlled = (i == currentCarIndex);
        }

        // สั่งให้กล้องย้ายตามไปที่รถคันใหม่
        if (mainCamera != null)
        {
            mainCamera.target = cars[currentCarIndex].transform;
        }
    }
}