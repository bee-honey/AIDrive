using UnityEngine;

namespace AIDrive.City
{
    /// <summary>A named destination in the scene. The road graph is built from these.</summary>
    public class Landmark : MonoBehaviour
    {
        public string displayName = "Landmark";
        public int blockX;
        public int blockZ;
        public RoadSide side;
        public Color color = Color.white;

        public LandmarkDef ToDef() => new LandmarkDef(displayName, blockX, blockZ, side, color);

        public void Apply(LandmarkDef def)
        {
            displayName = def.name;
            blockX = def.blockX;
            blockZ = def.blockZ;
            side = def.side;
            color = def.color;
        }
    }
}
