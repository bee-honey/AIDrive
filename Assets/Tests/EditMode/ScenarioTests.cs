using System.Linq;
using AIDrive.City;
using AIDrive.Navigation;
using AIDrive.Scenario;
using NUnit.Framework;
using UnityEngine;

namespace AIDrive.Tests
{
    public class RoadAddressTests
    {
        [Test]
        public void Resolves_EastboundInnerLane_Midblock()
        {
            Assert.IsTrue(RoadAddress.TryResolve("E2", "S3", "S4", "eastbound", LanePosition.Inner, 0.5f, out var spot, out var err), err);
            float midX = (CityLayout.RoadCenter(2) + CityLayout.RoadCenter(3)) / 2f;
            Assert.AreEqual(midX, spot.Position.x, 0.01f);
            // Eastbound traffic keeps right = south of the centre line.
            Assert.AreEqual(CityLayout.RoadCenter(1) - RoadAddress.InnerLane, spot.Position.z, 0.01f);
            Assert.AreEqual(Vector3.right, spot.Direction);
        }

        [Test]
        public void At_IsMeasuredInTravelDirection_AndCrossStreetOrderDoesNotMatter()
        {
            RoadAddress.TryResolve("E2", "S3", "S4", "eastbound", LanePosition.Curb, 0.1f, out var east, out _);
            RoadAddress.TryResolve("E2", "S4", "S3", "westbound", LanePosition.Curb, 0.1f, out var west, out _);
            Assert.Less(east.Position.x, west.Position.x, "10% into the block from each driver's point of view");
            Assert.Greater(west.Position.z, CityLayout.RoadCenter(1), "westbound keeps right = north");
            Assert.AreEqual(CityLayout.RoadCenter(1) + RoadAddress.CurbLane, west.Position.z, 0.01f);
        }

        [Test]
        public void NorthboundOnSRoad()
        {
            Assert.IsTrue(RoadAddress.TryResolve("S7", "E3", "E4", "northbound", LanePosition.Inner, 0f, out var spot, out var err), err);
            Assert.AreEqual(CityLayout.RoadCenter(6) + RoadAddress.InnerLane, spot.Position.x, 0.01f);
            Assert.Greater(spot.Position.z, CityLayout.RoadCenter(2));
        }

        [TestCase("E9", "S3", "S4", "eastbound", "Unknown street")]
        [TestCase("E2", "S3", "S5", "eastbound", "not adjacent")]
        [TestCase("E2", "E3", "E4", "eastbound", "must be streets crossing")]
        [TestCase("E2", "S3", "S4", "northbound", "has no northbound")]
        [TestCase("E2", "S3", "S4", "sideways", "Unknown direction")]
        public void RejectsInvalidAddresses(string street, string a, string b, string dir, string expected)
        {
            Assert.IsFalse(RoadAddress.TryResolve(street, a, b, dir, LanePosition.Inner, 0.5f, out _, out var err));
            StringAssert.Contains(expected, err);
        }
    }

    public class ScenarioFileTests
    {
        [Test]
        public void AllScenarioFiles_LoadAndValidate()
        {
            var scenarios = ScenarioLibrary.LoadAll();
            Assert.GreaterOrEqual(scenarios.Count, 6);
            var graph = RoadGraph.BuildDefault();
            foreach (var s in scenarios)
            {
                var errors = ScenarioLibrary.Validate(s, graph);
                Assert.IsEmpty(errors, $"{s.name}: {string.Join("; ", errors)}");
            }
            CollectionAssert.AllItemsAreUnique(scenarios.Select(s => s.name));
        }

        [Test]
        public void Validate_ReportsBadProps()
        {
            var def = new ScenarioDef
            {
                name = "bad", start = "Home", destination = "Nowhere", expect = "Arrived",
                props = new[] { new PropPlacement { type = "Tank", street = "E2", between = new[] { "S3", "S4" }, direction = "eastbound", lane = "inner" } },
            };
            var errors = ScenarioLibrary.Validate(def, RoadGraph.BuildDefault());
            Assert.IsTrue(errors.Any(e => e.Contains("Nowhere")));
            Assert.IsTrue(errors.Any(e => e.Contains("Tank")));
        }

        [Test]
        public void PropLibrary_HasEveryPrefab()
        {
            var lib = PropLibrary.Load();
            Assert.IsNotNull(lib, "Resources/PropLibrary missing — run AIDrive → Build Props");
            foreach (ObstacleKind kind in System.Enum.GetValues(typeof(ObstacleKind)))
            {
                var prefab = lib.Prefab(kind);
                Assert.IsNotNull(prefab, kind.ToString());
                Assert.IsNotNull(prefab.GetComponent<Collider>(), $"{kind} needs a collider for the sensors");
                Assert.AreEqual(kind, prefab.GetComponent<Obstacle>().kind);
            }
            Assert.IsNotNull(lib.streetLamp);
        }
    }
}
