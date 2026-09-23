using AIDrive.Navigation;
using AIDrive.UI;
using AIDrive.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AIDrive.Editor
{
    /// <summary>Builds the primitive car prefab and places it in the scene, parked at Home.</summary>
    public static class CarBuilder
    {
        const string PrefabDir = "Assets/Vehicle";
        const string PrefabPath = PrefabDir + "/Car.prefab";
        const string MaterialDir = "Assets/Vehicle/Materials";
        const string SpawnLandmark = "Home";

        [MenuItem("AIDrive/Create Car")]
        public static void CreateCar()
        {
            var old = GameObject.Find("Car");
            if (old != null) Undo.DestroyObjectImmediate(old);

            var prefab = BuildPrefab();
            var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            car.name = "Car";

            var graph = CityMap.Instance.Graph;
            graph.TryGetLandmark(SpawnLandmark, out var home);
            var pose = LanePath.CurbPose(home, LaneSettings.Default);
            car.transform.SetPositionAndRotation(pose.position, pose.rotation);

            var cam = Camera.main;
            CameraRig rig = null;
            if (cam != null)
            {
                rig = cam.GetComponent<CameraRig>();
                if (rig == null) rig = Undo.AddComponent<CameraRig>(cam.gameObject);
                rig.target = car.transform;
                rig.mode = CameraRig.Mode.Chase;
            }

            var hud = car.GetComponent<DriveHud>();
            hud.autopilot = car.GetComponent<Autopilot>();
            hud.cameraRig = rig;

            Undo.RegisterCreatedObjectUndo(car, "Create Car");
            EditorSceneManager.MarkSceneDirty(car.scene);
            Selection.activeGameObject = car;
        }

        static GameObject BuildPrefab()
        {
            EnsureFolder("Assets", "Vehicle");
            EnsureFolder(PrefabDir, "Materials");

            var body = Mat("CarBody", new Color(0.8f, 0.12f, 0.1f), 0.7f);
            var glass = Mat("CarGlass", new Color(0.1f, 0.14f, 0.2f), 0.9f);
            var tire = Mat("Tire", new Color(0.06f, 0.06f, 0.06f), 0.1f);
            var head = Mat("Headlight", new Color(1f, 0.95f, 0.75f), 0.9f, true);
            var tail = Mat("Taillight", new Color(0.9f, 0.05f, 0.05f), 0.9f, true);

            var root = new GameObject("Car");
            Part(PrimitiveType.Cube, "Body", root.transform, new Vector3(0, 0.65f, 0), new Vector3(1.9f, 0.6f, 4.2f), body);
            Part(PrimitiveType.Cube, "Cabin", root.transform, new Vector3(0, 1.22f, -0.25f), new Vector3(1.7f, 0.55f, 2.2f), glass);
            foreach (float x in new[] { -0.6f, 0.6f })
            {
                Part(PrimitiveType.Cube, "Headlight", root.transform, new Vector3(x, 0.72f, 2.1f), new Vector3(0.4f, 0.15f, 0.04f), head);
                Part(PrimitiveType.Cube, "Taillight", root.transform, new Vector3(x, 0.75f, -2.1f), new Vector3(0.4f, 0.12f, 0.04f), tail);
            }

            var vc = root.AddComponent<VehicleController>();
            var pivots = new Transform[2];
            var meshes = new Transform[4];
            int w = 0;
            foreach (float z in new[] { vc.wheelbase / 2f, -vc.wheelbase / 2f })
            foreach (float x in new[] { -0.85f, 0.85f })
            {
                bool front = z > 0;
                var pivot = new GameObject(front ? "FrontWheelPivot" : "RearWheelPivot").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(x, vc.wheelRadius, z);
                var mesh = Part(PrimitiveType.Cylinder, "Wheel", pivot, Vector3.zero,
                    new Vector3(vc.wheelRadius * 2f, 0.125f, vc.wheelRadius * 2f), tire);
                mesh.transform.localRotation = Quaternion.Euler(0, 0, 90);
                if (front) pivots[x < 0 ? 0 : 1] = pivot;
                meshes[w++] = mesh.transform;
            }
            vc.frontWheelPivots = pivots;
            vc.wheelMeshes = meshes;

            // Collider sits above the 0.2 m sidewalk so only real obstacles register as collisions.
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.85f, 0);
            col.size = new Vector3(1.9f, 1.1f, 4.2f);

            var rb = root.GetComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            root.AddComponent<RaycastSensors>();
            root.AddComponent<Autopilot>();
            root.AddComponent<DriveHud>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static Material Mat(string name, Color c, float smooth, bool emissive = false)
        {
            string path = MaterialDir + "/" + name + ".mat";
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
                m.SetColor("_EmissionColor", c * 1.5f);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
