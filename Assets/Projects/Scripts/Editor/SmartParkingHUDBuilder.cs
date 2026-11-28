using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// เครื่องมือจัดหน้าตา Dashboard ใหม่ด้วยกราฟิก/ฟอนต์/สีจาก SlimUI "Modern Menu 1"
// เมนู: Tools > Smart Parking > Build Modern Menu HUD
// - ตั้ง Canvas Scaler ให้ Scale With Screen Size (1920x1080) => UI ขยาย/ย่อตามจอ โดยสัดส่วนไม่เบี้ยว
// - ยึด Panel ไว้กับมุมจอ (Anchor) => จอกว้าง/แคบ Panel ก็ยังเกาะขอบจอ ไม่ทับกัน
// - ใช้ GameObject เดิมทั้งหมด (ชื่อเดิม) ของตกแต่งที่สร้างใหม่จะขึ้นต้นด้วย "HUD_" กดซ้ำได้ไม่ซ้อนกัน
// - Layout แบบ Dashboard: แถบหัวด้านบน (ชื่อระบบ + Warning), คอลัมน์ซ้าย (สถานะ + Park timer),
//   คอลัมน์ขวา (ปุ่ม Slot + Time speed + EVENT LOG) และภาพ 3D อยู่ตรงกลาง (HudViewportFitter ย่อกล้องให้ไม่โดน UI บัง)
public static class SmartParkingHUDBuilder
{
    private const string SlimRoot = "Assets/SlimUI/Modern Menu 1/";
    private const string ThemePath = "Assets/SlimUI/Data/ThemeSettings_ModernMenuUI.asset";
    private const string BlinkOffMaterialPath = "Assets/Projects/Material/Black.mat";
    private const string Prefix = "HUD_";

    // ขนาดอ้างอิงของ Layout (ออกแบบที่ 1920x1080 แล้วให้ Canvas Scaler ขยายตามจอ)
    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    private const float Margin = 24f;
    private const float Gap = 10f;
    private const float TopBarHeight = 96f;
    private const float TitleWidth = 720f;
    private const float LeftWidth = 440f;
    private const float RightWidth = 400f;
    private const float HeaderHeight = 40f;
    private const float RowHeight = 34f;
    private const float RowStep = 36f;

    // ระยะจากขอบบนจอถึงหัวคอลัมน์ซ้าย/ขวา (ใต้แถบหัว)
    private static float ColumnTop => Margin + TopBarHeight + Gap;

    private static readonly Color PanelFill = new Color(0.055f, 0.063f, 0.082f, 0.86f);
    private static readonly Color FrameTint = new Color(1f, 1f, 1f, 0.35f);
    private static readonly Color LabelColor = new Color(0.62f, 0.66f, 0.72f, 1f);
    private static readonly Color SeparatorColor = new Color(1f, 1f, 1f, 0.07f);

    private static TMP_FontAsset fontBold, fontLight, fontBody;
    private static Sprite frameSprite, gradientSprite;
    private static Color accent;

    [MenuItem("Tools/Smart Parking/Build Modern Menu HUD")]
    public static void Build()
    {
        if (!LoadAssets()) return;

        Canvas canvas = FindCanvas();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Smart Parking HUD", "ไม่พบ Canvas ในฉากที่เปิดอยู่", "OK");
            return;
        }

        SetupCanvas(canvas);
        Transform root = canvas.transform;

        // ---------- ซ้ายบน: ชื่อระบบ ----------
        var title = PlacePanel(root, "Title_Panel", new Vector2(0, 1), new Vector2(Margin, -Margin), new Vector2(TitleWidth, TopBarHeight), false);
        RectTransform liveDot = null;
        TMP_Text clock = null;
        if (title != null)
        {
            var bg = title.GetComponent<Image>();
            bg.sprite = gradientSprite;
            bg.type = Image.Type.Simple;
            bg.color = new Color(0.03f, 0.035f, 0.05f, 0.95f);
            AccentBar(title, 0f, 0f, 6f, true);

            var titleText = Child<TMP_Text>(title, "title_text");
            if (titleText != null)
            {
                Style(titleText, fontBold, 40f, Color.white, TextAlignmentOptions.Left, 3f);
                TopStretch(titleText.rectTransform, 10f, 50f, 26f, 190f);
                titleText.text = $"SMART PARKING <color=#{ColorUtility.ToHtmlStringRGB(accent)}>TWIN</color>";
            }

            liveDot = AddImage(title, "LiveDot", null, new Color(0.3f, 0.87f, 0.5f)).rectTransform;
            SetRect(liveDot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28f, -68f), new Vector2(10f, 10f));

            var subtitle = AddText(title, "Subtitle", fontLight, 15f, LabelColor, TextAlignmentOptions.Left, 8f);
            TopStretch(subtitle.rectTransform, 61f, 24f, 46f, 190f);
            subtitle.text = "LIVE  |  DIGITAL TWIN DASHBOARD  |  PARK-01";

            clock = AddText(title, "Clock", fontBody, 30f, Color.white, TextAlignmentOptions.TopRight, 2f);
            SetRect(clock.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20f, -16f), new Vector2(170f, 70f));
            clock.text = "00:00:00\n<size=55%>-- --- ----</size>";
        }

        // ---------- ซ้าย: Availability ----------
        float y = -ColumnTop;
        var avail = PlacePanel(root, "Availability_Panel", new Vector2(0, 1), new Vector2(Margin, y), new Vector2(LeftWidth, 144f), true);
        TMP_Text availDetail = null;
        RectTransform barFill = null;
        if (avail != null)
        {
            Header(avail, null, "AVAILABILITY", 0f);

            var big = Child<TMP_Text>(avail, "Availability_text");
            if (big != null)
            {
                Style(big, fontBold, 60f, Color.white, TextAlignmentOptions.Left, 0f);
                TopStretch(big.rectTransform, 46f, 64f, 20f, 200f);
                big.text = "--<size=50%>%</size>";
            }

            availDetail = AddText(avail, "AvailDetail", fontBody, 18f, Color.white, TextAlignmentOptions.BottomRight, 2f);
            TopStretch(availDetail.rectTransform, 56f, 44f, 200f, 20f);
            availDetail.text = "- / -  SLOTS FREE";

            var barBg = AddImage(avail, "BarBG", null, new Color(1f, 1f, 1f, 0.08f));
            TopStretch(barBg.rectTransform, 118f, 8f, 20f, 20f);
            var fill = AddImage(barBg.transform, "BarFill", null, new Color(0.3f, 0.87f, 0.5f));
            barFill = fill.rectTransform;
            barFill.anchorMin = Vector2.zero;
            barFill.anchorMax = new Vector2(0.7f, 1f);
            barFill.offsetMin = barFill.offsetMax = Vector2.zero;
        }
        y -= 144f + Gap;

        // ---------- ซ้าย: Normal slots ----------
        float normalHeight = HeaderHeight + 10f + 2 * RowStep + 12f;
        var normal = PlacePanel(root, "Normalslotstatus_panel", new Vector2(0, 1), new Vector2(Margin, y), new Vector2(LeftWidth, normalHeight), true);
        if (normal != null)
        {
            Header(normal, Child<TMP_Text>(normal, "titlenormalslots"), "NORMAL SLOTS", 0f);
            Rows(normal, HeaderHeight + 10f, "Freeslottext", "Occupiedslottext");
        }
        y -= normalHeight + Gap;

        // ---------- ซ้าย: Special slots ----------
        float specialHeight = HeaderHeight + 10f + 3 * RowStep + 12f;
        var special = PlacePanel(root, "Specialslotstatus_panel", new Vector2(0, 1), new Vector2(Margin, y), new Vector2(LeftWidth, specialHeight), true);
        if (special != null)
        {
            Header(special, Child<TMP_Text>(special, "titlespecialslots"), "SPECIAL SLOTS", 0f);
            Rows(special, HeaderHeight + 10f, "VIPslottext", "Accessibilityslottext", "Maternityslottext");
        }
        y -= specialHeight + Gap;

        // ---------- ซ้าย: Reserved ----------
        var reserved = PlacePanel(root, "Reserves_Panel", new Vector2(0, 1), new Vector2(Margin, y), new Vector2(LeftWidth, 56f), true);
        if (reserved != null)
        {
            AccentBar(reserved, 0f, 0f, 4f, true);
            var t = Child<TMP_Text>(reserved, "Reserves_text");
            if (t != null)
            {
                Style(t, fontBody, 22f, Color.white, TextAlignmentOptions.Left, 2f);
                TopStretch(t.rectTransform, 11f, RowHeight, 22f, 20f);
            }
        }

        y -= 56f + Gap;

        // ---------- ซ้ายล่าง: Park timer ----------
        float specialHeaderTop = HeaderHeight + 10f + 4 * RowStep + 10f;
        float timerHeight = TimerPanelHeight();
        var timer = PlacePanel(root, "Parktimer_panel", new Vector2(0, 1), new Vector2(Margin, y), new Vector2(LeftWidth, timerHeight), true);
        if (timer != null)
        {
            Header(timer, Child<TMP_Text>(timer, "Timer_text"), "PARK TIMER  |  NORMAL", 0f);
            Rows(timer, HeaderHeight + 10f, "slot1_timer", "slot2_timer", "slot3_timer", "slot4_timer");
            Header(timer, Child<TMP_Text>(timer, "Timer_Special_text"), "PARK TIMER  |  SPECIAL", specialHeaderTop);
            Rows(timer, specialHeaderTop + HeaderHeight + 10f, "slot_vip_timer", "slot_accessibility_timer", "slot_maternity_timer");
        }

        // ---------- แถบหัว ขวาของชื่อระบบ: Warning (ยืดถึงขอบขวาจอ) ----------
        var warning = PlacePanel(root, "Warning_panel", new Vector2(0, 1), new Vector2(Margin + TitleWidth + Gap, -Margin), new Vector2(400f, TopBarHeight), true);
        if (warning != null)
        {
            warning.anchorMin = new Vector2(0, 1);
            warning.anchorMax = new Vector2(1, 1);
            warning.pivot = new Vector2(0, 1);
            warning.offsetMin = new Vector2(Margin + TitleWidth + Gap, -Margin - TopBarHeight);
            warning.offsetMax = new Vector2(-Margin, -Margin);

            AccentBar(warning, 0f, 0f, 6f, true);

            var head = Child<TMP_Text>(warning, "Warning_text");
            if (head != null)
            {
                Style(head, fontBold, 19f, accent, TextAlignmentOptions.Left, 8f);
                SetRect(head.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(26f, 0f), new Vector2(170f, 0f));
                head.text = "WARNING";
            }

            var sep = AddImage(warning, "VSep", null, new Color(1f, 1f, 1f, 0.15f));
            SetRect(sep.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(200f, 0f), new Vector2(2f, 36f));

            var label = Child<TMP_Text>(warning, "Warning_label");
            if (label != null)
            {
                Style(label, fontBody, 24f, Color.white, TextAlignmentOptions.Left, 2f);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                label.rectTransform.offsetMin = new Vector2(222f, 0f);
                label.rectTransform.offsetMax = new Vector2(-20f, 0f);
                label.text = "ALL SYSTEMS NORMAL";
            }
        }

        // ---------- ขวาบน: ปุ่ม Slot 1-7 ----------
        BuildSlotButtons(root);

        // ---------- ขวา ใต้ปุ่ม Slot: TIME SPEED + MQTT ----------
        BuildTimePanel(root);

        // ---------- ขวาล่าง: EVENT LOG (ยืดถึงขอบล่างจอ) ----------
        var eventLog = BuildEventLog(root);

        // ---------- ภาพ 3D อยู่ตรงกลาง ไม่โดน Panel บัง ----------
        SetupViewport(canvas,
            new[] { avail, normal, special, reserved, timer },
            new[] { FindDeep(root, SlotPanelName) as RectTransform, FindDeep(root, TimePanelName) as RectTransform, eventLog },
            new[] { title, warning });

        WireUI(availDetail, barFill, clock, liveDot);
        SetupBlinkMaterial();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Smart Parking] Modern Menu HUD built. กด Ctrl+S เพื่อบันทึกฉาก");
    }

    // ==========================================================
    // ปุ่ม Slot 1-7 (กด = เสกรถมาจอด / กดอีกที = รถหาย)
    // ==========================================================
    private const string SlotPanelName = "SlotButtons_Panel";
    private static readonly string[] CarPrefabPaths =
    {
        "Assets/Projects/Prefabs/Sedan.prefab",
        "Assets/Projects/Prefabs/4x4.prefab",
        "Assets/Projects/Prefabs/Wagon.prefab"
    };

    [MenuItem("Tools/Smart Parking/Build Slot Buttons")]
    public static void BuildSlotButtonsMenu()
    {
        if (!LoadAssets()) return;
        Canvas canvas = FindCanvas();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Smart Parking HUD", "ไม่พบ Canvas ในฉากที่เปิดอยู่", "OK");
            return;
        }
        SetupCanvas(canvas);
        BuildSlotButtons(canvas.transform);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Smart Parking] Slot buttons built. กด Ctrl+S เพื่อบันทึกฉาก");
    }

    private static float TimerPanelHeight()
    {
        float specialHeaderTop = HeaderHeight + 10f + 4 * RowStep + 10f;
        return specialHeaderTop + HeaderHeight + 10f + 3 * RowStep + 12f;
    }

    private static void BuildSlotButtons(Transform root)
    {
        var spawner = SetupSpawner();
        if (spawner == null) return;
        EnsureUIInput(root);

        if (FindDeep(root, SlotPanelName) == null)
        {
            var go = new GameObject(SlotPanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root, false);
            Undo.RegisterCreatedObjectUndo(go, "Slot Buttons");
        }

        const int perRow = 4;
        const float pad = 12f, gap = 8f, buttonHeight = 64f;
        float buttonWidth = (RightWidth - pad * 2f - gap * (perRow - 1)) / perRow;
        int count = Mathf.Max(1, spawner.slots.Count);
        int rows = Mathf.CeilToInt(count / (float)perRow);
        float panelHeight = HeaderHeight + 10f + rows * buttonHeight + (rows - 1) * gap + 14f;
        float top = -ColumnTop;

        var panel = PlacePanel(root, SlotPanelName, new Vector2(1, 1), new Vector2(-Margin, top), new Vector2(RightWidth, panelHeight), true);
        if (panel == null) return;
        Header(panel, null, "SLOT CONTROL", 0f);

        for (int i = 0; i < count; i++)
        {
            int n = i + 1;
            int row = i / perRow, col = i % perRow;
            int inRow = Mathf.Min(perRow, count - row * perRow);
            float rowOffset = (perRow - inRow) * (buttonWidth + gap) * 0.5f; // จัดแถวสุดท้ายให้อยู่กลาง
            float x = pad + rowOffset + col * (buttonWidth + gap);
            float y = HeaderHeight + 10f + row * (buttonHeight + gap);

            var fill = AddImage(panel, "SlotButton_" + n, null, new Color(0.08f, 0.09f, 0.11f, 0.92f));
            fill.raycastTarget = true;
            SetRect(fill.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(buttonWidth, buttonHeight));

            var frame = AddImage(fill.transform, "Frame", frameSprite, new Color(1f, 1f, 1f, 0.45f));
            frame.type = Image.Type.Sliced;
            frame.pixelsPerUnitMultiplier = 1f;
            Stretch(frame.rectTransform);

            var label = AddText(fill.transform, "Label", fontBold, 17f, Color.white, TextAlignmentOptions.Center, 3f);
            TopStretch(label.rectTransform, 6f, 22f, 2f, 2f);
            label.text = "SLOT " + n;

            var type = AddText(fill.transform, "Type", fontLight, 11f, LabelColor, TextAlignmentOptions.Center, 4f);
            TopStretch(type.rectTransform, 28f, 14f, 2f, 2f);
            type.text = SlotButton.TypeName(spawner.GetSlot(i));

            var status = AddText(fill.transform, "Status", fontBody, 12f, LabelColor, TextAlignmentOptions.Center, 3f);
            TopStretch(status.rectTransform, 43f, 16f, 2f, 2f);
            status.text = "ADD CAR";

            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = new Color(0.8f, 0.8f, 0.8f, 0.85f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = new Color(0.8f, 0.8f, 0.8f, 0.85f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // กันปุ่มลูกศร/WASD ไปเลื่อนโฟกัสปุ่มตอนขับรถ

            var slotButton = fill.gameObject.AddComponent<SlotButton>();
            slotButton.spawner = spawner;
            slotButton.slotNumber = n;
            slotButton.fill = fill;
            slotButton.frame = frame;
            slotButton.label = label;
            slotButton.typeLabel = type;
            slotButton.status = status;
            slotButton.accentColor = accent;
            UnityEventTools.AddPersistentListener(button.onClick, slotButton.Toggle);
            EditorUtility.SetDirty(slotButton);
            EditorUtility.SetDirty(button);
        }
    }

    // ใส่ SlotCarSpawner ไว้ที่ GameObject เดียวกับ ParkingLotManager แล้วใช้ลำดับช่องจอดชุดเดียวกัน (Slot 1-7)
    private static SlotCarSpawner SetupSpawner()
    {
        var manager = Object.FindFirstObjectByType<ParkingLotManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Smart Parking HUD", "ไม่พบ ParkingLotManager ในฉาก (ใช้ลำดับช่องจอด Slot 1-7 จากตัวนี้)", "OK");
            return null;
        }

        var spawner = manager.GetComponent<SlotCarSpawner>();
        if (spawner == null) spawner = Undo.AddComponent<SlotCarSpawner>(manager.gameObject);
        else Undo.RecordObject(spawner, "Slot Buttons");

        spawner.slots.Clear();
        var list = new SerializedObject(manager).FindProperty("slots");
        if (list != null)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is ParkingSensor sensor) spawner.slots.Add(sensor);
            }
        }
        if (spawner.slots.Count == 0) Debug.LogWarning("[Smart Parking] ParkingLotManager ยังไม่มีรายการช่องจอด (Slots) — ปุ่มจะยังไม่ทำงาน");

        spawner.carPrefabs.Clear();
        foreach (var path in CarPrefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) spawner.carPrefabs.Add(prefab);
        }
        EditorUtility.SetDirty(spawner);
        return spawner;
    }

    // ปุ่มต้องมี EventSystem + GraphicRaycaster ถึงจะกดได้
    private static void EnsureUIInput(Transform canvasRoot)
    {
        if (canvasRoot.GetComponent<GraphicRaycaster>() == null) Undo.AddComponent<GraphicRaycaster>(canvasRoot.gameObject);
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(es, "Slot Buttons");
        }
    }

    // ==========================================================
    // TIME SPEED: ปุ่มเร่งเวลาจอดรถ 1x (ปกติ) / 5x / 10x / 15x / 20x + สถานะ MQTT
    // ==========================================================
    private const string TimePanelName = "TimeSpeed_Panel";
    private static readonly float[] TimeScales = { 1f, 5f, 10f, 15f, 20f };
    private static readonly string[] TimeScaleNames = { "NORMAL", "FAST", "FASTER", "TURBO", "MAX" };

    [MenuItem("Tools/Smart Parking/Build Time Speed + MQTT Panel")]
    public static void BuildTimePanelMenu()
    {
        if (!LoadAssets()) return;
        Canvas canvas = FindCanvas();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Smart Parking HUD", "ไม่พบ Canvas ในฉากที่เปิดอยู่", "OK");
            return;
        }
        SetupCanvas(canvas);
        var bridge = BuildTimePanel(canvas.transform);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        if (bridge != null) Selection.activeGameObject = bridge.gameObject;
        Debug.Log("[Smart Parking] Time speed + MQTT panel built. กด Ctrl+S เพื่อบันทึกฉาก");
    }

    private static float SlotPanelHeight(int slotCount)
    {
        int rows = Mathf.CeilToInt(Mathf.Max(1, slotCount) / 4f);
        return HeaderHeight + 10f + rows * 64f + (rows - 1) * 8f + 14f;
    }

    private static MqttParkingBridge BuildTimePanel(Transform root)
    {
        var manager = Object.FindFirstObjectByType<ParkingLotManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Smart Parking HUD", "ไม่พบ ParkingLotManager ในฉาก", "OK");
            return null;
        }
        EnsureUIInput(root);

        if (FindDeep(root, TimePanelName) == null)
        {
            var go = new GameObject(TimePanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root, false);
            Undo.RegisterCreatedObjectUndo(go, "Time Speed Panel");
        }

        const float pad = 12f, gap = 8f, buttonTop = HeaderHeight + 10f, buttonHeight = 52f;
        float statusTop = buttonTop + buttonHeight + 18f;
        float panelHeight = statusTop + 22f + 12f;
        float top = -ColumnTop - SlotPanelHeight(manager.Slots.Count) - Gap;

        var panel = PlacePanel(root, TimePanelName, new Vector2(1, 1), new Vector2(-Margin, top), new Vector2(RightWidth, panelHeight), true);
        if (panel == null) return null;

        var header = AddText(panel, "Header", fontBold, 18f, Color.white, TextAlignmentOptions.Left, 8f);
        Header(panel, header, "TIME SPEED  <color=#" + ColorUtility.ToHtmlStringRGB(LabelColor) + ">|  x1</color>", 0f);
        header.transform.SetAsLastSibling(); // ให้ตัวหนังสืออยู่บนแถบหัวข้อ

        var controller = panel.GetComponent<TimeScaleButtons>();
        if (controller == null) controller = Undo.AddComponent<TimeScaleButtons>(panel.gameObject);
        controller.manager = manager;
        controller.headerText = header;
        controller.accentColor = accent;
        controller.labelColor = LabelColor;
        controller.entries.Clear();

        int count = TimeScales.Length;
        float buttonWidth = (RightWidth - pad * 2f - gap * (count - 1)) / count;
        for (int i = 0; i < count; i++)
        {
            float scale = TimeScales[i];
            bool active = Mathf.Approximately(scale, 1f);
            float x = pad + i * (buttonWidth + gap);

            var fill = AddImage(panel, "TimeButton_" + scale + "x", null,
                active ? new Color(accent.r, accent.g, accent.b, 0.32f) : controller.idleFill);
            fill.raycastTarget = true;
            SetRect(fill.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -buttonTop), new Vector2(buttonWidth, buttonHeight));

            var frame = AddImage(fill.transform, "Frame", frameSprite, active ? accent : controller.idleFrame);
            frame.type = Image.Type.Sliced;
            frame.pixelsPerUnitMultiplier = 1f;
            Stretch(frame.rectTransform);

            var label = AddText(fill.transform, "Label", fontBold, 21f, active ? accent : Color.white, TextAlignmentOptions.Center, 1f);
            TopStretch(label.rectTransform, 6f, 26f, 2f, 2f);
            label.text = scale.ToString("0") + "x";

            var sub = AddText(fill.transform, "Sub", fontLight, 10f, active ? accent : LabelColor, TextAlignmentOptions.Center, 3f);
            TopStretch(sub.rectTransform, 32f, 14f, 1f, 1f);
            sub.text = TimeScaleNames[i];

            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            UnityEventTools.AddFloatPersistentListener(button.onClick, controller.SetScale, scale);
            EditorUtility.SetDirty(button);

            controller.entries.Add(new TimeScaleButtons.Entry { scale = scale, button = button, fill = fill, frame = frame, label = label, subLabel = sub });
        }
        EditorUtility.SetDirty(controller);

        // เส้นคั่น + บรรทัดสถานะ MQTT
        var sep = AddImage(panel, "Sep", null, SeparatorColor);
        TopStretch(sep.rectTransform, statusTop - 8f, 1f, 14f, 14f);

        var dot = AddImage(panel, "MqttDot", null, new Color(0.94f, 0.33f, 0.31f));
        SetRect(dot.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(18f, -statusTop - 11f), new Vector2(8f, 8f));

        var status = AddText(panel, "MqttStatus", fontBody, 14f, Color.white, TextAlignmentOptions.Left, 2f);
        TopStretch(status.rectTransform, statusTop, 22f, 34f, 16f);
        status.text = "<align=left>MQTT  <color=#F0544F>OFF</color><line-height=0>\n<align=right><color=#" + ColorUtility.ToHtmlStringRGB(LabelColor) + ">press Play to connect</color>";

        return SetupMqttBridge(manager, status, dot);
    }

    // ใส่ MqttParkingBridge ไว้ที่ GameObject เดียวกับ ParkingLotManager (GameManager)
    private static MqttParkingBridge SetupMqttBridge(ParkingLotManager manager, TMP_Text status, Image dot)
    {
        var bridge = manager.GetComponent<MqttParkingBridge>();
        if (bridge == null) bridge = Undo.AddComponent<MqttParkingBridge>(manager.gameObject);
        else Undo.RecordObject(bridge, "MQTT Bridge");

        bridge.manager = manager;
        bridge.spawner = manager.GetComponent<SlotCarSpawner>();
        bridge.statusText = status;
        bridge.statusDot = dot;
        EditorUtility.SetDirty(bridge);
        return bridge;
    }

    private static float TimePanelHeight()
    {
        const float buttonTop = HeaderHeight + 10f, buttonHeight = 52f;
        float statusTop = buttonTop + buttonHeight + 18f;
        return statusTop + 22f + 12f;
    }

    // ==========================================================
    // EVENT LOG: เหตุการณ์ล่าสุดอยู่บนสุด (RX / TX / ACK / NET / PARK / GATE / WARN)
    // ==========================================================
    private const string EventLogPanelName = "EventLog_Panel";

    [MenuItem("Tools/Smart Parking/Build Event Log Panel")]
    public static void BuildEventLogMenu()
    {
        if (!LoadAssets()) return;
        Canvas canvas = FindCanvas();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Smart Parking HUD", "ไม่พบ Canvas ในฉากที่เปิดอยู่", "OK");
            return;
        }
        SetupCanvas(canvas);
        BuildEventLog(canvas.transform);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Smart Parking] Event log panel built. กด Ctrl+S เพื่อบันทึกฉาก");
    }

    private static RectTransform BuildEventLog(Transform root)
    {
        var manager = Object.FindFirstObjectByType<ParkingLotManager>();
        int slotCount = manager != null ? manager.Slots.Count : 7;

        if (FindDeep(root, EventLogPanelName) == null)
        {
            var go = new GameObject(EventLogPanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root, false);
            Undo.RegisterCreatedObjectUndo(go, "Event Log Panel");
        }

        float top = ColumnTop + SlotPanelHeight(slotCount) + Gap + TimePanelHeight() + Gap;
        var panel = PlacePanel(root, EventLogPanelName, new Vector2(1, 1), new Vector2(-Margin, -top), new Vector2(RightWidth, 300f), true);
        if (panel == null) return null;

        // ยืดลงไปถึงขอบล่างจอ (จอสูงแค่ไหนก็เต็มพอดี)
        panel.anchorMin = new Vector2(1, 0);
        panel.anchorMax = new Vector2(1, 1);
        panel.pivot = new Vector2(1, 1);
        panel.offsetMin = new Vector2(-Margin - RightWidth, Margin);
        panel.offsetMax = new Vector2(-Margin, -top);

        var header = AddText(panel, "Header", fontBold, 18f, Color.white, TextAlignmentOptions.Left, 8f);
        Header(panel, header, "EVENT LOG  <color=#" + ColorUtility.ToHtmlStringRGB(LabelColor) + ">|  NEWEST FIRST</color>", 0f);
        header.transform.SetAsLastSibling();

        var body = AddText(panel, "Body", fontBody, 15f, Color.white, TextAlignmentOptions.TopLeft, 0.5f);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.overflowMode = TextOverflowModes.Truncate;
        body.lineSpacing = 4f;
        Stretch(body.rectTransform);
        body.rectTransform.offsetMin = new Vector2(18f, 12f);
        body.rectTransform.offsetMax = new Vector2(-14f, -(HeaderHeight + 10f));
        body.text = "<color=#" + ColorUtility.ToHtmlStringRGB(LabelColor) + ">waiting for events...</color>";

        var log = panel.GetComponent<EventLogPanel>();
        if (log == null) log = Undo.AddComponent<EventLogPanel>(panel.gameObject);
        else Undo.RecordObject(log, "Event Log Panel");
        log.body = body;
        log.manager = manager;
        EditorUtility.SetDirty(log);

        // ปุ่ม CLEAR มุมขวาของหัวแผง
        var clear = AddImage(panel, "ClearButton", null, new Color(0.08f, 0.09f, 0.11f, 0.92f));
        clear.raycastTarget = true;
        SetRect(clear.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-10f, -7f), new Vector2(72f, 28f));
        var clearFrame = AddImage(clear.transform, "Frame", frameSprite, new Color(1f, 1f, 1f, 0.45f));
        clearFrame.type = Image.Type.Sliced;
        clearFrame.pixelsPerUnitMultiplier = 1f;
        Stretch(clearFrame.rectTransform);
        var clearLabel = AddText(clear.transform, "Label", fontBold, 12f, Color.white, TextAlignmentOptions.Center, 3f);
        Stretch(clearLabel.rectTransform);
        clearLabel.text = "CLEAR";
        clear.transform.SetAsLastSibling();

        var button = clear.gameObject.AddComponent<Button>();
        button.targetGraphic = clearFrame;
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        UnityEventTools.AddPersistentListener(button.onClick, log.Clear);
        EditorUtility.SetDirty(button);

        return panel;
    }

    // ==========================================================
    // ภาพ 3D ตรงกลาง: กล้องวาดเฉพาะช่องว่างระหว่างคอลัมน์ (ไม่โดน UI บัง) กด H ซ่อน/แสดงคอลัมน์
    // ==========================================================
    private static void SetupViewport(Canvas canvas, RectTransform[] left, RectTransform[] right, RectTransform[] top)
    {
        var fitter = canvas.GetComponent<HudViewportFitter>();
        if (fitter == null) fitter = Undo.AddComponent<HudViewportFitter>(canvas.gameObject);
        else Undo.RecordObject(fitter, "HUD Viewport");

        Camera cam = Camera.main;
        if (cam == null)
        {
            var follow = Object.FindFirstObjectByType<CameraFollow>();
            if (follow != null) cam = follow.GetComponent<Camera>();
        }
        if (cam == null) Debug.LogWarning("[Smart Parking] ไม่พบกล้องหลัก (Main Camera) — ใส่ที่ HudViewportFitter > Target Camera เอง");

        fitter.targetCamera = cam;
        fitter.leftColumn = System.Array.FindAll(left, r => r != null);
        fitter.rightColumn = System.Array.FindAll(right, r => r != null);
        fitter.topBar = System.Array.FindAll(top, r => r != null);
        fitter.gap = Gap;
        fitter.bottomMargin = Margin;
        fitter.frameSprite = frameSprite;
        fitter.frameColor = new Color(accent.r, accent.g, accent.b, 0.55f);
        EditorUtility.SetDirty(fitter);
    }

    // ==========================================================
    // โหลดฟอนต์/สไปรต์/สีธีมจาก SlimUI
    // ==========================================================
    private static bool LoadAssets()
    {
        fontBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SlimRoot + "Fonts/Poppins-Bold SDF.asset");
        fontLight = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SlimRoot + "Fonts/Poppins-Light SDF.asset");
        fontBody = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SlimRoot + "Fonts/RUBIK-MEDIUM SDF.asset");
        frameSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlimRoot + "Graphics/Buttons/Button Fram 256px.png");
        gradientSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlimRoot + "Graphics/Buttons/Button Highlighted 256px.png");

        accent = new Color(1f, 0.686f, 0f);
        var theme = AssetDatabase.LoadAssetAtPath<SlimUI.ModernMenu.ThemedUIData>(ThemePath);
        if (theme != null && theme.custom1 != null)
        {
            accent = theme.custom1.graphic1;
            accent.a = 1f;
        }

        if (fontBold == null || fontLight == null || fontBody == null || frameSprite == null || gradientSprite == null)
        {
            EditorUtility.DisplayDialog("Smart Parking HUD",
                "ไม่พบฟอนต์หรือกราฟิกของ SlimUI Modern Menu 1\nตรวจว่ามีโฟลเดอร์ " + SlimRoot + " อยู่ในโปรเจค", "OK");
            return false;
        }
        return true;
    }

    private static Canvas FindCanvas()
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go.name == "Canvas" && go.TryGetComponent(out Canvas named)) return named;
        }
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var c = go.GetComponentInChildren<Canvas>(true);
            if (c != null) return c;
        }
        return null;
    }

    private static void SetupCanvas(Canvas canvas)
    {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = false;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f; // ผสมกว้าง/สูงเท่าๆ กัน => จอ 4:3, 16:9, 21:9 ดูสมดุล
        scaler.referencePixelsPerUnit = 100f;
    }

    // ==========================================================
    // Panel / Header / Row
    // ==========================================================
    private static RectTransform PlacePanel(Transform root, string name, Vector2 corner, Vector2 position, Vector2 size, bool framed)
    {
        var rt = FindDeep(root, name) as RectTransform;
        if (rt == null)
        {
            Debug.LogWarning($"[Smart Parking] ไม่พบ {name} ใน Canvas");
            return null;
        }

        ClearHud(rt);
        if (rt.parent != root) rt.SetParent(root, false);
        SetRect(rt, corner, corner, corner, position, size);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        var img = rt.GetComponent<Image>();
        if (img == null) img = rt.gameObject.AddComponent<Image>();
        img.sprite = null;
        img.type = Image.Type.Simple;
        img.color = PanelFill;
        img.raycastTarget = false;

        if (framed)
        {
            var frame = AddImage(rt, "Frame", frameSprite, FrameTint);
            frame.type = Image.Type.Sliced;
            frame.pixelsPerUnitMultiplier = 1f;
            Stretch(frame.rectTransform);
            Corners(rt);
        }
        return rt;
    }

    private static void Header(RectTransform panel, TMP_Text text, string label, float top)
    {
        var bg = AddImage(panel, "HeaderBG", gradientSprite, new Color(accent.r, accent.g, accent.b, 0.45f));
        TopStretch(bg.rectTransform, top + 2f, HeaderHeight - 2f, 2f, 2f);
        AccentBar(panel, top + 2f, HeaderHeight - 2f, 4f, false);

        if (text == null) text = AddText(panel, "Header", fontBold, 18f, Color.white, TextAlignmentOptions.Left, 8f);
        Style(text, fontBold, 18f, Color.white, TextAlignmentOptions.Left, 8f);
        TopStretch(text.rectTransform, top + 1f, HeaderHeight, 20f, 14f);
        text.text = label;
    }

    private static void Rows(RectTransform panel, float firstTop, params string[] textNames)
    {
        for (int i = 0; i < textNames.Length; i++)
        {
            float top = firstTop + i * RowStep;
            var t = Child<TMP_Text>(panel, textNames[i]);
            if (t == null) continue;
            Style(t, fontBody, 21f, Color.white, TextAlignmentOptions.Left, 2f);
            TopStretch(t.rectTransform, top, RowHeight, 20f, 20f);

            if (i < textNames.Length - 1)
            {
                var sep = AddImage(panel, "Sep", null, SeparatorColor);
                TopStretch(sep.rectTransform, top + RowHeight + 1f, 1f, 18f, 18f);
            }
        }
    }

    // แถบสีเน้นด้านซ้าย (height <= 0 = ยืดเต็มความสูง)
    private static void AccentBar(RectTransform panel, float top, float height, float width, bool fullHeight)
    {
        var bar = AddImage(panel, "Accent", null, accent);
        if (fullHeight)
        {
            bar.rectTransform.anchorMin = new Vector2(0, 0);
            bar.rectTransform.anchorMax = new Vector2(0, 1);
            bar.rectTransform.pivot = new Vector2(0, 0.5f);
            bar.rectTransform.anchoredPosition = Vector2.zero;
            bar.rectTransform.sizeDelta = new Vector2(width, 0f);
        }
        else
        {
            SetRect(bar.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(2f, -top), new Vector2(width, height));
        }
    }

    // มุมแบบ HUD สีเน้น (บนซ้าย + ล่างขวา)
    private static void Corners(RectTransform panel)
    {
        const float len = 18f, thick = 2f;
        CornerLine(panel, "CornerTL_H", new Vector2(0, 1), new Vector2(len, thick));
        CornerLine(panel, "CornerTL_V", new Vector2(0, 1), new Vector2(thick, len));
        CornerLine(panel, "CornerBR_H", new Vector2(1, 0), new Vector2(len, thick));
        CornerLine(panel, "CornerBR_V", new Vector2(1, 0), new Vector2(thick, len));
    }

    private static void CornerLine(RectTransform panel, string name, Vector2 corner, Vector2 size)
    {
        var img = AddImage(panel, name, null, accent);
        SetRect(img.rectTransform, corner, corner, corner, Vector2.zero, size);
    }

    // ==========================================================
    // ต่อสาย field ใหม่ของ SmartParkingUI + วัสดุไฟกระพริบ
    // ==========================================================
    private static void WireUI(TMP_Text availDetail, RectTransform barFill, TMP_Text clock, RectTransform liveDot)
    {
        var ui = Object.FindFirstObjectByType<SmartParkingUI>();
        if (ui == null)
        {
            Debug.LogWarning("[Smart Parking] ไม่พบ SmartParkingUI ในฉาก (ข้ามการต่อสาย)");
            return;
        }

        var so = new SerializedObject(ui);
        SetRef(so, "availabilityDetailText", availDetail);
        SetRef(so, "availabilityBarFill", barFill);
        SetRef(so, "clockText", clock);
        SetRef(so, "liveIndicator", liveDot != null ? liveDot.GetComponent<Image>() : null);
        var accentProp = so.FindProperty("accentColor");
        if (accentProp != null) accentProp.colorValue = accent;
        so.ApplyModifiedProperties();

        // ใส่ข้อความตัวอย่างแบบใหม่ให้เห็นหน้าตาตั้งแต่ใน Editor (ตอน Play จะถูกแทนด้วยค่าจริง)
        ui.ResetAllUI();
    }

    private static void SetRef(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p != null) p.objectReferenceValue = value;
    }

    private static void SetupBlinkMaterial()
    {
        var off = AssetDatabase.LoadAssetAtPath<Material>(BlinkOffMaterialPath);
        if (off == null) return;
        foreach (var sensor in Object.FindObjectsByType<ParkingSensor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (sensor.blinkOffMaterial != null) continue;
            var so = new SerializedObject(sensor);
            so.FindProperty("blinkOffMaterial").objectReferenceValue = off;
            so.ApplyModifiedProperties();
        }
    }

    // ==========================================================
    // Helpers
    // ==========================================================
    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static T Child<T>(Transform panel, string name) where T : Component
    {
        var t = FindDeep(panel, name);
        if (t == null)
        {
            Debug.LogWarning($"[Smart Parking] ไม่พบ {name} ใน {panel.name}");
            return null;
        }
        if (t.parent != panel) t.SetParent(panel, false);
        return t.GetComponent<T>();
    }

    private static void ClearHud(Transform panel)
    {
        for (int i = panel.childCount - 1; i >= 0; i--)
        {
            var child = panel.GetChild(i);
            if (child.name.StartsWith(Prefix)) Object.DestroyImmediate(child.gameObject);
        }
    }

    // ของตกแต่งถูกวางไว้ "ก่อน" ข้อความเดิม เพื่อให้วาดอยู่ด้านหลังตัวหนังสือ
    private static Image AddImage(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(Prefix + name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.SetSiblingIndex(CountHudChildren(parent) - 1);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static TMP_Text AddText(Transform parent, string name, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align, float spacing)
    {
        var go = new GameObject(Prefix + name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        Style(t, font, size, color, align, spacing);
        return t;
    }

    private static int CountHudChildren(Transform parent)
    {
        int n = 0;
        foreach (Transform c in parent) if (c.name.StartsWith(Prefix)) n++;
        return n;
    }

    private static void Style(TMP_Text t, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align, float spacing)
    {
        t.font = font;
        t.fontSharedMaterial = font.material;
        t.fontSize = size;
        t.enableAutoSizing = false;
        t.fontStyle = FontStyles.Normal;
        t.color = color;
        t.alignment = align;
        t.characterSpacing = spacing;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.margin = Vector4.zero;
        t.raycastTarget = false;
        t.rectTransform.localScale = Vector3.one;
        t.rectTransform.localRotation = Quaternion.identity;
    }

    private static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    // ยืดเต็มความกว้างของ Panel โดยวัดระยะจากขอบบน
    private static void TopStretch(RectTransform rt, float top, float height, float left, float right)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(left, -top - height);
        rt.offsetMax = new Vector2(-right, -top);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
