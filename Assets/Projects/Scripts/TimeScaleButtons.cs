using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// แผง TIME SPEED: ปุ่มเร่งเวลาจอดรถ (1x ปกติ / 5x / 10x / 15x / 20x)
// ตัวปุ่มเรียก ParkingLotManager.SetTimeScale(ค่า) ผ่าน OnClick ใน Inspector
// สคริปต์นี้มีหน้าที่ไฮไลต์ปุ่มที่กำลังใช้อยู่ และแสดงความเร็วปัจจุบันที่หัวข้อ
public class TimeScaleButtons : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public float scale = 1f;
        public Button button;
        public Image fill;
        public Image frame;
        public TMP_Text label;
        public TMP_Text subLabel;
    }

    public ParkingLotManager manager;
    public TMP_Text headerText;
    public List<Entry> entries = new List<Entry>();

    [Header("สี (ธีม Modern Menu)")]
    public Color accentColor = new Color(1f, 0.686f, 0f);
    public Color idleFill = new Color(0.08f, 0.09f, 0.11f, 0.92f);
    public Color idleFrame = new Color(1f, 1f, 1f, 0.45f);
    public Color labelColor = new Color(0.62f, 0.66f, 0.72f);

    private float shownScale = -1f;

    private void Update()
    {
        if (manager == null) return;
        float current = manager.TimeScale;
        if (Mathf.Approximately(current, shownScale)) return;
        shownScale = current;

        if (headerText != null)
        {
            string hex = ColorUtility.ToHtmlStringRGB(Mathf.Approximately(current, 1f) ? labelColor : accentColor);
            headerText.text = $"TIME SPEED  <color=#{hex}>|  x{current:0.##}</color>";
        }

        foreach (var e in entries)
        {
            bool active = Mathf.Approximately(e.scale, current);
            if (e.fill != null) e.fill.color = active ? new Color(accentColor.r, accentColor.g, accentColor.b, 0.32f) : idleFill;
            if (e.frame != null) e.frame.color = active ? accentColor : idleFrame;
            if (e.label != null) e.label.color = active ? accentColor : Color.white;
            if (e.subLabel != null) e.subLabel.color = active ? accentColor : labelColor;
        }
    }

    // เรียกจากปุ่ม (ทำงานเหมือนกด OnClick)
    public void SetScale(float scale)
    {
        if (manager != null) manager.SetTimeScale(scale);
        ClearSelection();
    }

    // ไม่ให้ปุ่มค้างโฟกัส กัน Space/Enter ไปกดซ้ำตอนขับรถ
    public void ClearSelection()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }
}
