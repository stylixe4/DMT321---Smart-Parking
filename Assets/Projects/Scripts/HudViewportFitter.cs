using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ทำให้ภาพ 3D ไม่ถูก UI บัง
// - ย่อพื้นที่วาดของกล้อง (Camera.rect) ให้อยู่ "ระหว่าง" คอลัมน์ Panel ซ้าย/ขวา และใต้แถบหัวด้านบน
// - พื้นที่นอกภาพ 3D ถมด้วยพื้นหลังสีเข้ม + กรอบรอบภาพ 3D
// - คำนวณจากตำแหน่ง Panel จริงทุกเฟรม จอขนาดไหน/สัดส่วนไหนก็ไม่บัง
// - กด H (toggleKey) เพื่อซ่อน/แสดงคอลัมน์ซ้าย-ขวา (ซ่อนแล้วภาพ 3D กว้างเต็มจอ ใต้แถบหัว)
// ใส่ไว้ที่ Canvas (Screen Space - Overlay) โดยเมนู Tools > Smart Parking > Build Modern Menu HUD
[DisallowMultipleComponent]
public class HudViewportFitter : MonoBehaviour
{
    [Header("กล้องที่แสดงภาพ 3D")]
    public Camera targetCamera;

    [Header("Panel แต่ละกลุ่ม (ภาพ 3D จะอยู่ในช่องว่างตรงกลาง)")]
    public RectTransform[] leftColumn;
    public RectTransform[] rightColumn;
    public RectTransform[] topBar;

    [Header("ระยะ (หน่วยเดียวกับ Canvas อ้างอิง 1920x1080)")]
    public float gap = 10f;
    public float bottomMargin = 24f;

    [Header("พื้นหลัง/กรอบ")]
    public Color backdropColor = new Color(0.035f, 0.04f, 0.055f, 1f);
    public Sprite frameSprite;
    public Color frameColor = new Color(1f, 1f, 1f, 0.35f);

    [Header("ซ่อน/แสดงคอลัมน์ซ้าย-ขวา")]
    public KeyCode toggleKey = KeyCode.H;
    public bool columnsVisible = true;

    private Canvas canvas;
    private RectTransform canvasRect;
    private readonly Image[] strips = new Image[4];
    private Image frame;
    private readonly Vector3[] corners = new Vector3[4];
    private readonly List<CanvasGroup> columnGroups = new List<CanvasGroup>();
    private Rect applied = new Rect(-1f, -1f, -1f, -1f);
    private bool appliedColumns;

    public Rect CurrentViewport => applied;

    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvas = canvas.rootCanvas;
        canvasRect = canvas != null ? canvas.transform as RectTransform : null;

        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) targetCamera = FindFirstObjectByType<Camera>();

        CreateBackdrop();
        CollectColumnGroups();
        appliedColumns = !columnsVisible; // บังคับให้ตั้งค่าครั้งแรก
    }

    private void OnDisable()
    {
        if (targetCamera != null) targetCamera.rect = new Rect(0f, 0f, 1f, 1f);
        foreach (var s in strips) if (s != null) s.enabled = false;
        if (frame != null) frame.enabled = false;
        SetColumnsShown(true);
        applied = new Rect(-1f, -1f, -1f, -1f);
    }

    private void LateUpdate()
    {
        if (canvasRect == null || targetCamera == null) return;

        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)) columnsVisible = !columnsVisible;
        if (appliedColumns != columnsVisible)
        {
            appliedColumns = columnsVisible;
            SetColumnsShown(columnsVisible);
        }

        Rect r = ComputeViewport(columnsVisible);
        if (!Approximately(r, applied)) Apply(r);
    }

    // ------------------------------------------------------------ คำนวณพื้นที่ภาพ 3D (0..1 ของจอ)

    public Rect ComputeViewport(bool includeColumns)
    {
        canvasRect.GetWorldCorners(corners);
        float x0 = corners[0].x, y0 = corners[0].y;
        float w = corners[2].x - x0, h = corners[2].y - y0;
        if (w <= 1f || h <= 1f) return new Rect(0f, 0f, 1f, 1f);

        float unit = canvasRect.lossyScale.x; // จำนวน pixel ต่อ 1 หน่วยของ Canvas
        float left = x0, right = x0 + w, top = y0 + h, bottom = y0 + bottomMargin * unit;

        if (includeColumns)
        {
            foreach (var p in Active(leftColumn)) { p.GetWorldCorners(corners); left = Mathf.Max(left, corners[2].x + gap * unit); }
            foreach (var p in Active(rightColumn)) { p.GetWorldCorners(corners); right = Mathf.Min(right, corners[0].x - gap * unit); }
        }
        foreach (var p in Active(topBar)) { p.GetWorldCorners(corners); top = Mathf.Min(top, corners[0].y - gap * unit); }

        // พื้นที่เหลือเล็กเกินไป (จอแคบมาก) ใช้เต็มจอแทน
        if (right - left < w * 0.2f || top - bottom < h * 0.2f) return new Rect(0f, 0f, 1f, 1f);
        return new Rect((left - x0) / w, (bottom - y0) / h, (right - left) / w, (top - bottom) / h);
    }

    private static IEnumerable<RectTransform> Active(RectTransform[] list)
    {
        if (list == null) yield break;
        foreach (var p in list) if (p != null && p.gameObject.activeInHierarchy) yield return p;
    }

    // ------------------------------------------------------------ ใช้ค่า

    private void Apply(Rect r)
    {
        applied = r;
        targetCamera.rect = r;

        bool full = r.width >= 0.999f && r.height >= 0.999f;
        SetStrip(0, new Vector2(0f, 0f), new Vector2(r.xMin, 1f), full);              // ซ้าย
        SetStrip(1, new Vector2(r.xMax, 0f), new Vector2(1f, 1f), full);              // ขวา
        SetStrip(2, new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, 1f), full);      // บน
        SetStrip(3, new Vector2(r.xMin, 0f), new Vector2(r.xMax, r.yMin), full);      // ล่าง

        if (frame != null)
        {
            frame.enabled = !full;
            Place(frame.rectTransform, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMax));
        }
    }

    private void SetStrip(int i, Vector2 min, Vector2 max, bool hide)
    {
        var s = strips[i];
        if (s == null) return;
        s.enabled = !hide && max.x - min.x > 0.0001f && max.y - min.y > 0.0001f;
        Place(s.rectTransform, min, max);
    }

    private static void Place(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ------------------------------------------------------------ สร้างพื้นหลัง/กรอบ (ตอน Play)

    private void CreateBackdrop()
    {
        if (canvasRect == null) return;
        string[] names = { "Left", "Right", "Top", "Bottom" };
        for (int i = 0; i < strips.Length; i++)
        {
            strips[i] = NewImage("HUD_ViewBackdrop_" + names[i], null, backdropColor);
            strips[i].transform.SetSiblingIndex(i); // วาดก่อน (อยู่หลัง) Panel ทั้งหมด
        }

        if (frameSprite != null)
        {
            frame = NewImage("HUD_ViewFrame", frameSprite, frameColor);
            frame.type = Image.Type.Sliced;
            frame.transform.SetSiblingIndex(strips.Length);
        }
    }

    private Image NewImage(string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = canvasRect.gameObject.layer;
        go.transform.SetParent(canvasRect, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        img.enabled = false;
        return img;
    }

    // ------------------------------------------------------------ ซ่อน/แสดงคอลัมน์ (ใช้ CanvasGroup สคริปต์ใน Panel จึงยังทำงานต่อ)

    private void CollectColumnGroups()
    {
        columnGroups.Clear();
        AddGroups(leftColumn);
        AddGroups(rightColumn);
    }

    private void AddGroups(RectTransform[] list)
    {
        if (list == null) return;
        foreach (var p in list)
        {
            if (p == null) continue;
            var g = p.GetComponent<CanvasGroup>();
            if (g == null) g = p.gameObject.AddComponent<CanvasGroup>();
            columnGroups.Add(g);
        }
    }

    private void SetColumnsShown(bool shown)
    {
        foreach (var g in columnGroups)
        {
            if (g == null) continue;
            g.alpha = shown ? 1f : 0f;
            g.blocksRaycasts = shown;
            g.interactable = shown;
        }
    }

    private static bool Approximately(Rect a, Rect b)
    {
        const float e = 0.0005f;
        return Mathf.Abs(a.x - b.x) < e && Mathf.Abs(a.y - b.y) < e && Mathf.Abs(a.width - b.width) < e && Mathf.Abs(a.height - b.height) < e;
    }
}
