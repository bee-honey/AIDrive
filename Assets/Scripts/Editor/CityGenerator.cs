using AIDrive.City;
using AIDrive.Navigation;
using AIDrive.Traffic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIDrive.Editor
{
    /// <summary>
    /// Procedurally builds the city (roads, markings, blocks, buildings, parks) plus the map layer
    /// (street signs, road labels, landmarks). Deterministic: fixed seed, layout from <see cref="CityLayout"/>.
    /// </summary>
    public static class CityGenerator
    {
        const int Seed = 1234;
        const string MaterialDir = "Assets/City/Materials";

        static Font font;
        static Material textMat;

        [MenuItem("AIDrive/Generate City")]
        public static void Generate()
        {
            var old = GameObject.Find("City");
            if (old != null) Undo.DestroyObjectImmediate(old);

            Random.InitState(Seed);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textMat = TextMaterial();

            var root = new GameObject("City").transform;
            var blockTop = BuildCity(root);
            BuildMap(root, blockTop);

            root.gameObject.AddComponent<CityMap>();
            root.gameObject.AddComponent<RouteDebugger>();

            FrameMainCamera();
            OrientLabels(root);

            Undo.RegisterCreatedObjectUndo(root.gameObject, "Generate City");
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root.gameObject;
        }

        // ---------------------------------------------------------------- city

        /// <summary>Builds roads and blocks. Returns the tallest building top per block.</summary>
        static float[,] BuildCity(Transform root)
        {
            float bs = CityLayout.BlockSize, rw = CityLayout.RoadWidth, sh = CityLayout.SidewalkHeight;
            float pitch = CityLayout.Pitch, extent = CityLayout.Extent, start = CityLayout.Origin;
            int blocks = CityLayout.Blocks, roadCount = CityLayout.RoadCount;

            var asphalt  = Mat("Asphalt",  new Color(0.18f, 0.18f, 0.19f), 0.1f);
            var line     = Mat("LaneWhite", new Color(0.95f, 0.95f, 0.9f), 0.3f);
            var yellow   = Mat("LaneYellow", new Color(0.95f, 0.78f, 0.1f), 0.3f);
            var sidewalk = Mat("Sidewalk", new Color(0.62f, 0.62f, 0.6f), 0.1f);
            var grass    = Mat("Grass",    new Color(0.28f, 0.5f, 0.22f), 0.05f);
            var trunk    = Mat("Trunk",    new Color(0.35f, 0.24f, 0.15f), 0.05f);
            var leaves   = Mat("Leaves",   new Color(0.18f, 0.42f, 0.16f), 0.05f);
            var ground   = Mat("Ground",   new Color(0.3f, 0.36f, 0.25f), 0.05f);
            Material[] bmats =
            {
                Mat("Bldg_Concrete", new Color(0.7f, 0.68f, 0.64f), 0.2f),
                Mat("Bldg_Brick",    new Color(0.55f, 0.3f, 0.24f), 0.15f),
                Mat("Bldg_Glass",    new Color(0.35f, 0.5f, 0.62f), 0.85f),
                Mat("Bldg_Sand",     new Color(0.82f, 0.74f, 0.58f), 0.2f),
                Mat("Bldg_Dark",     new Color(0.25f, 0.27f, 0.3f), 0.6f),
            };
            var roofMat = Mat("Roof", new Color(0.3f, 0.3f, 0.3f), 0.1f);

            Box("Ground", root, new Vector3(0, -0.06f, 0), new Vector3(extent + 200, 0.1f, extent + 200), ground);

            var roads = Group("Roads", root);
            for (int i = 0; i < roadCount; i++)
            {
                float c = CityLayout.RoadCenter(i);
                Box("Road_" + CityLayout.NorthSouthRoad(i), roads, new Vector3(c, 0f, 0), new Vector3(rw, 0.02f, extent), asphalt);
                Box("Road_" + CityLayout.EastWestRoad(i), roads, new Vector3(0, 0.001f, c), new Vector3(extent, 0.02f, rw), asphalt);
            }

            // Lane markings, only between intersections
            var marks = Group("LaneMarkings", root);
            float dash = 3f, gap = 3f;
            for (int i = 0; i < roadCount; i++)
            {
                float c = CityLayout.RoadCenter(i);
                for (int s = 0; s < blocks; s++)
                {
                    float segStart = start + rw + s * pitch + 1f;
                    float segEnd = segStart + bs - 2f;
                    float mid = (segStart + segEnd) / 2f;
                    float len = segEnd - segStart;
                    Box("Yellow", marks, new Vector3(c - 0.15f, 0.015f, mid), new Vector3(0.12f, 0.01f, len), yellow, false);
                    Box("Yellow", marks, new Vector3(c + 0.15f, 0.015f, mid), new Vector3(0.12f, 0.01f, len), yellow, false);
                    Box("Yellow", marks, new Vector3(mid, 0.015f, c - 0.15f), new Vector3(len, 0.01f, 0.12f), yellow, false);
                    Box("Yellow", marks, new Vector3(mid, 0.015f, c + 0.15f), new Vector3(len, 0.01f, 0.12f), yellow, false);
                    for (float d = segStart; d + dash <= segEnd; d += dash + gap)
                    {
                        float dm = d + dash / 2f;
                        foreach (float off in new[] { -3f, 3f })
                        {
                            Box("Dash", marks, new Vector3(c + off, 0.015f, dm), new Vector3(0.15f, 0.01f, dash), line, false);
                            Box("Dash", marks, new Vector3(dm, 0.015f, c + off), new Vector3(dash, 0.01f, 0.15f), line, false);
                        }
                    }
                    foreach (float edge in new[] { segStart - 0.5f, segEnd + 0.5f })
                    {
                        for (int k = 0; k < 8; k++)
                        {
                            float o = -rw / 2f + 0.9f + k * 1.5f;
                            Box("Crosswalk", marks, new Vector3(c + o, 0.015f, edge), new Vector3(0.7f, 0.01f, 1.2f), line, false);
                            Box("Crosswalk", marks, new Vector3(edge, 0.015f, c + o), new Vector3(1.2f, 0.01f, 0.7f), line, false);
                        }
                    }
                }
            }

            var blockTop = new float[blocks, blocks];
            var blocksGroup = Group("Blocks", root);
            float maxDist = extent / 2f;
            for (int bx = 0; bx < blocks; bx++)
            for (int bz = 0; bz < blocks; bz++)
            {
                var center = CityLayout.BlockCenter(bx, bz);
                var block = Group("Block_" + bx + "_" + bz, blocksGroup);
                block.localPosition = center;

                Box("Sidewalk", block, new Vector3(0, sh / 2f, 0), new Vector3(bs, sh, bs), sidewalk);

                bool park = (bx == 1 && bz == 4) || (bx == 4 && bz == 1);
                if (park)
                {
                    block.name += "_Park";
                    Box("Lawn", block, new Vector3(0, sh + 0.02f, 0), new Vector3(bs - 5f, 0.05f, bs - 5f), grass);
                    for (int t = 0; t < 14; t++)
                    {
                        var tr = Group("Tree", block);
                        tr.localPosition = new Vector3(Random.Range(-15f, 15f), sh, Random.Range(-15f, 15f));
                        float s = Random.Range(0.8f, 1.3f);
                        Primitive(PrimitiveType.Cylinder, "Trunk", tr, new Vector3(0, 1.5f * s, 0), new Vector3(0.4f * s, 1.5f * s, 0.4f * s), trunk, true);
                        Primitive(PrimitiveType.Sphere, "Canopy", tr, new Vector3(0, 4f * s, 0), Vector3.one * 3.2f * s, leaves, false);
                    }
                    blockTop[bx, bz] = 6f;
                    continue;
                }

                // Taller downtown, lower toward the edges
                float dist = new Vector2(center.x, center.z).magnitude / maxDist;
                float heightScale = Mathf.Lerp(1f, 0.15f, Mathf.Clamp01(dist));

                int lots = Random.value < 0.35f ? 2 : 3;
                float inner = bs - 4f; // 2 m sidewalk ring
                float lot = inner / lots;
                for (int lx = 0; lx < lots; lx++)
                for (int lz = 0; lz < lots; lz++)
                {
                    if (lots == 3 && lx == 1 && lz == 1) continue; // courtyard
                    float w = lot - Random.Range(1f, 3f);
                    float d = lot - Random.Range(1f, 3f);
                    float h = Mathf.Max(6f, Random.Range(8f, 70f) * heightScale + Random.Range(0f, 8f));
                    float px = -inner / 2f + lot * (lx + 0.5f);
                    float pz = -inner / 2f + lot * (lz + 0.5f);
                    var mat = bmats[Random.Range(0, bmats.Length)];
                    Box("Building", block, new Vector3(px, sh + h / 2f, pz), new Vector3(w, h, d), mat);
                    Box("Roof", block, new Vector3(px, sh + h + 0.15f, pz), new Vector3(w * 0.9f, 0.3f, d * 0.9f), roofMat, false);
                    blockTop[bx, bz] = Mathf.Max(blockTop[bx, bz], sh + h);
                }
            }
            return blockTop;
        }

        // ---------------------------------------------------------------- map layer

        static void BuildMap(Transform root, float[,] blockTop)
        {
            var map = Group("Map", root);
            BuildStreetSigns(Group("StreetSigns", map));
            BuildRoadLabels(Group("RoadLabels", map));
            BuildLandmarks(Group("Landmarks", map), blockTop);
            BuildStreetLamps(Group("StreetLamps", map));
            BuildTrafficSignals(Group("TrafficSignals", map));
        }

        /// <summary>
        /// Signals where main roads cross (see <see cref="CityLayout.IsSignalized"/>). Each approach gets a
        /// far-side head on the far-right corner, facing the drivers, plus a white stop line before the crosswalk.
        /// </summary>
        static void BuildTrafficSignals(Transform parent)
        {
            var housing = Mat("SignalHousing", new Color(0.12f, 0.13f, 0.14f), 0.4f);
            var pole = Mat("SignPole", new Color(0.45f, 0.45f, 0.47f), 0.4f);
            var stopLine = Mat("LaneWhite", new Color(0.95f, 0.95f, 0.9f), 0.3f);
            Material Lamp(string name, Color c, bool on)
            {
                var m = Mat(name, on ? c : c * 0.18f, 0.8f);
                if (on)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", c * 3f);
                }
                else
                {
                    m.DisableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", Color.black);
                }
                return m;
            }
            Color red = new Color(1f, 0.1f, 0.05f), yellow = new Color(1f, 0.75f, 0.05f), green = new Color(0.1f, 1f, 0.35f);

            float rw = CityLayout.RoadWidth;
            var approaches = new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            for (int i = 0; i < CityLayout.RoadCount; i++)
            for (int j = 0; j < CityLayout.RoadCount; j++)
            {
                if (!CityLayout.IsSignalized(i, j)) continue;
                var centre = CityLayout.Intersection(i, j);
                var root = new GameObject("Signal_" + CityLayout.NorthSouthRoad(i) + "_" + CityLayout.EastWestRoad(j));
                root.transform.SetParent(parent, false);
                root.transform.localPosition = centre;
                var signal = root.AddComponent<TrafficSignal>();
                signal.gridX = i;
                signal.gridZ = j;
                signal.offset = SignalTiming.OffsetFor(i, j);
                signal.redOn = Lamp("SignalRedOn", red, true);
                signal.redOff = Lamp("SignalRedOff", red, false);
                signal.yellowOn = Lamp("SignalYellowOn", yellow, true);
                signal.yellowOff = Lamp("SignalYellowOff", yellow, false);
                signal.greenOn = Lamp("SignalGreenOn", green, true);
                signal.greenOff = Lamp("SignalGreenOff", green, false);

                var heads = new System.Collections.Generic.List<SignalHead>();
                foreach (var d in approaches)
                {
                    var right = LanePath.RightOf(d);
                    // Far-right corner as seen by traffic travelling in d, diagonal from the street-sign pole.
                    var cornerPos = (d + right) * (rw / 2f + 1.6f) + Vector3.up * CityLayout.SidewalkHeight;
                    var post = Group("Pole", root.transform);
                    post.localPosition = cornerPos;
                    Primitive(PrimitiveType.Cylinder, "Post", post, new Vector3(0, 1.9f, 0), new Vector3(0.14f, 1.9f, 0.14f), pole, true);

                    var head = Group("Head", post);
                    head.localPosition = new Vector3(0, 3.35f, 0);
                    head.localRotation = Quaternion.LookRotation(-d); // lamps face the approaching drivers
                    Box("Housing", head, Vector3.zero, new Vector3(0.36f, 1.0f, 0.28f), housing, false);
                    Renderer L(string name, float y)
                    {
                        var lamp = Primitive(PrimitiveType.Sphere, name, head, new Vector3(0, y, 0.12f), Vector3.one * 0.22f, housing, false);
                        lamp.isStatic = false; // material is swapped at runtime; keep it out of static batches
                        return lamp.GetComponent<Renderer>();
                    }
                    heads.Add(new SignalHead
                    {
                        axis = SignalTiming.AxisOf(d),
                        red = L("Red", 0.3f),
                        yellow = L("Yellow", 0f),
                        green = L("Green", -0.3f),
                    });

                    // Stop line across the approach's two lanes, just before the crosswalk.
                    var line = Box("StopLine", root.transform, -d * (LaneSettings.Default.StopLine - 0.25f) + right * 3.1f + Vector3.up * 0.016f,
                                   new Vector3(5.8f, 0.01f, 0.4f), stopLine, false);
                    line.transform.localRotation = Quaternion.LookRotation(d);
                }
                signal.heads = heads.ToArray();
            }
        }

        /// <summary>
        /// One lamp per sidewalk side per block, at 30 % / 70 % along the block so they never clash with
        /// corner street signs or the mid-block landmark signs. Sides facing outside the city are skipped.
        /// </summary>
        static void BuildStreetLamps(Transform parent)
        {
            var prefab = PropBuilder.EnsureBuilt().streetLamp;
            float rw = CityLayout.RoadWidth, sidewalk = rw / 2f + 0.7f;
            int n = CityLayout.RoadCount;

            void Place(Vector3 roadPoint, Vector3 toSidewalk)
            {
                var lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                lamp.transform.position = roadPoint + toSidewalk * sidewalk + Vector3.up * CityLayout.SidewalkHeight;
                lamp.transform.rotation = Quaternion.LookRotation(-toSidewalk); // arm reaches over the road
            }

            for (int road = 0; road < n; road++)
            for (int seg = 0; seg < n - 1; seg++)
            {
                float a = CityLayout.RoadCenter(seg) + rw / 2f + 1f;
                float len = CityLayout.BlockSize - 2f;
                float c = CityLayout.RoadCenter(road);
                // E road: sidewalks to the north (+z) and south (-z)
                if (road < n - 1) Place(new Vector3(a + 0.3f * len, 0, c), Vector3.forward);
                if (road > 0)     Place(new Vector3(a + 0.7f * len, 0, c), Vector3.back);
                // S road: sidewalks to the east (+x) and west (-x)
                if (road < n - 1) Place(new Vector3(c, 0, a + 0.7f * len), Vector3.right);
                if (road > 0)     Place(new Vector3(c, 0, a + 0.3f * len), Vector3.left);
            }
        }

        /// <summary>One pole per intersection on the corner facing the city centre, with a plate per street.</summary>
        static void BuildStreetSigns(Transform parent)
        {
            var pole = Mat("SignPole", new Color(0.45f, 0.45f, 0.47f), 0.4f);
            var plate = Mat("StreetSignGreen", new Color(0.05f, 0.4f, 0.2f), 0.3f);
            int n = CityLayout.RoadCount;
            float corner = CityLayout.RoadWidth / 2f + 0.8f;

            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float dx = i < n - 1 ? 1f : -1f;
                float dz = j < n - 1 ? 1f : -1f;
                var sign = Group("Sign_" + CityLayout.NorthSouthRoad(i) + "_" + CityLayout.EastWestRoad(j), parent);
                sign.localPosition = CityLayout.Intersection(i, j) + new Vector3(dx * corner, CityLayout.SidewalkHeight, dz * corner);

                Primitive(PrimitiveType.Cylinder, "Pole", sign, new Vector3(0, 1.7f, 0), new Vector3(0.12f, 1.7f, 0.12f), pole, true);

                // Plate naming the N–S street runs along Z; plate naming the E–W street runs along X.
                Box("Plate_" + CityLayout.NorthSouthRoad(i), sign, new Vector3(0, 3.25f, 0), new Vector3(0.06f, 0.34f, 1.3f), plate, false);
                PlateText(CityLayout.NorthSouthRoad(i), sign, new Vector3(0, 3.25f, 0), Vector3.right);
                Box("Plate_" + CityLayout.EastWestRoad(j), sign, new Vector3(0, 2.85f, 0), new Vector3(1.3f, 0.34f, 0.06f), plate, false);
                PlateText(CityLayout.EastWestRoad(j), sign, new Vector3(0, 2.85f, 0), Vector3.forward);
            }
        }

        /// <summary>Text on both faces of a plate whose face normal is ±<paramref name="normal"/>.</summary>
        static void PlateText(string text, Transform parent, Vector3 pos, Vector3 normal)
        {
            foreach (var f in new[] { normal, -normal })
                Text(text, parent, pos - f * 0.04f, Quaternion.LookRotation(f), 0.045f, Color.white);
        }

        /// <summary>Large names painted on the ground at both ends of every road, readable from above.</summary>
        static void BuildRoadLabels(Transform parent)
        {
            var flat = Quaternion.Euler(90f, 0f, 0f);
            float end = CityLayout.Extent / 2f + 9f;
            for (int i = 0; i < CityLayout.RoadCount; i++)
            {
                float c = CityLayout.RoadCenter(i);
                string ns = CityLayout.NorthSouthRoad(i), ew = CityLayout.EastWestRoad(i);
                Text(ns, parent, new Vector3(c, 0.05f, -end), flat, 0.9f, Color.white);
                Text(ns, parent, new Vector3(c, 0.05f, end), flat, 0.9f, Color.white);
                Text(ew, parent, new Vector3(-end, 0.05f, c), flat, 0.9f, Color.white);
                Text(ew, parent, new Vector3(end, 0.05f, c), flat, 0.9f, Color.white);
            }
        }

        static void BuildLandmarks(Transform parent, float[,] blockTop)
        {
            var pole = Mat("SignPole", new Color(0.45f, 0.45f, 0.47f), 0.4f);
            var graph = RoadGraph.BuildDefault();
            float rw = CityLayout.RoadWidth;

            foreach (var def in CityLayout.DefaultLandmarks)
            {
                graph.TryGetLandmark(def.name, out var node);
                var color = Mat("Landmark_" + def.name.Replace(" ", ""), def.color, 0.3f);
                var go = new GameObject("Landmark_" + def.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<Landmark>().Apply(def);

                var roadMid = node.Position;
                var toBlock = SnapToAxis(node.BlockCenter - roadMid);
                var facing = Quaternion.LookRotation(toBlock);
                go.transform.localPosition = roadMid;

                // Drop-off zone painted in the curb-side lane
                var zone = Box("DropOffZone", go.transform, toBlock * (rw / 2f - 1.6f) + Vector3.up * 0.02f, new Vector3(10f, 0.01f, 2.8f), color, false);
                zone.transform.localRotation = facing;

                // Curbside sign facing the road
                var signPos = toBlock * (rw / 2f + 1.0f) + Vector3.up * CityLayout.SidewalkHeight;
                Primitive(PrimitiveType.Cylinder, "SignPole", go.transform, signPos + Vector3.up * 1.75f, new Vector3(0.14f, 1.75f, 0.14f), pole, true);
                var panel = Box("SignPanel", go.transform, signPos + Vector3.up * 3.4f, new Vector3(2.8f, 0.9f, 0.08f), color, false);
                panel.transform.localRotation = facing;
                Text(def.name, go.transform, signPos + Vector3.up * 3.4f - toBlock * 0.06f, facing, 0.07f, Color.white);

                // Floating label above the block, visible from the overview camera
                float top = blockTop[def.blockX, def.blockZ];
                var label = Text(def.name, go.transform, node.BlockCenter - roadMid + Vector3.up * (top + 12f), Quaternion.identity, 0.8f, Color.white);
                label.fontStyle = FontStyle.Bold;
                label.gameObject.name = "FloatingLabel";
                label.gameObject.AddComponent<Billboard>();
            }
        }

        static Vector3 SnapToAxis(Vector3 v) =>
            Mathf.Abs(v.x) > Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(v.z));

        static void FrameMainCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float e = CityLayout.Extent;
            cam.transform.position = new Vector3(-e * 0.55f, e * 0.35f, -e * 0.55f);
            cam.transform.LookAt(Vector3.zero);
            cam.farClipPlane = 2000f;
        }

        /// <summary>Billboards only turn at runtime; face them toward the main camera in the saved scene too.</summary>
        static void OrientLabels(Transform root)
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var b in root.GetComponentsInChildren<Billboard>())
            {
                var dir = b.transform.position - cam.transform.position;
                dir.y = 0;
                b.transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        // ---------------------------------------------------------------- helpers

        static Material Mat(string name, Color c, float smooth)
        {
            if (!AssetDatabase.IsValidFolder("Assets/City")) AssetDatabase.CreateFolder("Assets", "City");
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder("Assets/City", "Materials");
            string path = MaterialDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Depth-tested material for TextMesh labels (the font's default material draws through walls).</summary>
        static Material TextMaterial()
        {
            string path = MaterialDir + "/Text3D.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("AIDrive/Text3D"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.mainTexture = font.material.mainTexture;
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.isStatic = true;
            return go;
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool collider = true) =>
            Primitive(PrimitiveType.Cube, name, parent, pos, size, mat, collider);

        static Transform Group(string name, Transform parent)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.isStatic = true;
            return g.transform;
        }

        static TextMesh Text(string text, Transform parent, Vector3 pos, Quaternion rot, float charSize, Color color)
        {
            var go = new GameObject("Text_" + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = font;
            tm.fontSize = 100;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = textMat;
            return tm;
        }
    }
}
