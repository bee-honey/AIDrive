using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIDrive.Traffic
{
    public enum SignalAxis { NorthSouth, EastWest }
    public enum SignalState { Green, Yellow, Red }

    /// <summary>
    /// Fixed-time two-phase plan: north–south green → yellow → all-red, then east–west green → yellow → all-red.
    /// </summary>
    public static class SignalTiming
    {
        public const float Green = 10f, Yellow = 3f, AllRed = 1.5f;
        public const float Half = Green + Yellow + AllRed;
        public const float Cycle = 2f * Half;

        public static SignalState StateAt(float time, float offset, SignalAxis axis)
        {
            float p = Mod(time + offset, Cycle);
            if (axis == SignalAxis.EastWest) p = Mod(p - Half, Cycle);
            if (p < Green) return SignalState.Green;
            if (p < Green + Yellow) return SignalState.Yellow;
            return SignalState.Red;
        }

        /// <summary>Axis of the phase that controls traffic travelling in <paramref name="travelDir"/>.</summary>
        public static SignalAxis AxisOf(Vector3 travelDir) =>
            Mathf.Abs(travelDir.z) > Mathf.Abs(travelDir.x) ? SignalAxis.NorthSouth : SignalAxis.EastWest;

        /// <summary>Deterministic per-intersection offset so neighbouring signals aren't all in sync.</summary>
        public static float OffsetFor(int gridX, int gridZ) => (gridX * 5 + gridZ * 11) % (int)Cycle;

        static float Mod(float a, float m) => (a % m + m) % m;
    }

    [Serializable]
    public class SignalHead
    {
        public SignalAxis axis;
        public Renderer red, yellow, green;
    }

    /// <summary>
    /// The signal controller for one intersection. Runs on scene time so every scene load starts the
    /// same cycle (deterministic tests and scenarios). Tests and scenarios can force a state per axis.
    /// </summary>
    public class TrafficSignal : MonoBehaviour
    {
        public int gridX, gridZ;
        public float offset;
        public SignalHead[] heads = Array.Empty<SignalHead>();
        public Material redOn, redOff, yellowOn, yellowOff, greenOn, greenOff;

        static readonly Dictionary<(int, int), TrafficSignal> Registry = new Dictionary<(int, int), TrafficSignal>();

        SignalState? forcedNS, forcedEW;
        SignalState shownNS = (SignalState)(-1), shownEW = (SignalState)(-1);

        public static TrafficSignal At(int gridX, int gridZ) =>
            Registry.TryGetValue((gridX, gridZ), out var s) && s != null ? s : null;

        public static IEnumerable<TrafficSignal> All => Registry.Values;

        public string Name => $"{City.CityLayout.IntersectionName(gridX, gridZ)}";

        public SignalState StateFor(SignalAxis axis)
        {
            var forced = axis == SignalAxis.NorthSouth ? forcedNS : forcedEW;
            return forced ?? SignalTiming.StateAt(Time.timeSinceLevelLoad, offset, axis);
        }

        /// <summary>Pin one axis to a state (null = back to the timed plan). For tests and scenarios.</summary>
        public void Force(SignalAxis axis, SignalState? state)
        {
            if (axis == SignalAxis.NorthSouth) forcedNS = state; else forcedEW = state;
        }

        void OnEnable() => Registry[(gridX, gridZ)] = this;

        void OnDisable()
        {
            if (Registry.TryGetValue((gridX, gridZ), out var s) && s == this) Registry.Remove((gridX, gridZ));
        }

        void Update()
        {
            var ns = StateFor(SignalAxis.NorthSouth);
            var ew = StateFor(SignalAxis.EastWest);
            if (ns == shownNS && ew == shownEW) return;
            shownNS = ns;
            shownEW = ew;
            foreach (var h in heads)
            {
                var st = h.axis == SignalAxis.NorthSouth ? ns : ew;
                h.red.sharedMaterial = st == SignalState.Red ? redOn : redOff;
                h.yellow.sharedMaterial = st == SignalState.Yellow ? yellowOn : yellowOff;
                h.green.sharedMaterial = st == SignalState.Green ? greenOn : greenOff;
            }
        }
    }
}
