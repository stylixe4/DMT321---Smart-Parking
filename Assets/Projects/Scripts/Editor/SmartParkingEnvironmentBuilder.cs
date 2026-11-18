using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// สร้างฉากเมืองแบบ Low-poly รอบลานจอดรถ + ท้องฟ้า/แสง/หมอก/Post-processing
// เมนู: Tools > Smart Parking > Build City Environment + Sky
// - ของทั้งหมดอยู่ใต้ GameObject "Environment" กดซ้ำได้ (ลบของเก่าแล้วสร้างใหม่) และ Ctrl+Z ได้
// - วางของไว้นอกพื้นลานจอด (Ground) ทั้งหมด ไม่ทับทางวิ่งของรถ
// - Mesh/Material/Texture ที่สร้างจะเก็บไว้ที่ Assets/Projects/Environment
public static class SmartParkingEnvironmentBuilder
{
    private const string RootName = "Environment";
    private const string VolumeName = "Global Volume (City)";
    private const string Dir = "Assets/Projects/Environment";
    private const string MatDir = Dir + "/Materials";
    private const string TexDir = Dir + "/Textures";
    private const string MeshAssetPath = Dir + "/EnvironmentMeshes.asset";
    private const string SkyMaterialPath = Dir + "/StylizedSky.mat";
    private const string VolumeProfilePath = Dir + "/CityPostFX.asset";
    private const string SignFontPath = "Assets/SlimUI/Modern Menu 1/Fonts/Poppins-Bold SDF.asset";

    private const float SW = 3.5f;   // ความกว้างทางเท้า
    private const float RW = 14f;    // ความกว้างถนนใหญ่ (2 เลน)
    private const float SoiW = 8f;   // ความกว้างซอย

    private static Transform root;
    private static System.Random rng;
    private static Shader lit;
    private static Mesh cube, prism, cone, cylinder;
    private static Mesh[] blobs;
    private static List<Mesh> generated;
    private static int meshCounter;
    private static readonly Dictionary<string, Material> M = new Dictionary<string, Material>();
    private static Material[] officeFacades, homeFacades, awnings, leaves, signs, houseRoofs;

    // ขอบเขตลานจอด (คำนวณจาก Ground)
    private static float lx0, lx1, lz0, lz1, top, cx, cz, sTop, rTop, soiZ0, soiZ1;

    [MenuItem("Tools/Smart Parking/Build City Environment + Sky")]
    public static void Build()
    {
        var ground = GameObject.Find("Ground");
        var groundRenderer = ground != null ? ground.GetComponent<Renderer>() : null;
        if (groundRenderer == null)
        {
            EditorUtility.DisplayDialog("Smart Parking", "ไม่พบ GameObject ชื่อ Ground (พื้นลานจอด) ในฉาก", "OK");
            return;
        }

        lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            EditorUtility.DisplayDialog("Smart Parking", "ไม่พบ Shader: Universal Render Pipeline/Lit", "OK");
            return;
        }

        Bounds b = groundRenderer.bounds;
        lx0 = b.min.x; lx1 = b.max.x; lz0 = b.min.z; lz1 = b.max.z;
        top = b.max.y; cx = b.center.x; cz = b.center.z;
        sTop = top + 0.1f;          // ผิวทางเท้า (สูงกว่าลานจอดนิดหน่อย)
        rTop = top - 0.004f;        // ผิวถนน
        soiZ1 = lz0 - SW;           // ซอยด้านใต้ลานจอด
        soiZ0 = soiZ1 - SoiW;

        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        EnsureFolder(Dir);
        EnsureFolder(MatDir);
        EnsureFolder(TexDir);
        AssetDatabase.DeleteAsset(MeshAssetPath);

        rng = new System.Random(20261118);
        generated = new List<Mesh>();
        meshCounter = 0;

        EditorUtility.DisplayProgressBar("Smart Parking", "Creating materials...", 0.1f);
        try
        {
            CreateMaterials();
            CreateBaseMeshes();

            var rootGo = new GameObject(RootName);
            root = rootGo.transform;

            EditorUtility.DisplayProgressBar("Smart Parking", "Ground & roads...", 0.25f);
            BuildGroundAndRoads();
            EditorUtility.DisplayProgressBar("Smart Parking", "Buildings...", 0.45f);
            BuildShophouses();
            BuildHouses();
            BuildOffices();
            BuildParkAndApartments();
            BuildSkyline();
            EditorUtility.DisplayProgressBar("Smart Parking", "Street furniture...", 0.7f);
            BuildStreetFurniture();
            BuildBoothAndSign();

            EditorUtility.DisplayProgressBar("Smart Parking", "Saving meshes...", 0.85f);
            SaveGeneratedMeshes();
            SetStatic(rootGo);
            SetupSkyAndLighting();
            SetupPostProcessing();

            Undo.RegisterCreatedObjectUndo(rootGo, "Build City Environment");
            Selection.activeGameObject = rootGo;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Smart Parking] City environment + sky built. กด Ctrl+S เพื่อบันทึกฉาก");
    }

    [MenuItem("Tools/Smart Parking/Remove City Environment")]
    public static void Remove()
    {
        var old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        var vol = GameObject.Find(VolumeName);
        if (vol != null) Undo.DestroyObjectImmediate(vol);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // ==========================================================
    // พื้นหญ้า ทางเท้า ถนน เส้นจราจร
    // ==========================================================
    private static void BuildGroundAndRoads()
    {
        var g = Group("Ground & Roads", root, Vector3.zero, Quaternion.identity);
        Box("Grass", g, new Vector3(cx, -0.5f, cz), new Vector3(700f, 1f, 700f), M["Grass"], true);

        // ทางเท้ารอบลานจอด
        Slab("Sidewalk_North", g, lx0 - SW, lx1 - SW, lz1, lz1 + SW, sTop, M["Sidewalk"], true);
        Slab("Sidewalk_South", g, lx0 - SW, lx1 - SW, lz0 - SW, lz0, sTop, M["Sidewalk"], true);
        Slab("Sidewalk_West", g, lx0 - SW, lx0, lz0, lz1, sTop, M["Sidewalk"], true);
        // ทางเดินหน้าตึกแถว
        Slab("Walkway_Shophouses", g, lx0 - SW, lx1 - SW, lz1 + SW + 2f, lz1 + SW + 4f, sTop, M["Sidewalk"], true);
        // ทางเท้าริมถนนใหญ่ ฝั่งลานจอด (เว้นทางเข้าลานจอดและปากซอย)
        Slab("Sidewalk_Road_North", g, lx1 - SW, lx1, lz1, cz + 300f, sTop, M["Sidewalk"], true);
        Slab("Sidewalk_Road_Corner", g, lx1 - SW, lx1, lz0 - SW, lz0, sTop, M["Sidewalk"], true);
        Slab("Sidewalk_Road_South", g, lx1 - SW, lx1, cz - 300f, soiZ0, sTop, M["Sidewalk"], true);
        // ทางเท้าฝั่งตรงข้ามถนนใหญ่ และฝั่งใต้ของซอย
        Slab("Sidewalk_Road_Far", g, lx1 + RW, lx1 + RW + SW, cz - 300f, cz + 300f, sTop, M["Sidewalk"], true);
        Slab("Sidewalk_Soi_South", g, lx0 - SW - 70f, lx1 - SW, soiZ0 - SW, soiZ0, sTop, M["Sidewalk"], true);

        // ขอบคันหิน
        float kTop = top + 0.13f;
        Slab("Curb", g, lx0, lx1, lz1, lz1 + 0.25f, kTop, M["Curb"], false);
        Slab("Curb", g, lx0, lx1, lz0 - 0.25f, lz0, kTop, M["Curb"], false);
        Slab("Curb", g, lx0 - 0.25f, lx0, lz0, lz1, kTop, M["Curb"], false);
        Slab("Curb", g, lx1 - 0.25f, lx1, lz1, cz + 300f, kTop, M["Curb"], false);
        Slab("Curb", g, lx1 - 0.25f, lx1, lz0 - SW, lz0, kTop, M["Curb"], false);
        Slab("Curb", g, lx1 - 0.25f, lx1, cz - 300f, soiZ0, kTop, M["Curb"], false);
        Slab("Curb", g, lx1 + RW, lx1 + RW + 0.25f, cz - 300f, cz + 300f, kTop, M["Curb"], false);
        Slab("Curb", g, lx0 - SW - 70f, lx1, soiZ1, soiZ1 + 0.25f, kTop, M["Curb"], false);
        Slab("Curb", g, lx0 - SW - 70f, lx1 - 0.25f, soiZ0 - 0.25f, soiZ0, kTop, M["Curb"], false);

        // ถนน
        Slab("Road_Main", g, lx1, lx1 + RW, cz - 300f, cz + 300f, rTop, M["Road"], true);
        Slab("Road_Soi", g, lx0 - SW - 70f, lx1, soiZ0, soiZ1, rTop, M["Road"], true);

        // เส้นจราจร
        var marks = Group("Road Markings", g, Vector3.zero, Quaternion.identity);
        float mY = rTop + 0.006f;
        float roadMid = lx1 + RW * 0.5f;
        for (float z = cz - 150f; z < cz + 150f; z += 6f)
        {
            if (z > lz1 && z < lz1 + 5f) continue; // ทางม้าลาย
            Box("CenterDash", marks, new Vector3(roadMid, mY, z + 1.5f), new Vector3(0.18f, 0.012f, 3f), M["LineWhite"]);
        }
        Box("EdgeLine", marks, new Vector3(lx1 + RW - 0.45f, mY, cz), new Vector3(0.15f, 0.012f, 300f), M["LineWhite"]);
        Line(marks, lx1 + 0.45f, lz1 + 0.5f, cz + 150f, mY);
        Line(marks, lx1 + 0.45f, cz - 150f, soiZ0 - 0.5f, mY);

        float soiMid = (soiZ0 + soiZ1) * 0.5f;
        for (float x = lx0 - SW - 70f; x < lx1 - 6f; x += 4f)
        {
            Box("SoiDash", marks, new Vector3(x + 1f, mY, soiMid), new Vector3(2f, 0.012f, 0.15f), M["LineYellow"]);
        }

        // ทางม้าลาย: ข้ามถนนใหญ่ และ ปากซอย
        for (float x = lx1 + 0.9f; x < lx1 + RW - 0.5f; x += 1.1f)
        {
            Box("Zebra", marks, new Vector3(x, mY, lz1 + 2.5f), new Vector3(0.55f, 0.012f, 3f), M["LineWhite"]);
        }
        for (float z = soiZ0 + 0.7f; z < soiZ1 - 0.4f; z += 1.1f)
        {
            Box("Zebra", marks, new Vector3(lx1 - 2.2f, mY, z), new Vector3(3f, 0.012f, 0.55f), M["LineWhite"]);
        }
    }

    // ==========================================================
    // อาคาร
    // ==========================================================
    // ตึกแถวฝั่งเหนือ หันหน้าเข้าลานจอด
    private static void BuildShophouses()
    {
        var g = Group("Shophouses", root, Vector3.zero, Quaternion.identity);
        float zFront = lz1 + SW + 4f;
        float x = lx0 - SW + 0.5f;
        float xEnd = lx1 - SW - 1f;
        int inBlock = 0, blockSize = RI(3, 6);
        const float w = 4.8f, d = 12f, floorH = 3.2f;

        while (x + w <= xEnd)
        {
            int floors = RI(3, 5);
            var b = Building(g, "Shophouse", new Vector3(x + w * 0.5f, 0f, zFront + d * 0.5f), 0f,
                w, d, floors, floorH, 2.4f, Pick(homeFacades), M["Roof"], true, true, out float h);

            Box("ShopGlass", b, new Vector3(0f, 1.35f, -d * 0.5f - 0.06f), new Vector3(w - 0.7f, 2.5f, 0.1f), M["Glass"]);
            var awning = Box("Awning", b, new Vector3(0f, 2.95f, -d * 0.5f - 0.75f), new Vector3(w - 0.2f, 0.1f, 1.5f), Pick(awnings));
            awning.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            Box("SignBoard", b, new Vector3(0f, 3.55f, -d * 0.5f - 0.08f), new Vector3(w - 0.5f, 0.55f, 0.1f), Pick(signs));

            for (int f = 1; f < floors; f++)
            {
                if (rng.NextDouble() > 0.6) continue;
                Box("Balcony", b, new Vector3(0f, f * floorH + 0.05f, -d * 0.5f - 0.45f), new Vector3(w - 0.9f, 0.12f, 0.9f), M["Roof"]);
                Box("Railing", b, new Vector3(0f, f * floorH + 0.5f, -d * 0.5f - 0.86f), new Vector3(w - 0.9f, 0.8f, 0.06f), M["Metal"]);
            }
            if (rng.NextDouble() < 0.45)
                Obj("WaterTank", b, cylinder, M["TankBlue"], new Vector3(R(-1f, 1f), h + 0.3f, R(1f, 4f)), Quaternion.identity, new Vector3(1.2f, 1.4f, 1.2f));

            x += w;
            if (++inBlock >= blockSize)
            {
                x += 1.6f;
                inBlock = 0;
                blockSize = RI(3, 6);
            }
        }
    }

    // บ้านเดี่ยวฝั่งใต้ซอย หันหน้าเข้าซอย
    private static void BuildHouses()
    {
        var g = Group("Houses", root, Vector3.zero, Quaternion.identity);
        var trees = Group("House Trees", g, Vector3.zero, Quaternion.identity);
        float zFront = soiZ0 - SW - 4f;
        float x = lx0 - SW - 40f;
        float xEnd = lx1 - SW - 2f;
        const float d = 8f;

        while (x + 8f < xEnd)
        {
            float w = R(8f, 10.5f);
            int floors = rng.NextDouble() < 0.55 ? 2 : 1;
            var b = Building(g, "House", new Vector3(x + w * 0.5f, 0f, zFront - d * 0.5f), 180f,
                w, d, floors, 2.9f, 2.7f, Pick(homeFacades), M["Roof"], false, true, out float h);

            bool ridgeAlongWidth = rng.NextDouble() < 0.6;
            float roofH = R(1.6f, 2.4f);
            Obj("Roof", b, prism, Pick(houseRoofs), new Vector3(0f, h, 0f),
                ridgeAlongWidth ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity,
                ridgeAlongWidth ? new Vector3(d + 0.9f, roofH, w + 0.9f) : new Vector3(w + 0.9f, roofH, d + 0.9f));

            float doorX = R(-w * 0.25f, w * 0.25f);
            Box("Door", b, new Vector3(doorX, 1.05f, -d * 0.5f - 0.04f), new Vector3(1f, 2.1f, 0.08f), M["Door"]);
            Box("Porch", b, new Vector3(doorX, 0.08f, -d * 0.5f - 0.8f), new Vector3(2.2f, 0.16f, 1.6f), M["Sidewalk"]);
            Box("Path", b, new Vector3(doorX, 0.03f, -d * 0.5f - 2.6f), new Vector3(1.4f, 0.06f, 2.4f), M["Sidewalk"]);

            // รั้วหน้าบ้าน เว้นช่องประตู
            float fz = -d * 0.5f - 3.8f, left = -w * 0.5f - 1f, right = w * 0.5f + 1f;
            Box("Fence", b, new Vector3((left + doorX - 1.2f) * 0.5f, 0.45f, fz), new Vector3(doorX - 1.2f - left, 0.9f, 0.15f), M["White"], true);
            Box("Fence", b, new Vector3((doorX + 1.2f + right) * 0.5f, 0.45f, fz), new Vector3(right - doorX - 1.2f, 0.9f, 0.15f), M["White"], true);

            Vector3 lawn = b.TransformPoint(new Vector3(doorX > 0f ? -w * 0.3f : w * 0.3f, 0f, -d * 0.5f - 2.2f));
            if (rng.NextDouble() < 0.65) Tree(trees, lawn, 0.8f);
            else Bush(trees, lawn, new Vector3(1.6f, 1.1f, 1.6f), null);

            x += w + R(3.5f, 5.5f);
        }
    }

    // อาคารสำนักงานฝั่งตรงข้ามถนนใหญ่
    private static void BuildOffices()
    {
        var g = Group("Offices", root, Vector3.zero, Quaternion.identity);
        float xFront = lx1 + RW + SW + 3f;
        float z = cz - 95f;

        while (z < cz + 95f)
        {
            float w = R(14f, 22f), d = R(14f, 20f);
            int floors = RI(4, 13);
            var facade = Pick(officeFacades);
            var b = Building(g, "Office", new Vector3(xFront + d * 0.5f, 0f, z + w * 0.5f), 90f,
                w, d, floors, 3.5f, 3f, facade, M["Roof"], true, true, out float h);

            Box("LobbyGlass", b, new Vector3(0f, 1.9f, -d * 0.5f - 0.06f), new Vector3(w * 0.55f, 3.4f, 0.1f), M["Glass"]);
            Box("Canopy", b, new Vector3(0f, 3.75f, -d * 0.5f - 1.1f), new Vector3(w * 0.55f + 1f, 0.2f, 2.2f), M["Metal"]);

            if (floors >= 9 && rng.NextDouble() < 0.6)
            {
                int extra = RI(3, 8);
                float tw = w * 0.6f, td = d * 0.6f, th = extra * 3.5f;
                var towerMesh = BuildingMesh(tw, th, td, Mathf.Max(1, Mathf.RoundToInt(tw / 3f)), extra, Mathf.Max(1, Mathf.RoundToInt(td / 3f)));
                Obj("Tower", b, towerMesh, new[] { facade, M["Roof"] }, new Vector3(0f, h + 0.3f, 0f), Quaternion.identity, Vector3.one, true);
                Box("TowerCap", b, new Vector3(0f, h + 0.3f + th + 0.15f, 0f), new Vector3(tw + 0.3f, 0.3f, td + 0.3f), M["Roof"]);
            }
            else
            {
                int units = RI(1, 4);
                for (int i = 0; i < units; i++)
                {
                    var size = new Vector3(R(1.5f, 3f), R(0.8f, 1.6f), R(1.5f, 3f));
                    Box("RoofUnit", b, new Vector3(R(-w / 3f, w / 3f), h + 0.3f + size.y * 0.5f, R(-d / 3f, d / 3f)), size, M["Metal"]);
                }
            }
            z += w + R(4f, 8f);
        }
    }

    // สวนสาธารณะหลังลานจอด + อพาร์ตเมนต์
    private static void BuildParkAndApartments()
    {
        var g = Group("Park", root, Vector3.zero, Quaternion.identity);
        float px0 = lx0 - SW - 30f, px1 = lx0 - SW;
        float pz0 = soiZ1 + 0.25f, pz1 = lz1 + SW + 22f;
        float pathX = px0 + 14f;

        Slab("ParkLawn", g, px0, px1, pz0, pz1, 0.02f, M["GrassPark"], false);
        Slab("Path_EW", g, px0, px1, cz - 1.2f, cz + 1.2f, 0.06f, M["Sidewalk"], false);
        Slab("Path_NS", g, pathX - 1.2f, pathX + 1.2f, pz0, pz1, 0.06f, M["Sidewalk"], false);

        // บ่อน้ำ
        Vector3 pond = new Vector3(px0 + 6.5f, 0f, cz - 7.5f);
        Obj("PondRim", g, cylinder, M["Curb"], pond, Quaternion.identity, new Vector3(9f, 0.12f, 7f));
        Obj("Pond", g, cylinder, M["Water"], pond + new Vector3(0f, 0.02f, 0f), Quaternion.identity, new Vector3(8.2f, 0.12f, 6.2f));
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI * 2f / 6f + 0.3f;
            Bush(g, pond + new Vector3(Mathf.Cos(a) * 5.2f, 0f, Mathf.Sin(a) * 4.2f), new Vector3(0.9f, 0.7f, 0.9f), i % 2 == 0 ? M["FlowerPink"] : M["FlowerYellow"]);
        }

        // ศาลาพักผ่อน
        var sala = Group("Sala", g, new Vector3(pathX + 7f, 0f, cz + 7.5f), Quaternion.identity);
        Box("Platform", sala, new Vector3(0f, 0.2f, 0f), new Vector3(5f, 0.4f, 5f), M["White"], true);
        for (int i = 0; i < 4; i++)
        {
            float sx = (i % 2 == 0 ? -1f : 1f) * 2.1f, sz = (i < 2 ? -1f : 1f) * 2.1f;
            Box("Post", sala, new Vector3(sx, 1.6f, sz), new Vector3(0.25f, 2.4f, 0.25f), M["Wood"], true);
        }
        Box("Beam", sala, new Vector3(0f, 2.85f, 0f), new Vector3(5.2f, 0.15f, 5.2f), M["Wood"]);
        Obj("Roof", sala, prism, M["RoofRed"], new Vector3(0f, 2.9f, 0f), Quaternion.identity, new Vector3(6.2f, 1.9f, 6.2f));
        Bench(sala, new Vector3(0f, 0.4f, 1.6f), 0f);

        // ม้านั่ง + ถังขยะ ริมทางเดิน
        var furniture = Group("Park Furniture", g, Vector3.zero, Quaternion.identity);
        float[] benchX = { px0 + 4f, px0 + 9f, pathX + 5f, pathX + 11f };
        foreach (float bx in benchX)
        {
            Bench(furniture, new Vector3(bx, 0.06f, cz + 2f), 0f);
            Obj("Bin", furniture, cylinder, M["AwningGreen"], new Vector3(bx + 1.4f, 0.06f, cz + 2f), Quaternion.identity, new Vector3(0.5f, 0.85f, 0.5f));
        }
        for (float z = pz0 + 6f; z < pz1 - 3f; z += 12f)
        {
            if (Mathf.Abs(z - cz) < 4f) continue;
            Lamp(furniture, new Vector3(pathX + 1.6f, 0.06f, z), 90f);
        }

        // ต้นไม้ในสวน (สุ่มแบบไม่ทับทางเดิน/บ่อน้ำ/ศาลา)
        var trees = Group("Park Trees", g, Vector3.zero, Quaternion.identity);
        var placed = new List<Vector3>();
        int attempts = 0;
        while (placed.Count < 28 && attempts++ < 600)
        {
            var p = new Vector3(R(px0 + 1.5f, px1 - 2f), 0f, R(pz0 + 1.5f, pz1 - 1.5f));
            if (Mathf.Abs(p.z - cz) < 2.8f || Mathf.Abs(p.x - pathX) < 2.8f) continue;
            if (Vector3.Distance(p, pond) < 6.5f || Vector3.Distance(p, sala.position) < 5.5f) continue;
            bool tooClose = false;
            foreach (var q in placed) if (Vector3.Distance(p, q) < 4f) { tooClose = true; break; }
            if (tooClose) continue;
            placed.Add(p);
            Tree(trees, p, R(0.9f, 1.3f));
        }
        // แนวพุ่มไม้ขอบสวน
        for (float z = pz0 + 1f; z < lz1 + SW; z += 2.4f)
        {
            if (Mathf.Abs(z - cz) < 2f) continue;
            Bush(trees, new Vector3(px1 - 0.9f, 0f, z), new Vector3(1.7f, 1.1f, 1.7f), null);
        }

        // อพาร์ตเมนต์ด้านหลังสวน หันหน้าเข้าสวน
        var apts = Group("Apartments", root, Vector3.zero, Quaternion.identity);
        float xFront = px0 - 6f;
        float z0 = pz0 - 6f;
        for (int i = 0; i < 3; i++)
        {
            float w = R(18f, 22f), d = 12f;
            int floors = RI(6, 10);
            var b = Building(apts, "Apartment", new Vector3(xFront - d * 0.5f, 0f, z0 + w * 0.5f), -90f,
                w, d, floors, 3f, 2.6f, Pick(homeFacades), M["Roof"], true, true, out float h);
            for (int f = 1; f < floors; f++)
                Box("BalconyBand", b, new Vector3(0f, f * 3f + 0.05f, -d * 0.5f - 0.5f), new Vector3(w - 1f, 0.14f, 1f), M["White"]);
            Obj("WaterTank", b, cylinder, M["TankBlue"], new Vector3(R(-w / 4f, w / 4f), h + 0.3f, R(-2f, 2f)), Quaternion.identity, new Vector3(1.6f, 1.8f, 1.6f));
            z0 += w + 8f;
        }
    }

    // ตึกสูงไกลๆ เป็นเส้นขอบฟ้าของเมือง
    private static void BuildSkyline()
    {
        var g = Group("Skyline", root, Vector3.zero, Quaternion.identity);
        var center = new Vector3(cx, 0f, cz);
        const int count = 42;
        for (int i = 0; i < count; i++)
        {
            float ang = i * Mathf.PI * 2f / count + R(-0.05f, 0.05f);
            float r = R(165f, 235f);
            var p = new Vector3(cx + Mathf.Cos(ang) * r, 0f, cz + Mathf.Sin(ang) * r);
            Vector3 f = (center - p).normalized;
            float yaw = Mathf.Atan2(-f.x, -f.z) * Mathf.Rad2Deg;
            float w = R(18f, 34f), d = R(16f, 30f);
            int floors = RI(8, 26);
            var b = Building(g, "Tower", p, yaw, w, d, floors, 3.5f, 3f, Pick(officeFacades), M["Roof"], true, false, out float h);
            if (rng.NextDouble() < 0.3)
                Box("Antenna", b, new Vector3(0f, h + 0.3f + 5f, 0f), new Vector3(0.35f, 10f, 0.35f), M["Metal"]);
        }
    }

    // ==========================================================
    // ต้นไม้ เสาไฟ ม้านั่ง ริมถนน
    // ==========================================================
    private static void BuildStreetFurniture()
    {
        var g = Group("Street Furniture", root, Vector3.zero, Quaternion.identity);
        var trees = Group("Street Trees", g, Vector3.zero, Quaternion.identity);
        var lamps = Group("Street Lamps", g, Vector3.zero, Quaternion.identity);
        var benches = Group("Benches", g, Vector3.zero, Quaternion.identity);
        float boothX = BoothX();

        // แถบต้นไม้หน้าตึกแถว + ม้านั่ง
        float stripZ = lz1 + SW + 1f;
        int k = 0;
        for (float x = lx0 - SW + 2.5f; x < lx1 - SW - 1f; x += 7f, k++)
        {
            Tree(trees, new Vector3(x, 0f, stripZ), 1f);
            if (k % 2 == 1 && x + 3.5f < lx1 - SW - 1f)
            {
                Bench(benches, new Vector3(x + 3.5f, 0f, stripZ), 0f);
                Obj("Bin", benches, cylinder, M["AwningGreen"], new Vector3(x + 5f, 0f, stripZ), Quaternion.identity, new Vector3(0.5f, 0.85f, 0.5f));
            }
        }

        // ต้นไม้ในกระถางบนทางเท้าฝั่งใต้
        float planterZ = lz0 - SW + 0.9f;
        for (float x = lx0 + 1f; x < lx1 - SW - 2f; x += 8f)
        {
            if (Mathf.Abs(x - boothX) < 3f) continue;
            Box("Planter", trees, new Vector3(x, sTop + 0.175f, planterZ), new Vector3(1.2f, 0.35f, 1.2f), M["Curb"], true);
            Tree(trees, new Vector3(x, sTop + 0.35f, planterZ), 0.85f);
        }

        // ริมถนนใหญ่
        for (float z = cz - 120f; z < cz + 120f; z += 10f)
        {
            if (z > lz1 && z < lz1 + 6f) continue;
            Tree(trees, new Vector3(lx1 + RW + SW - 1f, sTop, z), 1.1f);
            bool nearSideOk = z > lz1 + 6f || z < soiZ0 - 3f;
            if (nearSideOk) Tree(trees, new Vector3(lx1 - SW + 1f, sTop, z + 5f), 1f);
        }
        for (float z = cz - 120f; z < cz + 120f; z += 18f)
        {
            Lamp(lamps, new Vector3(lx1 + RW + 0.7f, sTop, z), 90f);
            if (z > lz1 + 1f || z < soiZ0 - 1f) Lamp(lamps, new Vector3(lx1 - 0.7f, sTop, z + 9f), -90f);
        }

        // ซอยด้านใต้
        for (float x = lx0 - SW - 60f; x < lx1 - SW - 2f; x += 9f)
            Tree(trees, new Vector3(x, sTop, soiZ0 - SW + 1f), 0.95f);
        for (float x = lx0 - SW - 56f; x < lx1 - SW - 2f; x += 16f)
            Lamp(lamps, new Vector3(x, sTop, soiZ0 - 0.7f), 180f);

        // เสาไฟรอบลานจอด (หันโคมเข้าลานจอด)
        for (float x = lx0 - SW + 6f; x < lx1 - SW - 1f; x += 14f)
            Lamp(lamps, new Vector3(x, sTop, lz1 + SW - 0.6f), 0f);
        for (float x = lx0 + 5f; x < lx1 - SW - 1f; x += 14f)
        {
            if (Mathf.Abs(x - boothX) < 3f) continue;
            Lamp(lamps, new Vector3(x, sTop, lz0 - SW + 0.6f), 180f);
        }
        Lamp(lamps, new Vector3(lx0 - SW + 0.6f, sTop, cz), -90f);
    }

    // ป้อมยาม + ป้าย P
    private static void BuildBoothAndSign()
    {
        var booth = Group("Guard Booth", root, new Vector3(BoothX(), sTop, lz0 - SW * 0.5f), Quaternion.identity);
        Box("Body", booth, new Vector3(0f, 1.25f, 0f), new Vector3(2.2f, 2.5f, 2f), M["White"], true);
        Box("Windows", booth, new Vector3(0f, 1.6f, 0f), new Vector3(2.26f, 0.9f, 2.06f), M["Glass"]);
        Box("Stripe", booth, new Vector3(0f, 0.3f, 0f), new Vector3(2.24f, 0.25f, 2.04f), M["AwningOrange"]);
        Box("Roof", booth, new Vector3(0f, 2.62f, 0f), new Vector3(2.8f, 0.22f, 2.6f), M["SignBlue"]);
        Box("Door", booth, new Vector3(0.5f, 1.05f, -1.03f), new Vector3(0.8f, 2f, 0.06f), M["Door"]);
        Bush(booth, new Vector3(-1.6f, 0f, -0.6f), new Vector3(0.8f, 0.6f, 0.8f), M["FlowerPink"]);

        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SignFontPath);
        var sign = Group("Parking Sign", root, new Vector3(lx1 - 1.2f, sTop, lz0 - 1.4f), Quaternion.Euler(0f, -90f, 0f));
        Obj("Pole", sign, cylinder, M["Pole"], Vector3.zero, Quaternion.identity, new Vector3(0.14f, 3.6f, 0.14f), true);
        Box("Panel", sign, new Vector3(0f, 3.3f, -0.12f), new Vector3(1.3f, 1.3f, 0.1f), M["SignBlue"]);
        Box("Panel2", sign, new Vector3(0f, 2.4f, -0.12f), new Vector3(1.3f, 0.42f, 0.1f), M["White"]);
        Text3D(sign, "P", new Vector3(0f, 3.3f, -0.18f), new Vector2(1.1f, 1.1f), Color.white, font);
        Text3D(sign, "SMART PARKING", new Vector3(0f, 2.4f, -0.18f), new Vector2(1.15f, 0.3f), new Color(0.13f, 0.38f, 0.75f), font);
    }

    private static float BoothX()
    {
        var gate = GameObject.Find("Auto gate barrier enter");
        return gate != null ? gate.transform.position.x + 2.2f : cx;
    }

    // ==========================================================
    // ชิ้นส่วนสำเร็จรูป
    // ==========================================================
    private static Transform Building(Transform parent, string name, Vector3 pos, float yaw, float w, float d, int floors,
        float floorH, float cellW, Material facade, Material roof, bool flatRoofCap, bool collider, out float height)
    {
        height = floors * floorH;
        int cols = Mathf.Max(1, Mathf.RoundToInt(w / cellW));
        int colsD = Mathf.Max(1, Mathf.RoundToInt(d / cellW));
        var g = Group(name, parent, pos, Quaternion.Euler(0f, yaw, 0f));
        Obj("Body", g, BuildingMesh(w, height, d, cols, floors, colsD), new[] { facade, roof }, Vector3.zero, Quaternion.identity, Vector3.one, collider);
        if (flatRoofCap)
            Box("RoofCap", g, new Vector3(0f, height + 0.15f, 0f), new Vector3(w + 0.3f, 0.3f, d + 0.3f), roof);
        return g;
    }

    private static void Tree(Transform parent, Vector3 basePos, float scale)
    {
        var t = Group("Tree", parent, basePos, Quaternion.Euler(0f, R(0f, 360f), 0f));
        float s = scale * R(0.85f, 1.2f);
        bool pine = rng.NextDouble() < 0.25;
        float trunkH = (pine ? 1.2f : 1.9f) * s;
        Obj("Trunk", t, cylinder, M["Trunk"], Vector3.zero, Quaternion.identity, new Vector3(0.32f * s, trunkH, 0.32f * s), true);
        if (pine)
        {
            Obj("Cone1", t, cone, M["Pine"], new Vector3(0f, trunkH * 0.8f, 0f), Quaternion.identity, new Vector3(2.6f * s, 2.8f * s, 2.6f * s));
            Obj("Cone2", t, cone, M["Pine"], new Vector3(0f, trunkH * 0.8f + 1.6f * s, 0f), Quaternion.Euler(0f, 25f, 0f), new Vector3(2f * s, 2.4f * s, 2f * s));
        }
        else
        {
            var leaf = Pick(leaves);
            Obj("Leaves", t, Pick(blobs), leaf, new Vector3(0f, trunkH + 1f * s, 0f), Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(2.8f * s, 2.4f * s, 2.8f * s));
            if (rng.NextDouble() < 0.7)
                Obj("Leaves2", t, Pick(blobs), leaf, new Vector3(R(-0.6f, 0.6f) * s, trunkH + 1.9f * s, R(-0.6f, 0.6f) * s),
                    Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(1.9f * s, 1.7f * s, 1.9f * s));
        }
    }

    private static void Bush(Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        Obj("Bush", parent, Pick(blobs), mat != null ? mat : Pick(leaves), pos + new Vector3(0f, size.y * 0.35f, 0f),
            Quaternion.Euler(0f, R(0f, 360f), 0f), size);
    }

    // โคมไฟยื่นไปทางแกน -Z ของตัวเอง (yaw 0 = ยื่นไปทาง -Z, 180 = +Z, 90 = -X, -90 = +X)
    private static void Lamp(Transform parent, Vector3 basePos, float yaw)
    {
        var l = Group("StreetLamp", parent, basePos, Quaternion.Euler(0f, yaw, 0f));
        Obj("Pole", l, cylinder, M["Pole"], Vector3.zero, Quaternion.identity, new Vector3(0.16f, 4.6f, 0.16f), true);
        Box("Arm", l, new Vector3(0f, 4.5f, -0.6f), new Vector3(0.1f, 0.1f, 1.3f), M["Pole"]);
        Box("Head", l, new Vector3(0f, 4.42f, -1.15f), new Vector3(0.36f, 0.14f, 0.6f), M["LampHead"]);
    }

    // ม้านั่งหันหน้าไปทาง -Z ของตัวเอง
    private static void Bench(Transform parent, Vector3 pos, float yaw)
    {
        var b = Group("Bench", parent, pos, Quaternion.Euler(0f, yaw, 0f));
        Box("Seat", b, new Vector3(0f, 0.45f, 0f), new Vector3(1.8f, 0.08f, 0.5f), M["Wood"]);
        Box("Back", b, new Vector3(0f, 0.78f, 0.24f), new Vector3(1.8f, 0.5f, 0.06f), M["Wood"]);
        Box("LegL", b, new Vector3(-0.75f, 0.22f, 0f), new Vector3(0.08f, 0.44f, 0.5f), M["Metal"]);
        Box("LegR", b, new Vector3(0.75f, 0.22f, 0f), new Vector3(0.08f, 0.44f, 0.5f), M["Metal"]);
    }

    private static void Text3D(Transform parent, string text, Vector3 localPos, Vector2 box, Color color, TMP_FontAsset font)
    {
        var go = new GameObject("Text_" + text, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.1f;
        tmp.fontSizeMax = 72f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.margin = Vector4.zero;
        var rt = tmp.rectTransform;
        rt.sizeDelta = box;
        rt.localPosition = localPos;
        rt.localRotation = Quaternion.identity;
    }

    private static void Line(Transform parent, float x, float z0, float z1, float y)
    {
        if (z1 <= z0) return;
        Box("EdgeLine", parent, new Vector3(x, y, (z0 + z1) * 0.5f), new Vector3(0.15f, 0.012f, z1 - z0), M["LineWhite"]);
    }

    // แผ่นพื้นสี่เหลี่ยมจากพื้นหญ้า (y=0) ขึ้นไปถึง topY
    private static void Slab(string name, Transform parent, float x0, float x1, float z0, float z1, float topY, Material mat, bool collider)
    {
        if (x1 <= x0 || z1 <= z0) return;
        Box(name, parent, new Vector3((x0 + x1) * 0.5f, topY * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, topY, z1 - z0), mat, collider);
    }

    private static Transform Group(string name, Transform parent, Vector3 localPos, Quaternion localRot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        return go.transform;
    }

    private static GameObject Box(string name, Transform parent, Vector3 localCenter, Vector3 size, Material mat, bool collider = false)
    {
        return Obj(name, parent, cube, mat, localCenter, Quaternion.identity, size, collider);
    }

    private static GameObject Obj(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale, bool collider = false)
    {
        return Obj(name, parent, mesh, new[] { mat }, pos, rot, scale, collider);
    }

    private static GameObject Obj(string name, Transform parent, Mesh mesh, Material[] mats, Vector3 pos, Quaternion rot, Vector3 scale, bool collider)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        if (collider) go.AddComponent<BoxCollider>();
        return go;
    }

    private static void SetStatic(GameObject rootGo)
    {
        foreach (var t in rootGo.GetComponentsInChildren<Transform>(true))
        {
            if (t.GetComponent<TMP_Text>() != null) continue;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }
    }

    // ==========================================================
    // วัสดุ + Texture หน้าต่าง
    // ==========================================================
    private static void CreateMaterials()
    {
        M.Clear();
        Mat("Grass", C(0.49f, 0.70f, 0.37f));
        Mat("GrassPark", C(0.42f, 0.66f, 0.34f));
        Mat("Sidewalk", C(0.83f, 0.81f, 0.77f));
        Mat("Curb", C(0.64f, 0.63f, 0.61f));
        Mat("Road", C(0.25f, 0.26f, 0.28f), 0.25f);
        Mat("LineWhite", C(0.95f, 0.95f, 0.93f));
        Mat("LineYellow", C(0.96f, 0.78f, 0.25f));
        Mat("Roof", C(0.60f, 0.61f, 0.62f));
        Mat("RoofRed", C(0.72f, 0.32f, 0.24f));
        Mat("RoofDark", C(0.34f, 0.37f, 0.41f));
        Mat("RoofTeal", C(0.24f, 0.50f, 0.52f));
        Mat("Trunk", C(0.47f, 0.33f, 0.22f));
        Mat("Leaf1", C(0.36f, 0.62f, 0.29f));
        Mat("Leaf2", C(0.48f, 0.72f, 0.33f));
        Mat("Leaf3", C(0.29f, 0.53f, 0.31f));
        Mat("Pine", C(0.23f, 0.46f, 0.30f));
        Mat("Pole", C(0.22f, 0.24f, 0.27f), 0.4f);
        Mat("LampHead", C(1f, 0.95f, 0.82f), 0.3f, null, new Color(1.6f, 1.3f, 0.8f, 1f));
        Mat("Wood", C(0.66f, 0.46f, 0.29f));
        Mat("Metal", C(0.38f, 0.40f, 0.43f), 0.45f);
        Mat("Glass", C(0.22f, 0.32f, 0.42f), 0.85f);
        Mat("Door", C(0.45f, 0.30f, 0.20f));
        Mat("White", C(0.95f, 0.95f, 0.94f));
        Mat("SignBlue", C(0.13f, 0.38f, 0.75f));
        Mat("Water", C(0.33f, 0.62f, 0.78f), 0.9f);
        Mat("FlowerPink", C(0.95f, 0.55f, 0.66f));
        Mat("FlowerYellow", C(0.98f, 0.84f, 0.35f));
        Mat("TankBlue", C(0.25f, 0.45f, 0.75f), 0.5f);
        awnings = new[]
        {
            Mat("AwningRed", C(0.86f, 0.31f, 0.28f)),
            Mat("AwningBlue", C(0.25f, 0.48f, 0.82f)),
            Mat("AwningGreen", C(0.26f, 0.62f, 0.41f)),
            Mat("AwningOrange", C(0.95f, 0.62f, 0.20f))
        };
        leaves = new[] { M["Leaf1"], M["Leaf2"], M["Leaf3"] };
        signs = new[] { M["White"], M["FlowerYellow"], M["SignBlue"], M["AwningOrange"] };
        houseRoofs = new[] { M["RoofRed"], M["RoofDark"], M["RoofTeal"] };

        var officeColors = new[]
        {
            ("White", C(0.93f, 0.93f, 0.91f)), ("Slate", C(0.62f, 0.68f, 0.74f)), ("Sky", C(0.68f, 0.79f, 0.89f)),
            ("Sand", C(0.88f, 0.80f, 0.66f)), ("Charcoal", C(0.45f, 0.48f, 0.53f))
        };
        officeFacades = new Material[officeColors.Length];
        for (int i = 0; i < officeColors.Length; i++)
        {
            string key = "Facade_Office_" + officeColors[i].Item1;
            officeFacades[i] = Mat(key, Color.white, 0.25f, FacadeTexture(key, officeColors[i].Item2, true));
        }

        var homeColors = new[]
        {
            ("Cream", C(0.96f, 0.91f, 0.80f)), ("Terracotta", C(0.86f, 0.56f, 0.43f)), ("Sage", C(0.68f, 0.77f, 0.62f)),
            ("Lavender", C(0.78f, 0.74f, 0.86f)), ("Peach", C(0.97f, 0.78f, 0.64f)), ("Mint", C(0.70f, 0.86f, 0.80f))
        };
        homeFacades = new Material[homeColors.Length];
        for (int i = 0; i < homeColors.Length; i++)
        {
            string key = "Facade_Home_" + homeColors[i].Item1;
            homeFacades[i] = Mat(key, Color.white, 0.12f, FacadeTexture(key, homeColors[i].Item2, false));
        }
    }

    private static Material Mat(string key, Color color, float smoothness = 0.12f, Texture2D tex = null, Color? emission = null)
    {
        string path = $"{MatDir}/{key}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(lit);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = lit;
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", 0f);
        m.SetTexture("_BaseMap", tex);
        m.SetTexture("_MainTex", tex);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        M[key] = m;
        return m;
    }

    // Texture หน้าต่าง 1 ช่อง (1 ช่อง = กว้าง 1 ช่องหน้าต่าง x สูง 1 ชั้น) แล้วให้ UV ของตึกวนซ้ำ
    private static Texture2D FacadeTexture(string name, Color wall, bool office)
    {
        string path = $"{TexDir}/{name}.png";
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var glassLow = new Color(0.16f, 0.23f, 0.31f);
        var glassHigh = new Color(0.40f, 0.52f, 0.64f);
        var frame = new Color(0.94f, 0.94f, 0.93f);
        var floorLine = wall * 0.84f;
        var sill = wall * 0.72f;
        float wx0 = office ? 0.08f : 0.24f, wx1 = office ? 0.92f : 0.76f;
        float wy0 = office ? 0.20f : 0.28f, wy1 = office ? 0.88f : 0.80f;
        const float f = 0.025f;

        var pixels = new Color[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                Color c = wall;
                if (v < 0.035f) c = floorLine;
                if (!office && v > wy0 - 0.07f && v < wy0 - 0.02f && u > wx0 - 0.06f && u < wx1 + 0.06f) c = sill;
                bool inFrame = u > wx0 - f && u < wx1 + f && v > wy0 - f && v < wy1 + f;
                bool inGlass = u > wx0 && u < wx1 && v > wy0 && v < wy1;
                if (inFrame) c = frame;
                if (inGlass)
                {
                    float gv = (v - wy0) / (wy1 - wy0);
                    c = Color.Lerp(glassLow, glassHigh, gv * gv);
                    float diag = (u - wx0) + (v - wy0) * 0.6f;
                    if (Mathf.Repeat(diag, 0.55f) < 0.06f) c = Color.Lerp(c, Color.white, 0.22f);
                    if (office && Mathf.Abs(u - 0.5f) < 0.012f) c = frame;
                    if (!office && Mathf.Abs(v - (wy0 + wy1) * 0.5f) < 0.012f) c = frame;
                }
                c.a = 1f;
                pixels[y * S + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ==========================================================
    // Mesh แบบ Low-poly (flat shading)
    // ==========================================================
    private sealed class MeshBuilder
    {
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int>[] subMeshes;

        public MeshBuilder(int subMeshCount)
        {
            subMeshes = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++) subMeshes[i] = new List<int>();
        }

        // สามเหลี่ยมที่หันออกจากจุด center เสมอ (ใช้กับรูปทรงนูน)
        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 center)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, (a + b + c) / 3f - center) < 0f)
            {
                (b, c) = (c, b);
                (ub, uc) = (uc, ub);
                n = -n;
            }
            n.Normalize();
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
            subMeshes[sub].Add(i); subMeshes[sub].Add(i + 1); subMeshes[sub].Add(i + 2);
        }

        public void Quad(int sub, Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Vector2 uvSize, Vector3 center)
        {
            Tri(sub, bl, tl, tr, Vector2.zero, new Vector2(0f, uvSize.y), uvSize, center);
            Tri(sub, bl, tr, br, Vector2.zero, uvSize, new Vector2(uvSize.x, 0f), center);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subMeshes.Length;
            for (int i = 0; i < subMeshes.Length; i++) mesh.SetTriangles(subMeshes[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // กล่องตึก: submesh 0 = ผนัง (UV เป็นจำนวนช่องหน้าต่าง/ชั้น), submesh 1 = หลังคา — จุดหมุนอยู่ที่ฐานกลางตึก
    private static Mesh BuildingMesh(float w, float h, float d, int cols, int rows, int colsD)
    {
        var mb = new MeshBuilder(2);
        float x = w * 0.5f, z = d * 0.5f;
        var c = new Vector3(0f, h * 0.5f, 0f);
        var uvFront = new Vector2(cols, rows);
        var uvSide = new Vector2(colsD, rows);
        mb.Quad(0, new Vector3(-x, 0, -z), new Vector3(-x, h, -z), new Vector3(x, h, -z), new Vector3(x, 0, -z), uvFront, c);
        mb.Quad(0, new Vector3(x, 0, z), new Vector3(x, h, z), new Vector3(-x, h, z), new Vector3(-x, 0, z), uvFront, c);
        mb.Quad(0, new Vector3(x, 0, -z), new Vector3(x, h, -z), new Vector3(x, h, z), new Vector3(x, 0, z), uvSide, c);
        mb.Quad(0, new Vector3(-x, 0, z), new Vector3(-x, h, z), new Vector3(-x, h, -z), new Vector3(-x, 0, -z), uvSide, c);
        mb.Quad(1, new Vector3(-x, h, -z), new Vector3(-x, h, z), new Vector3(x, h, z), new Vector3(x, h, -z), Vector2.one, c);
        return Track(mb.ToMesh("Building_" + (meshCounter++)));
    }

    private static void CreateBaseMeshes()
    {
        cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        prism = Track(MakePrism());
        cone = Track(MakeCone(7));
        cylinder = Track(MakeCylinder(8));
        blobs = new[] { Track(MakeBlob(11)), Track(MakeBlob(23)), Track(MakeBlob(37)) };
    }

    // หลังคาจั่ว: กว้าง 1 (X) สูง 1 ลึก 1 (Z) สันหลังคาตามแกน Z
    private static Mesh MakePrism()
    {
        var mb = new MeshBuilder(1);
        var c = new Vector3(0f, 0.35f, 0f);
        var bl = new Vector3(-0.5f, 0f, -0.5f); var br = new Vector3(0.5f, 0f, -0.5f);
        var fl = new Vector3(-0.5f, 0f, 0.5f); var fr = new Vector3(0.5f, 0f, 0.5f);
        var r0 = new Vector3(0f, 1f, -0.5f); var r1 = new Vector3(0f, 1f, 0.5f);
        mb.Quad(0, fl, r1, r0, bl, Vector2.one, c);
        mb.Quad(0, br, r0, r1, fr, Vector2.one, c);
        mb.Tri(0, bl, r0, br, Vector2.zero, Vector2.zero, Vector2.zero, c);
        mb.Tri(0, fr, r1, fl, Vector2.zero, Vector2.zero, Vector2.zero, c);
        return mb.ToMesh("RoofPrism");
    }

    private static Mesh MakeCone(int segments)
    {
        var mb = new MeshBuilder(1);
        var apex = new Vector3(0f, 1f, 0f);
        var c = new Vector3(0f, 0.3f, 0f);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f);
            var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
            mb.Tri(0, p0, apex, p1, Vector2.zero, Vector2.zero, Vector2.zero, c);
            mb.Tri(0, p0, p1, Vector3.zero, Vector2.zero, Vector2.zero, Vector2.zero, c);
        }
        return mb.ToMesh("LowPolyCone");
    }

    private static Mesh MakeCylinder(int segments)
    {
        var mb = new MeshBuilder(1);
        var c = new Vector3(0f, 0.5f, 0f);
        var topCenter = new Vector3(0f, 1f, 0f);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            var b0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f);
            var b1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f);
            var t0 = b0 + Vector3.up;
            var t1 = b1 + Vector3.up;
            mb.Quad(0, b0, t0, t1, b1, Vector2.one, c);
            mb.Tri(0, t0, t1, topCenter, Vector2.zero, Vector2.zero, Vector2.zero, c);
        }
        return mb.ToMesh("LowPolyCylinder");
    }

    // ทรงกลมเหลี่ยม (icosphere) บิดเล็กน้อยให้ดูเป็นพุ่มไม้ธรรมชาติ
    private static Mesh MakeBlob(int seed)
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var verts = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
        };
        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
        int[] faces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };

        var cache = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
            cache[key] = verts.Count - 1;
            return verts.Count - 1;
        }

        var sub = new List<int>();
        for (int i = 0; i < faces.Length; i += 3)
        {
            int a = faces[i], b = faces[i + 1], c = faces[i + 2];
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            sub.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }

        var r = new System.Random(seed);
        for (int i = 0; i < verts.Count; i++)
            verts[i] = verts[i] * (0.5f * (0.88f + (float)r.NextDouble() * 0.24f));

        var mb = new MeshBuilder(1);
        for (int i = 0; i < sub.Count; i += 3)
            mb.Tri(0, verts[sub[i]], verts[sub[i + 1]], verts[sub[i + 2]], Vector2.zero, Vector2.zero, Vector2.zero, Vector3.zero);
        return mb.ToMesh("LowPolyBlob_" + seed);
    }

    private static Mesh Track(Mesh mesh)
    {
        generated.Add(mesh);
        return mesh;
    }

    private static void SaveGeneratedMeshes()
    {
        if (generated.Count == 0) return;
        AssetDatabase.CreateAsset(generated[0], MeshAssetPath);
        for (int i = 1; i < generated.Count; i++) AssetDatabase.AddObjectToAsset(generated[i], MeshAssetPath);
        AssetDatabase.SaveAssets();
    }

    // ==========================================================
    // ท้องฟ้า แสง หมอก Post-processing
    // ==========================================================
    private static void SetupSkyAndLighting()
    {
        Light sun = RenderSettings.sun;
        if (sun == null)
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { sun = l; break; }
            }
        }

        var shader = Shader.Find("SmartParking/Stylized Sky");
        if (shader != null)
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (sky == null)
            {
                sky = new Material(shader);
                AssetDatabase.CreateAsset(sky, SkyMaterialPath);
            }
            sky.shader = shader;
            if (sun != null) sky.SetVector("_SunDirection", -sun.transform.forward);
            sky.SetFloat("_CloudScale", 1.8f);
            sky.SetFloat("_CloudCoverage", 0.5f);
            sky.SetFloat("_CloudSoftness", 0.22f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }
        else
        {
            Debug.LogWarning("[Smart Parking] ไม่พบ Shader 'SmartParking/Stylized Sky' — ข้ามการเปลี่ยน Skybox");
        }

        if (sun != null)
        {
            Undo.RecordObject(sun, "City Lighting");
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            RenderSettings.sun = sun;
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.64f, 0.76f, 0.92f);
        RenderSettings.ambientEquatorColor = new Color(0.74f, 0.78f, 0.78f);
        RenderSettings.ambientGroundColor = new Color(0.42f, 0.42f, 0.38f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.80f, 0.87f, 0.93f);
        RenderSettings.fogStartDistance = 90f;
        RenderSettings.fogEndDistance = 340f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        DynamicGI.UpdateEnvironment();

        var cam = Camera.main;
        if (cam != null && cam.farClipPlane < 700f)
        {
            Undo.RecordObject(cam, "City Lighting");
            cam.farClipPlane = 700f;
        }
    }

    private static void SetupPostProcessing()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);
        }

        var tone = GetVolumeComponent<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.Neutral);
        var color = GetVolumeComponent<ColorAdjustments>(profile);
        color.postExposure.Override(0.15f);
        color.contrast.Override(10f);
        color.saturation.Override(12f);
        var bloom = GetVolumeComponent<Bloom>(profile);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(0.45f);
        bloom.scatter.Override(0.6f);
        var vignette = GetVolumeComponent<Vignette>(profile);
        vignette.intensity.Override(0.2f);
        vignette.smoothness.Override(0.45f);
        EditorUtility.SetDirty(profile);

        var volumeGo = GameObject.Find(VolumeName);
        if (volumeGo == null)
        {
            volumeGo = new GameObject(VolumeName);
            Undo.RegisterCreatedObjectUndo(volumeGo, "City Post FX");
        }
        var volume = volumeGo.GetComponent<Volume>();
        if (volume == null) volume = volumeGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;

        var cam = Camera.main;
        if (cam != null)
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null)
            {
                Undo.RecordObject(data, "City Post FX");
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
            }
        }
    }

    private static T GetVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet(out T component))
        {
            component = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(component, profile);
        }
        return component;
    }

    // ==========================================================
    // Utilities
    // ==========================================================
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);
    private static float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);
    private static int RI(int min, int maxExclusive) => rng.Next(min, maxExclusive);
    private static T Pick<T>(T[] items) => items[rng.Next(items.Length)];
}
