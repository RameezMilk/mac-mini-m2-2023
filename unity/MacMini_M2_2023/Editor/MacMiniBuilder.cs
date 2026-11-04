// Mac mini (2023, M2) procedural model builder.
// SPDX-License-Identifier: CC-BY-4.0
//
// Tools > Mac mini M2 > Build            regenerate meshes, materials, prefab and preview scene
// Tools > Mac mini M2 > Export GLB       export the prefab root to a self-contained .glb (needs glTFast)
// Tools > Mac mini M2 > Capture Review Views   render PNG review shots
//
// Conventions: meters, Y-up, front faces +Z, pivot at bottom-centre on the floor plane.
// "u" below is the horizontal position along the rear panel as seen from behind
// (left to right), which is +X in Unity.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if MACMINI_GLTFAST
using GLTFast;
using GLTFast.Export;
#endif

namespace MacMiniM2
{
    public static class MacMiniBuilder
    {
        // ---------- Paths ----------
        const string RootName = "MacMini_M2_2023";
        const string ExportPrefKey = "MacMiniM2.ExportPath";

        static string Folder
        {
            get
            {
                // Resolve the folder this script lives in so the asset folder can be dropped anywhere under Assets/.
                var guids = AssetDatabase.FindAssets("MacMiniBuilder t:MonoScript");
                foreach (var g in guids)
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (p.EndsWith("/Editor/MacMiniBuilder.cs"))
                        return p.Substring(0, p.Length - "/Editor/MacMiniBuilder.cs".Length);
                }
                return "Assets/MacMini_M2_2023";
            }
        }

        static string PrefabPath => Folder + "/" + RootName + ".prefab";
        static string ScenePath => Folder + "/" + RootName + "_Preview.unity";

        // ---------- Dimensions (meters) ----------
        // Apple spec: 197 mm x 197 mm x 35.8 mm.
        const float W = 0.197f, D = 0.197f, H = 0.0358f;
        const float R = 0.0155f;   // plan-view corner radius (measured from reference photos)
        const float Y0 = 0.0050f;  // underside of aluminium shell (height of black base)
        const float RB = 0.0030f;  // bottom edge fillet
        const float RT = 0.0015f;  // top edge fillet
        const int NC = 24;         // segments per plan corner
        const int MB = 6, MT = 5;  // fillet segments

        const float CX = W * 0.5f - R; // half-length of straight sides
        const float CZ = D * 0.5f - R;
        const float ZR = -D * 0.5f;    // rear face plane
        const float PanelY0 = Y0 + RB, PanelY1 = H - RT;
        const float PanelBorder = 0.0007f;
        const float CellMargin = 0.0008f;

        const float BaseRBottom = 0.0805f, BaseRTop = 0.0845f;

        static readonly Vector3 LedPos = new Vector3(-0.062f, 0.0115f, D * 0.5f); // front-right as seen from the front

        // ---------- Materials ----------
        enum Mat { Aluminum, PanelDark, PortShell, PortInterior, PortTongue, ContactMetal, BaseRubber, StatusLED }
        static Dictionary<Mat, Material> mats;

        // ---------- Ports (mm, rear view left->right) ----------
        enum Kind { Button, ACInlet, Ethernet, Thunderbolt, HDMI, USBA, Headphone, Vent }

        struct Port
        {
            public string name; public Kind kind;
            public float u, y, w, h, r; public int k; public float chamfer, depth; public Mat wall;
            public Port(string name, Kind kind, float u, float y, float w, float h, float r, int k, float depth, Mat wall, float chamfer = 0f)
            {
                this.name = name; this.kind = kind; this.u = u * 0.001f; this.y = y * 0.001f; this.w = w * 0.001f; this.h = h * 0.001f;
                this.r = r * 0.001f; this.k = k; this.depth = depth * 0.001f; this.wall = wall; this.chamfer = chamfer;
            }
        }

        // Base M2 rear I/O (two Thunderbolt 4). Styling follows Apple's 2023 rear panel; spacing approximated.
        static readonly Port[] Ports =
        {
            new Port("PowerButton",        Kind.Button,      -75.5f, 21.5f,  8.6f,  8.6f, 4.3f,  8,  0.8f, Mat.PortInterior),
            new Port("Port_Power",         Kind.ACInlet,     -58.0f, 21.5f, 18.5f,  9.5f, 4.75f, 6,  6.5f, Mat.PortInterior),
            new Port("Port_Ethernet",      Kind.Ethernet,    -37.0f, 21.5f, 15.0f, 12.5f, 0.4f,  2, 12.0f, Mat.PortInterior),
            new Port("Port_Thunderbolt_1", Kind.Thunderbolt, -18.0f, 26.5f,  8.9f,  3.2f, 1.6f,  4,  7.0f, Mat.PortShell),
            new Port("Port_Thunderbolt_2", Kind.Thunderbolt,  -3.0f, 26.5f,  8.9f,  3.2f, 1.6f,  4,  7.0f, Mat.PortShell),
            new Port("Port_HDMI",          Kind.HDMI,         22.0f, 26.5f, 14.5f,  4.6f, 0.35f, 2,  8.0f, Mat.PortShell, 0.22f),
            new Port("Port_USB_A_1",       Kind.USBA,         45.0f, 26.5f, 12.5f,  5.2f, 0.35f, 2,  9.0f, Mat.PortShell),
            new Port("Port_USB_A_2",       Kind.USBA,         62.0f, 26.5f, 12.5f,  5.2f, 0.35f, 2,  9.0f, Mat.PortShell),
            new Port("Port_Headphone",     Kind.Headphone,    53.5f, 17.0f,  3.6f,  3.6f, 1.8f,  6, 10.0f, Mat.PortInterior),
            new Port("RearVent",           Kind.Vent,          3.0f, 15.0f, 58.0f,  7.5f, 3.75f, 6,  6.0f, Mat.PortInterior),
        };

        // ======================================================================
        // Menu
        // ======================================================================

        [MenuItem("Tools/Mac mini M2/Build")]
        public static void Build()
        {
            EnsureFolders();
            CreateMaterials();
            var root = BuildHierarchy();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            BuildPreviewScene(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MacMini] Built {PrefabPath} ({CountTriangles(prefab)} triangles)");
        }

        [MenuItem("Tools/Mac mini M2/Export GLB")]
        public static void ExportDefault() => ExportGlb(DefaultExportPath());

        [MenuItem("Tools/Mac mini M2/Export GLB As...")]
        public static void ExportAs()
        {
            var p = EditorUtility.SaveFilePanel("Export Mac mini GLB", Path.GetDirectoryName(DefaultExportPath()), "mac-mini-m2-2023", "glb");
            if (string.IsNullOrEmpty(p)) return;
            EditorPrefs.SetString(ExportPrefKey, p);
            ExportGlb(p);
        }

        [MenuItem("Tools/Mac mini M2/Capture Review Views")]
        public static void CaptureDefault() => CaptureViews(Path.Combine(ProjectRoot(), "ReviewCaptures"));

        static string ProjectRoot() => Path.GetDirectoryName(Application.dataPath);

        static string DefaultExportPath() =>
            EditorPrefs.GetString(ExportPrefKey, Path.Combine(ProjectRoot(), "mac-mini-m2-2023.glb"));

        /// <summary>Batch entry: -executeMethod MacMiniM2.MacMiniBuilder.BatchBuildAndExport [-glbOut path] [-captureDir dir]</summary>
        public static void BatchBuildAndExport()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string key)
            {
                int i = Array.IndexOf(args, key);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            Build();
            var glb = Arg("-glbOut");
            if (!string.IsNullOrEmpty(glb)) ExportGlb(glb);
            var cap = Arg("-captureDir");
            if (!string.IsNullOrEmpty(cap)) CaptureViews(cap);
        }

        // ======================================================================
        // Export
        // ======================================================================

        public static void ExportGlb(string path)
        {
#if MACMINI_GLTFAST
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Debug.LogError("[MacMini] Prefab missing, run Build first."); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            try
            {
                var settings = new ExportSettings
                {
                    Format = GltfFormat.Binary,
                    FileConflictResolution = FileConflictResolution.Overwrite,
                    ComponentMask = ~(ComponentType.Camera | ComponentType.Animation | ComponentType.Light),
                };
                var goSettings = new GameObjectExportSettings { OnlyActiveInHierarchy = true, DisabledComponents = false };
                var export = new GameObjectExport(settings, goSettings);
                export.AddScene(new[] { go }, RootName); // root sits at the origin with identity transform
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                // forceSync: the Editor does not pump async continuations outside Play Mode.
                var ok = export.SaveToFileAndDispose(path, true).GetAwaiter().GetResult();
                if (ok) Debug.Log($"[MacMini] Exported {path} ({new FileInfo(path).Length / 1024f:0.0} KB)");
                else Debug.LogError("[MacMini] glTF export failed");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
#else
            Debug.LogError("[MacMini] Install com.unity.cloud.gltfast to export GLB.");
#endif
        }

        // ======================================================================
        // Assets
        // ======================================================================

        static void EnsureFolders()
        {
            foreach (var sub in new[] { "Meshes", "Materials" })
                if (!AssetDatabase.IsValidFolder(Folder + "/" + sub))
                    AssetDatabase.CreateFolder(Folder, sub);
        }

        static void CreateMaterials()
        {
            mats = new Dictionary<Mat, Material>();
            Def(Mat.Aluminum,     new Color(0.92f, 0.925f, 0.935f), 1.0f, 0.34f);
            Def(Mat.PanelDark,    new Color(0.085f, 0.085f, 0.09f), 0.4f, 0.42f);
            Def(Mat.PortShell,    new Color(0.42f, 0.42f, 0.44f),   1.0f, 0.38f);
            Def(Mat.PortInterior, new Color(0.012f, 0.012f, 0.014f),0.0f, 0.85f);
            Def(Mat.PortTongue,   new Color(0.03f, 0.03f, 0.035f),  0.0f, 0.55f);
            Def(Mat.ContactMetal, new Color(0.86f, 0.80f, 0.62f),   1.0f, 0.3f);
            Def(Mat.BaseRubber,   new Color(0.025f, 0.025f, 0.028f),0.0f, 0.72f);
            Def(Mat.StatusLED,    new Color(0.9f, 0.9f, 0.9f),      0.0f, 0.3f, Color.white);
        }

        static void Def(Mat id, Color baseColor, float metallic, float roughness, Color? emission = null)
        {
            var path = $"{Folder}/Materials/{id}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            bool isNew = m == null;
            if (isNew) m = new Material(shader);
            else if (m.shader != shader) m.shader = shader;
            m.name = id.ToString();
            m.SetColor("_BaseColor", baseColor);
            m.SetColor("_Color", baseColor);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", 1f - roughness);
            m.SetFloat("_Glossiness", 1f - roughness);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; // URP enables _EMISSION only when AnyEmissive is set
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            // Create the asset only after properties are set: URP's material postprocessor derives
            // keywords (e.g. _EMISSION) from the properties at import time.
            if (isNew) AssetDatabase.CreateAsset(m, path);
            EditorUtility.SetDirty(m);
            mats[id] = m;
        }

        static Mesh SaveMesh(Mesh mesh)
        {
            var path = $"{Folder}/Meshes/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = mesh.name;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static GameObject MeshObject(string name, Transform parent, Vector3 localPos, MeshBuilder mb)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var mesh = SaveMesh(mb.ToMesh(name, out var used));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var list = new Material[used.Count];
            for (int i = 0; i < used.Count; i++) list[i] = mats[used[i]];
            mr.sharedMaterials = list;
            mr.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        // ======================================================================
        // Hierarchy
        // ======================================================================

        static GameObject BuildHierarchy()
        {
            var root = new GameObject(RootName);
            var t = root.transform;

            MeshObject("Enclosure", t, Vector3.zero, BuildEnclosure());
            MeshObject("Base", t, Vector3.zero, BuildBase());
            var panel = MeshObject("RearPanel", t, Vector3.zero, BuildRearPlate());
            foreach (var p in Ports)
                MeshObject(p.name, panel.transform, new Vector3(p.u, p.y, ZR), BuildPort(p));
            MeshObject("StatusLight", t, LedPos, BuildLed());
            return root;
        }

        // ---------- Enclosure ----------

        struct OPt { public Vector2 p, n; }

        static List<OPt> Outline(float inset)
        {
            var list = new List<OPt>();
            float rr = R - inset;
            var centers = new[] { new Vector2(CX, CZ), new Vector2(-CX, CZ), new Vector2(-CX, -CZ), new Vector2(CX, -CZ) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= NC; i++)
                {
                    float a = (c * 90f + 90f * i / NC) * Mathf.Deg2Rad;
                    var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    list.Add(new OPt { p = centers[c] + n * rr, n = n });
                }
            return list;
        }

        static MeshBuilder BuildEnclosure()
        {
            var mb = new MeshBuilder();
            int sm = mb.Sub(Mat.Aluminum);

            // Vertical profile: (inset, y, normal-horizontal, normal-up)
            var prof = new List<Vector4>();
            for (int i = 0; i <= MB; i++)
            {
                float f = Mathf.Lerp(-90f, 0f, (float)i / MB) * Mathf.Deg2Rad;
                prof.Add(new Vector4(RB - RB * Mathf.Cos(f), Y0 + RB + RB * Mathf.Sin(f), Mathf.Cos(f), Mathf.Sin(f)));
            }
            int wallBand = MB; // band between bottom fillet end and top fillet start
            for (int i = 0; i <= MT; i++)
            {
                float f = Mathf.Lerp(0f, 90f, (float)i / MT) * Mathf.Deg2Rad;
                prof.Add(new Vector4(RT - RT * Mathf.Cos(f), H - RT + RT * Mathf.Sin(f), Mathf.Cos(f), Mathf.Sin(f)));
            }

            var rings = new List<int[]>();
            int count = 0;
            foreach (var pr in prof)
            {
                var ol = Outline(pr.x);
                count = ol.Count;
                var idx = new int[count];
                for (int i = 0; i < count; i++)
                {
                    var o = ol[i];
                    var pos = new Vector3(o.p.x, pr.y, o.p.y);
                    var nrm = new Vector3(o.n.x * pr.z, pr.w, o.n.y * pr.z).normalized;
                    idx[i] = mb.V(pos, nrm);
                }
                rings.Add(idx);
            }

            int rearSeg = 2 * (NC + 1) + NC; // last point of rear-left corner -> first of rear-right corner
            for (int b = 0; b < rings.Count - 1; b++)
            {
                var r0 = rings[b]; var r1 = rings[b + 1];
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    if (b == wallBand && i == rearSeg) continue; // opening for the rear I/O panel
                    mb.QuadSmooth(sm, r0[i], r0[j], r1[j], r1[i]);
                }
            }

            // Underside (flat aluminium) and top caps
            Cap(mb, sm, Outline(RB), Y0, Vector3.down);
            Cap(mb, sm, Outline(RT), H, Vector3.up);
            return mb;
        }

        static void Cap(MeshBuilder mb, int sm, List<OPt> ol, float y, Vector3 n)
        {
            int c = mb.V(new Vector3(0, y, 0), n);
            var idx = new int[ol.Count];
            for (int i = 0; i < ol.Count; i++) idx[i] = mb.V(new Vector3(ol[i].p.x, y, ol[i].p.y), n);
            for (int i = 0; i < ol.Count; i++) mb.Tri(sm, c, idx[i], idx[(i + 1) % ol.Count], n);
        }

        // ---------- Base ----------

        static MeshBuilder BuildBase()
        {
            var mb = new MeshBuilder();
            int sm = mb.Sub(Mat.BaseRubber);
            const int seg = 96;
            float y1 = Y0 - 0.0001f;
            float slope = (BaseRTop - BaseRBottom) / y1;
            var bottom = new int[seg]; var top = new int[seg]; var cap = new int[seg];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var n = new Vector3(d.x, -slope, d.z).normalized;
                bottom[i] = mb.V(d * BaseRBottom, n);
                top[i] = mb.V(d * BaseRTop + Vector3.up * y1, n);
                cap[i] = mb.V(d * BaseRBottom, Vector3.down);
            }
            int c = mb.V(Vector3.zero, Vector3.down);
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                mb.QuadSmooth(sm, bottom[i], bottom[j], top[j], top[i]);
                mb.Tri(sm, c, cap[i], cap[j], Vector3.down);
            }
            return mb;
        }

        // ---------- Rear panel plate with port openings ----------

        // Hole outline in port-local 2D (u,y), grouped by corner: TR, TL, BL, BR.
        static List<Vector2>[] HoleGroups(Port p)
        {
            float a = p.w * 0.5f - p.r, b = p.h * 0.5f - p.r;
            var centers = new[] { new Vector2(a, b), new Vector2(-a, b), new Vector2(-a, -b), new Vector2(a, -b) };
            var groups = new List<Vector2>[4];
            for (int c = 0; c < 4; c++)
            {
                groups[c] = new List<Vector2>();
                for (int i = 0; i <= p.k; i++)
                {
                    float ang = (c * 90f + 90f * i / p.k) * Mathf.Deg2Rad;
                    var v = centers[c] + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * p.r;
                    if (p.chamfer > 0f && v.y < 0f) v.x *= 1f - p.chamfer * (-v.y / (p.h * 0.5f)); // HDMI trapezoid
                    groups[c].Add(v);
                }
            }
            return groups;
        }

        static Rect Cell(Port p) =>
            Rect.MinMaxRect(p.u - p.w * 0.5f - CellMargin, p.y - p.h * 0.5f - CellMargin,
                            p.u + p.w * 0.5f + CellMargin, p.y + p.h * 0.5f + CellMargin);

        static MeshBuilder BuildRearPlate()
        {
            var mb = new MeshBuilder();
            int sm = mb.Sub(Mat.PanelDark);
            var n = Vector3.back;
            Vector3 P(float u, float y) => new Vector3(u, y, ZR);

            float ox0 = -CX, ox1 = CX, oy0 = PanelY0, oy1 = PanelY1;           // outer = shell opening
            float ix0 = ox0 + PanelBorder, ix1 = ox1 - PanelBorder, iy0 = oy0 + PanelBorder, iy1 = oy1 - PanelBorder;

            var cells = new List<Rect>();
            foreach (var p in Ports) cells.Add(Cell(p));

            var xs = new SortedSet<float> { ix0, ix1 };
            var ys = new SortedSet<float> { iy0, iy1 };
            foreach (var c in cells) { xs.Add(c.xMin); xs.Add(c.xMax); ys.Add(c.yMin); ys.Add(c.yMax); }
            var X = new List<float>(xs); var Y = new List<float>(ys);

            // Grid quads outside port cells
            for (int i = 0; i < X.Count - 1; i++)
                for (int j = 0; j < Y.Count - 1; j++)
                {
                    var mid = new Vector2((X[i] + X[i + 1]) * 0.5f, (Y[j] + Y[j + 1]) * 0.5f);
                    bool inside = false;
                    foreach (var c in cells) if (c.Contains(mid)) { inside = true; break; }
                    if (inside) continue;
                    mb.Quad(sm, P(X[i], Y[j]), P(X[i + 1], Y[j]), P(X[i + 1], Y[j + 1]), P(X[i], Y[j + 1]), n);
                }

            // Border frame: trapezoids from the 4 outer corners to every inner-edge grid vertex (no T-junctions with the shell).
            Fan(mb, sm, P(ox0, oy0), P(ox1, oy0), X, x => P(x, iy0), n);              // bottom
            Fan(mb, sm, P(ox1, oy1), P(ox0, oy1), Rev(X), x => P(x, iy1), n);         // top
            Fan(mb, sm, P(ox0, oy1), P(ox0, oy0), Rev(Y), y => P(ix0, y), n);         // left
            Fan(mb, sm, P(ox1, oy0), P(ox1, oy1), Y, y => P(ix1, y), n);              // right

            // Port cells: ring between cell rectangle and hole outline
            foreach (var p in Ports)
            {
                var c = Cell(p);
                var g = HoleGroups(p);
                var corners = new[] { new Vector2(c.xMax, c.yMax), new Vector2(c.xMin, c.yMax), new Vector2(c.xMin, c.yMin), new Vector2(c.xMax, c.yMin) };
                Vector3 Hp(Vector2 v) => P(p.u + v.x, p.y + v.y);
                for (int k = 0; k < 4; k++)
                {
                    var C = P(corners[k].x, corners[k].y);
                    for (int i = 0; i < g[k].Count - 1; i++) mb.Tri(sm, Hp(g[k][i]), Hp(g[k][i + 1]), C, n);

                    // side from corner k to corner k+1 with grid vertices inserted
                    int k1 = (k + 1) % 4;
                    var outer = new List<Vector3> { C };
                    var a = corners[k]; var b = corners[k1];
                    if (Mathf.Approximately(a.y, b.y))
                    {
                        var between = X.FindAll(x => x > Mathf.Min(a.x, b.x) + 1e-6f && x < Mathf.Max(a.x, b.x) - 1e-6f);
                        if (b.x < a.x) between.Reverse();
                        foreach (var x in between) outer.Add(P(x, a.y));
                    }
                    else
                    {
                        var between = Y.FindAll(y => y > Mathf.Min(a.y, b.y) + 1e-6f && y < Mathf.Max(a.y, b.y) - 1e-6f);
                        if (b.y < a.y) between.Reverse();
                        foreach (var y in between) outer.Add(P(a.x, y));
                    }
                    outer.Add(P(b.x, b.y));
                    var ia = Hp(g[k][g[k].Count - 1]);
                    var ib = Hp(g[k1][0]);
                    for (int i = 0; i < outer.Count - 1; i++) mb.Tri(sm, ia, outer[i], outer[i + 1], n);
                    mb.Tri(sm, ia, outer[outer.Count - 1], ib, n);
                }
            }
            return mb;
        }

        static List<float> Rev(List<float> l) { var r = new List<float>(l); r.Reverse(); return r; }

        static void Fan(MeshBuilder mb, int sm, Vector3 A, Vector3 B, List<float> inner, Func<float, Vector3> f, Vector3 n)
        {
            mb.Tri(sm, A, B, f(inner[inner.Count - 1]), n);
            for (int i = inner.Count - 1; i > 0; i--) mb.Tri(sm, A, f(inner[i]), f(inner[i - 1]), n);
        }

        // ---------- Individual ports (port-local; +z goes into the machine) ----------

        static MeshBuilder BuildPort(Port p)
        {
            var mb = new MeshBuilder();
            int wall = mb.Sub(p.wall);
            int back = mb.Sub(Mat.PortInterior);
            var pts = new List<Vector2>();
            foreach (var g in HoleGroups(p)) pts.AddRange(g);

            Vector2 center = Vector2.zero;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                if ((b - a).sqrMagnitude < 1e-14f) continue;
                var mid = (a + b) * 0.5f;
                var inward = (center - mid); inward = new Vector2(-(b - a).y, (b - a).x).normalized * Mathf.Sign(Vector2.Dot(new Vector2(-(b - a).y, (b - a).x), inward));
                var nn = new Vector3(inward.x, inward.y, 0);
                mb.Quad(wall, new Vector3(a.x, a.y, 0), new Vector3(b.x, b.y, 0), new Vector3(b.x, b.y, p.depth), new Vector3(a.x, a.y, p.depth), nn);
            }
            // back cap
            var bc = mb.V(new Vector3(0, 0, p.depth), Vector3.back);
            var ring = new int[pts.Count];
            for (int i = 0; i < pts.Count; i++) ring[i] = mb.V(new Vector3(pts[i].x, pts[i].y, p.depth), Vector3.back);
            for (int i = 0; i < pts.Count; i++) mb.Tri(back, bc, ring[i], ring[(i + 1) % pts.Count], Vector3.back);

            switch (p.kind)
            {
                case Kind.Thunderbolt:
                    mb.Box(mb.Sub(Mat.PortTongue), new Vector3(0, 0, 0.0015f + (p.depth - 0.0015f) * 0.5f), new Vector3(p.w * 0.72f, 0.0007f, p.depth - 0.0015f));
                    break;
                case Kind.HDMI:
                    mb.Box(mb.Sub(Mat.PortTongue), new Vector3(0, 0.0002f, 0.0015f + (p.depth - 0.0015f) * 0.5f), new Vector3(p.w * 0.70f, 0.0011f, p.depth - 0.0015f));
                    break;
                case Kind.USBA:
                    mb.Box(mb.Sub(Mat.PortTongue), new Vector3(0, p.h * 0.2f, 0.002f + (p.depth - 0.002f) * 0.5f), new Vector3(p.w * 0.86f, 0.0018f, p.depth - 0.002f));
                    break;
                case Kind.Ethernet:
                {
                    int cm = mb.Sub(Mat.ContactMetal);
                    for (int i = 0; i < 8; i++)
                        mb.Box(cm, new Vector3((i - 3.5f) * 0.00102f, p.h * 0.5f - 0.0014f, 0.0065f), new Vector3(0.00045f, 0.0003f, 0.0045f));
                    int tg = mb.Sub(Mat.PortTongue);
                    float bw = p.w * 0.5f - 0.003f;
                    for (int s = -1; s <= 1; s += 2)
                        mb.Box(tg, new Vector3(s * (p.w * 0.5f - bw * 0.5f), -p.h * 0.5f + 0.0011f, 0.002f + (p.depth - 0.002f) * 0.5f), new Vector3(bw, 0.0022f, p.depth - 0.002f));
                    break;
                }
                case Kind.ACInlet:
                {
                    int cm = mb.Sub(Mat.ContactMetal);
                    for (int s = -1; s <= 1; s += 2)
                        mb.CylinderZ(cm, new Vector2(s * 0.0044f, 0), 0.0011f, 0.0025f, p.depth, 12);
                    break;
                }
                case Kind.Button:
                {
                    int face = mb.Sub(Mat.PanelDark);
                    mb.CylinderZ(face, Vector2.zero, p.w * 0.5f - 0.0003f, 0.0003f, p.depth, 32, mb.Sub(Mat.PortShell)); // metal rim catches light
                    int icon = mb.Sub(Mat.PortShell);
                    float z = 0.0003f - 0.00003f;
                    // power symbol: open ring + bar
                    const int arcSeg = 20;
                    float r0 = 0.00125f, r1 = 0.0016f;
                    for (int i = 0; i < arcSeg; i++)
                    {
                        float a0 = Mathf.Lerp(130f, 410f, (float)i / arcSeg) * Mathf.Deg2Rad;
                        float a1 = Mathf.Lerp(130f, 410f, (float)(i + 1) / arcSeg) * Mathf.Deg2Rad;
                        Vector3 Pa(float a, float r) => new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
                        mb.Quad(icon, Pa(a0, r0), Pa(a1, r0), Pa(a1, r1), Pa(a0, r1), Vector3.back);
                    }
                    mb.Quad(icon, new Vector3(-0.00017f, 0.0001f, z), new Vector3(0.00017f, 0.0001f, z), new Vector3(0.00017f, 0.0019f, z), new Vector3(-0.00017f, 0.0019f, z), Vector3.back);
                    break;
                }
                case Kind.Vent:
                {
                    int fin = mb.Sub(Mat.PortShell);
                    float a = p.w * 0.5f - p.r;
                    for (float x = -p.w * 0.5f + 0.0016f; x <= p.w * 0.5f - 0.0016f; x += 0.0013f)
                    {
                        float hh = p.h * 0.5f - 0.0001f;
                        float ex = Mathf.Abs(x) - a;
                        if (ex > 0f) hh = Mathf.Sqrt(Mathf.Max(0f, p.r * p.r - ex * ex)) - 0.0001f;
                        if (hh < 0.0006f) continue;
                        mb.Box(fin, new Vector3(x, 0, 0.0012f + (p.depth - 0.0012f) * 0.5f), new Vector3(0.00045f, hh * 2f, p.depth - 0.0012f));
                    }
                    break;
                }
            }
            return mb;
        }

        static MeshBuilder BuildLed()
        {
            var mb = new MeshBuilder();
            int sm = mb.Sub(Mat.StatusLED);
            const int seg = 16; const float r = 0.00065f; const float z = 0.00004f;
            int c = mb.V(new Vector3(0, 0, z), Vector3.forward);
            var ring = new int[seg];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                ring[i] = mb.V(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z), Vector3.forward);
            }
            for (int i = 0; i < seg; i++) mb.Tri(sm, c, ring[i], ring[(i + 1) % seg], Vector3.forward);
            return mb;
        }

        // ======================================================================
        // Preview scene (staging lives outside the export root)
        // ======================================================================

        static void BuildPreviewScene(GameObject prefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var staging = new GameObject("Staging").transform;

            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 1.0f; key.shadows = LightShadows.Soft;
            key.transform.SetParent(staging); key.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            var fill = new GameObject("FillLight").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.6f; fill.shadows = LightShadows.None;
            fill.transform.SetParent(staging); fill.transform.rotation = Quaternion.Euler(25f, 150f, 0f);

            var under = new GameObject("UnderLight").AddComponent<Light>();
            under.type = LightType.Directional; under.intensity = 0.35f; under.shadows = LightShadows.None;
            under.transform.SetParent(staging); under.transform.rotation = Quaternion.Euler(-60f, 20f, 0f);

            var cam = new GameObject("PreviewCamera").AddComponent<Camera>();
            cam.transform.SetParent(staging);
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 10f; cam.fieldOfView = 25f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.transform.position = new Vector3(-0.32f, 0.2f, 0.42f);
            cam.transform.LookAt(new Vector3(0, H * 0.5f, 0));
            cam.gameObject.tag = "MainCamera";

            // Skybox-driven ambient + reflections so the metallic aluminium has something to reflect.
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();

            PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // ======================================================================
        // Review captures
        // ======================================================================

        public static void CaptureViews(string dir)
        {
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            DynamicGI.UpdateEnvironment();
            var target = new Vector3(0, H * 0.5f, 0);
            var views = new (string name, Vector3 pos, Vector3 up, float fov, Vector3 look)[]
            {
                ("front",       new Vector3(0f, 0.02f, 0.75f),       Vector3.up,      18f, target),
                ("rear",        new Vector3(0f, 0.02f, -0.75f),      Vector3.up,      18f, target),
                ("top",         new Vector3(0f, 0.75f, 0f),          Vector3.forward, 18f, target),
                ("underside",   new Vector3(0f, -0.75f, 0f),         Vector3.forward, 18f, target),
                ("front_3q",    new Vector3(-0.33f, 0.21f, 0.45f),   Vector3.up,      24f, target),
                ("rear_3q",     new Vector3(-0.30f, 0.12f, -0.42f),  Vector3.up,      24f, target),
                ("rear_ports",  new Vector3(-0.05f, 0.06f, -0.30f),  Vector3.up,      22f, new Vector3(-0.005f, 0.02f, ZR)),
                ("side",        new Vector3(0.75f, 0.02f, 0f),       Vector3.up,      18f, target),
            };

            var go = new GameObject("CaptureCamera");
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.82f, 0.83f, 0.85f);
            const int w = 1400, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                foreach (var v in views)
                {
                    cam.fieldOfView = v.fov;
                    cam.transform.position = v.pos;
                    cam.transform.LookAt(v.look, v.up);
                    var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                    if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
                    else cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, v.name + ".png"), tex.EncodeToPNG());
                }
                Debug.Log($"[MacMini] Wrote {views.Length} review captures to {dir}");
            }
            finally
            {
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        static int CountTriangles(GameObject root)
        {
            int n = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh) for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) n += (int)mf.sharedMesh.GetIndexCount(s) / 3;
            return n;
        }

        // ======================================================================
        // Mesh builder
        // ======================================================================

        class MeshBuilder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<Mat> subMats = new List<Mat>();
            readonly List<List<int>> subs = new List<List<int>>();

            public int Sub(Mat m)
            {
                int i = subMats.IndexOf(m);
                if (i >= 0) return i;
                subMats.Add(m); subs.Add(new List<int>());
                return subMats.Count - 1;
            }

            public int V(Vector3 p, Vector3 nrm)
            {
                v.Add(p); n.Add(nrm);
                var a = new Vector3(Mathf.Abs(nrm.x), Mathf.Abs(nrm.y), Mathf.Abs(nrm.z));
                uv.Add(a.y >= a.x && a.y >= a.z ? new Vector2(p.x, p.z) : a.x >= a.z ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
                return v.Count - 1;
            }

            // Unity front face: Cross(b-a, c-a) points toward the viewer.
            public void Tri(int sm, int a, int b, int c, Vector3 hint)
            {
                var fn = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (fn.sqrMagnitude < 1e-26f) return;
                if (Vector3.Dot(fn, hint) < 0f) { var t = b; b = c; c = t; }
                subs[sm].Add(a); subs[sm].Add(b); subs[sm].Add(c);
            }

            public void Tri(int sm, Vector3 a, Vector3 b, Vector3 c, Vector3 nrm) =>
                Tri(sm, V(a, nrm), V(b, nrm), V(c, nrm), nrm);

            public void Quad(int sm, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm)
            {
                int ia = V(a, nrm), ib = V(b, nrm), ic = V(c, nrm), id = V(d, nrm);
                Tri(sm, ia, ib, ic, nrm); Tri(sm, ia, ic, id, nrm);
            }

            public void QuadSmooth(int sm, int a, int b, int c, int d)
            {
                var hint = n[a] + n[b] + n[c] + n[d];
                Tri(sm, a, b, c, hint); Tri(sm, a, c, d, hint);
            }

            public void Box(int sm, Vector3 c, Vector3 s)
            {
                var h = s * 0.5f;
                Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
                Quad(sm, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), Vector3.back);
                Quad(sm, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Vector3.forward);
                Quad(sm, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), Vector3.left);
                Quad(sm, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.right);
                Quad(sm, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Vector3.down);
                Quad(sm, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up);
            }

            // Cylinder along +Z from z0 (capped, facing -Z) to z1 (open).
            public void CylinderZ(int sm, Vector2 c, float r, float z0, float z1, int seg, int sideSm = -1)
            {
                var ring0 = new int[seg]; var ring1 = new int[seg]; var cap = new int[seg];
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                    var p = new Vector3(c.x, c.y, 0) + d * r;
                    ring0[i] = V(new Vector3(p.x, p.y, z0), d);
                    ring1[i] = V(new Vector3(p.x, p.y, z1), d);
                    cap[i] = V(new Vector3(p.x, p.y, z0), Vector3.back);
                }
                int cc = V(new Vector3(c.x, c.y, z0), Vector3.back);
                for (int i = 0; i < seg; i++)
                {
                    int j = (i + 1) % seg;
                    QuadSmooth(sideSm >= 0 ? sideSm : sm, ring0[i], ring0[j], ring1[j], ring1[i]);
                    Tri(sm, cc, cap[i], cap[j], Vector3.back);
                }
            }

            public Mesh ToMesh(string name, out List<Mat> used)
            {
                // drop empty submeshes
                used = new List<Mat>();
                var tris = new List<List<int>>();
                for (int i = 0; i < subs.Count; i++)
                    if (subs[i].Count > 0) { used.Add(subMats[i]); tris.Add(subs[i]); }

                var m = new Mesh { name = name };
                m.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                // Snap positions to a 2^-20 m grid (< 1 µm). Unity derives bounds as centre±extents, and on
                // snapped values that arithmetic is exact in float32, so exported glTF accessor min/max match
                // the data exactly (otherwise the Khronos validator flags ACCESSOR_MIN_MISMATCH).
                const float q = 1048576f;
                var snapped = new List<Vector3>(v.Count);
                foreach (var p in v) snapped.Add(new Vector3(Mathf.Round(p.x * q) / q, Mathf.Round(p.y * q) / q, Mathf.Round(p.z * q) / q));
                m.SetVertices(snapped); m.SetNormals(n); m.SetUVs(0, uv);
                m.subMeshCount = tris.Count;
                for (int i = 0; i < tris.Count; i++) m.SetTriangles(tris[i], i, false);
                m.RecalculateBounds(); // no tangents: materials have no normal maps
                return m;
            }
        }
    }
}
