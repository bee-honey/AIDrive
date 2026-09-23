using AIDrive.City;
using AIDrive.Scenario;
using UnityEditor;
using UnityEngine;

namespace AIDrive.Editor
{
    /// <summary>
    /// Builds every prop prefab procedurally (cone mesh, striped barricades, parked cars, street lamp) and the
    /// <see cref="PropLibrary"/> asset that the runtime loads from Resources.
    /// Prop local axes: +Z faces the viewer (oncoming traffic for barricades), +X is across the road.
    /// </summary>
    public static class PropBuilder
    {
        const string Dir = "Assets/Props";
        const string MatDir = Dir + "/Materials";
        const string LibraryPath = "Assets/Resources/" + PropLibrary.ResourcePath + ".asset";

        static Material orange, white, red, black, concrete, lampPole, lampGlow, signWhite, textMat;
        static Font font;

        [MenuItem("AIDrive/Build Props")]
        public static PropLibrary BuildAll()
        {
            Folder("Assets", "Props");
            Folder(Dir, "Materials");
            Folder("Assets", "Resources");

            orange = Mat("PropOrange", new Color(1f, 0.42f, 0.05f), 0.4f);
            white = Mat("PropWhite", new Color(0.95f, 0.95f, 0.95f), 0.5f);
            red = Mat("PropRed", new Color(0.85f, 0.08f, 0.08f), 0.4f);
            black = Mat("PropBlack", new Color(0.08f, 0.08f, 0.08f), 0.2f);
            concrete = Mat("Concrete", new Color(0.66f, 0.65f, 0.62f), 0.1f);
            lampPole = Mat("LampPole", new Color(0.22f, 0.24f, 0.26f), 0.6f);
            lampGlow = Mat("LampGlow", new Color(1f, 0.9f, 0.6f), 0.9f, emissive: true);
            signWhite = Mat("SignWhite", Color.white, 0.3f);
            textMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/City/Materials/Text3D.mat");
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var lib = AssetDatabase.LoadAssetAtPath<PropLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<PropLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            lib.cone = Save(BuildCone(), "Cone");
            lib.barrier = Save(BuildBarrier(), "Barrier");
            lib.roadClosed = Save(BuildRoadClosed(), "RoadClosed");
            lib.jerseyBarrier = Save(BuildJersey(), "JerseyBarrier");
            lib.parkedCars = new[]
            {
                Save(BuildParkedCar("Blue", new Color(0.15f, 0.3f, 0.75f)), "ParkedCar_Blue"),
                Save(BuildParkedCar("White", new Color(0.9f, 0.9f, 0.88f)), "ParkedCar_White"),
                Save(BuildParkedCar("Green", new Color(0.15f, 0.45f, 0.25f)), "ParkedCar_Green"),
                Save(BuildParkedCar("Grey", new Color(0.35f, 0.36f, 0.38f)), "ParkedCar_Grey"),
            };
            lib.streetLamp = Save(BuildStreetLamp(), "StreetLamp");
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return lib;
        }

        public static PropLibrary EnsureBuilt()
        {
            var lib = AssetDatabase.LoadAssetAtPath<PropLibrary>(LibraryPath);
            return lib != null && lib.streetLamp != null ? lib : BuildAll();
        }

        // ---------------------------------------------------------------- props

        static GameObject BuildCone()
        {
            var root = Root("Cone", ObstacleKind.Cone);
            Box("Base", root, new Vector3(0, 0.02f, 0), new Vector3(0.45f, 0.04f, 0.45f), black);
            var cone = new GameObject("Body");
            cone.transform.SetParent(root.transform, false);
            cone.transform.localPosition = new Vector3(0, 0.04f, 0);
            cone.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
            cone.AddComponent<MeshRenderer>().sharedMaterial = orange;
            // Reflective band: a short, wide cylinder around the cone's middle.
            Primitive(PrimitiveType.Cylinder, "Band", root, new Vector3(0, 0.42f, 0), new Vector3(0.21f, 0.05f, 0.21f), white);
            Collider(root, new Vector3(0, 0.375f, 0), new Vector3(0.45f, 0.75f, 0.45f));
            return root;
        }

        /// <summary>A-frame barricade, 1.8 m wide: blocks one lane.</summary>
        static GameObject BuildBarrier()
        {
            var root = Root("Barrier", ObstacleKind.Barrier);
            foreach (float x in new[] { -0.8f, 0.8f })
            {
                var leg = Box("Leg", root, new Vector3(x, 0.5f, 0), new Vector3(0.06f, 1.05f, 0.06f), white);
                leg.transform.localRotation = Quaternion.Euler(12f, 0, 0);
                var back = Box("Leg", root, new Vector3(x, 0.5f, -0.2f), new Vector3(0.06f, 1.05f, 0.06f), white);
                back.transform.localRotation = Quaternion.Euler(-12f, 0, 0);
            }
            StripedBoard(root, new Vector3(0, 0.85f, 0.05f), 1.8f, 0.22f, 6);
            StripedBoard(root, new Vector3(0, 0.45f, 0.1f), 1.8f, 0.22f, 6);
            Collider(root, new Vector3(0, 0.55f, -0.08f), new Vector3(1.8f, 1.1f, 0.45f));
            return root;
        }

        /// <summary>5.6 m barricade with a ROAD CLOSED sign: closes both lanes in one direction.</summary>
        static GameObject BuildRoadClosed()
        {
            var root = Root("RoadClosed", ObstacleKind.RoadClosed);
            foreach (float x in new[] { -2.6f, 0f, 2.6f })
            {
                Box("Post", root, new Vector3(x, 0.6f, 0), new Vector3(0.1f, 1.2f, 0.1f), white);
                Box("Foot", root, new Vector3(x, 0.03f, 0), new Vector3(0.12f, 0.06f, 0.7f), black);
            }
            StripedBoard(root, new Vector3(0, 1.0f, 0.07f), 5.6f, 0.28f, 14);
            StripedBoard(root, new Vector3(0, 0.55f, 0.07f), 5.6f, 0.28f, 14);
            Box("SignPost", root, new Vector3(0, 1.45f, 0), new Vector3(0.08f, 0.6f, 0.08f), white);
            Box("Sign", root, new Vector3(0, 1.85f, 0.02f), new Vector3(2.2f, 0.55f, 0.05f), signWhite);
            Box("SignBorder", root, new Vector3(0, 1.85f, 0.0f), new Vector3(2.3f, 0.65f, 0.04f), red);
            Label("ROAD CLOSED", root, new Vector3(0, 1.85f, 0.06f), 0.042f, new Color(0.75f, 0.05f, 0.05f));
            Collider(root, new Vector3(0, 0.7f, 0.03f), new Vector3(5.6f, 1.4f, 0.4f));
            return root;
        }

        /// <summary>Concrete jersey barrier, 2.8 m across: blocks one lane.</summary>
        static GameObject BuildJersey()
        {
            var root = Root("JerseyBarrier", ObstacleKind.JerseyBarrier);
            Box("Base", root, new Vector3(0, 0.2f, 0), new Vector3(2.8f, 0.4f, 0.6f), concrete);
            Box("Top", root, new Vector3(0, 0.6f, 0), new Vector3(2.8f, 0.4f, 0.25f), concrete);
            Collider(root, new Vector3(0, 0.4f, 0), new Vector3(2.8f, 0.8f, 0.6f));
            return root;
        }

        static GameObject BuildParkedCar(string variant, Color color)
        {
            var root = Root("ParkedCar_" + variant, ObstacleKind.ParkedCar);
            var body = CarBuilder.Mat("ParkedCar_" + variant, color, 0.6f);
            CarBuilder.BuildBody(root, body, 2.6f, 0.36f, out _, out _);
            return root;
        }

        /// <summary>Sidewalk lamp: pole at the origin, arm reaching +Z over the road. Emissive head, no real light.</summary>
        static GameObject BuildStreetLamp()
        {
            var root = new GameObject("StreetLamp");
            Primitive(PrimitiveType.Cylinder, "Pole", root, new Vector3(0, 3f, 0), new Vector3(0.14f, 3f, 0.14f), lampPole, keepCollider: true);
            Box("Arm", root, new Vector3(0, 5.9f, 0.8f), new Vector3(0.08f, 0.08f, 1.6f), lampPole);
            Box("Head", root, new Vector3(0, 5.82f, 1.55f), new Vector3(0.3f, 0.12f, 0.55f), lampPole);
            Box("Glow", root, new Vector3(0, 5.75f, 1.55f), new Vector3(0.24f, 0.03f, 0.45f), lampGlow);
            return root;
        }

        // ---------------------------------------------------------------- helpers

        static GameObject Root(string name, ObstacleKind kind)
        {
            var go = new GameObject(name);
            go.AddComponent<Obstacle>().kind = kind;
            return go;
        }

        /// <summary>Alternating red/white segments facing +Z.</summary>
        static void StripedBoard(GameObject root, Vector3 centre, float width, float height, int stripes)
        {
            float w = width / stripes;
            for (int i = 0; i < stripes; i++)
            {
                float x = centre.x - width / 2f + w * (i + 0.5f);
                Box("Stripe", root, new Vector3(x, centre.y, centre.z), new Vector3(w, height, 0.04f), i % 2 == 0 ? red : white);
            }
        }

        static void Label(string text, GameObject root, Vector3 pos, float size, Color color)
        {
            foreach (var face in new[] { Vector3.back, Vector3.forward })
            {
                var go = new GameObject("Text");
                go.transform.SetParent(root.transform, false);
                // Front text sits just in front of the sign face; back text just behind the red border (z ≈ -0.02).
                go.transform.localPosition = new Vector3(pos.x, pos.y, face == Vector3.back ? pos.z : -0.03f);
                go.transform.localRotation = Quaternion.LookRotation(face);
                var tm = go.AddComponent<TextMesh>();
                tm.text = text;
                tm.font = font;
                tm.fontSize = 100;
                tm.fontStyle = FontStyle.Bold;
                tm.characterSize = size;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = color;
                go.GetComponent<MeshRenderer>().sharedMaterial = textMat;
            }
        }

        static void Collider(GameObject root, Vector3 centre, Vector3 size)
        {
            var c = root.AddComponent<BoxCollider>();
            c.center = centre;
            c.size = size;
        }

        static GameObject Box(string name, GameObject parent, Vector3 pos, Vector3 size, Material mat) =>
            Primitive(PrimitiveType.Cube, name, parent, pos, size, mat);

        static GameObject Primitive(PrimitiveType type, string name, GameObject parent, Vector3 pos, Vector3 size,
                                    Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<UnityEngine.Collider>());
            return go;
        }

        static Mesh ConeMesh()
        {
            const string path = Dir + "/ConeMesh.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            const int seg = 20;
            const float rBottom = 0.17f, rTop = 0.03f, h = 0.68f;
            var verts = new Vector3[seg * 2 + 2];
            var tris = new int[seg * 9];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts[i] = dir * rBottom;
                verts[seg + i] = dir * rTop + Vector3.up * h;
            }
            int topCentre = seg * 2;
            verts[topCentre] = Vector3.up * h;
            verts[topCentre + 1] = Vector3.zero;
            int t = 0;
            for (int i = 0; i < seg; i++)
            {
                int n = (i + 1) % seg;
                tris[t++] = i; tris[t++] = seg + i; tris[t++] = n;
                tris[t++] = n; tris[t++] = seg + i; tris[t++] = seg + n;
                tris[t++] = seg + i; tris[t++] = topCentre; tris[t++] = seg + n;
            }
            var mesh = new Mesh { name = "Cone", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static GameObject Save(GameObject go, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Dir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static Material Mat(string name, Color c, float smooth, bool emissive = false)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 2f);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Folder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
