using UnityEngine;

namespace AIDrive.Scenario
{
    /// <summary>
    /// Prefab references for every prop, loaded at runtime from Resources/PropLibrary.
    /// Built by AIDrive → Build Props.
    /// </summary>
    public class PropLibrary : ScriptableObject
    {
        public const string ResourcePath = "PropLibrary";

        public GameObject cone;
        public GameObject barrier;
        public GameObject roadClosed;
        public GameObject jerseyBarrier;
        public GameObject[] parkedCars;
        public GameObject streetLamp;

        static PropLibrary cached;

        public static PropLibrary Load()
        {
            if (cached == null) cached = Resources.Load<PropLibrary>(ResourcePath);
            return cached;
        }

        public GameObject Prefab(ObstacleKind kind, int variant = 0)
        {
            switch (kind)
            {
                case ObstacleKind.Cone: return cone;
                case ObstacleKind.Barrier: return barrier;
                case ObstacleKind.RoadClosed: return roadClosed;
                case ObstacleKind.JerseyBarrier: return jerseyBarrier;
                case ObstacleKind.ParkedCar: return parkedCars[Mathf.Abs(variant) % parkedCars.Length];
                default: return null;
            }
        }
    }
}
