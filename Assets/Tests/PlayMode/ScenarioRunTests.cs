using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AIDrive.Scenario;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIDrive.Tests
{
    /// <summary>Runs every scenario file end-to-end and checks it produces its expected outcome.</summary>
    public class ScenarioRunTests : DriveTestBase
    {
        static IEnumerable<string> ScenarioNames() => ScenarioLibrary.LoadAll().Select(s => s.name);

        [UnityTest]
        public IEnumerator Scenario([ValueSource(nameof(ScenarioNames))] string name)
        {
            var def = ScenarioLibrary.Load(name);
            var runner = Autopilot.GetComponent<ScenarioRunner>();
            Assert.IsNotNull(runner, "car has no ScenarioRunner");

            yield return runner.Run(def);
            var r = runner.LastResult;
            Debug.Log($"[{name}] {JsonUtility.ToJson(r)}");
            Assert.IsTrue(r.passed, r.failure);
        }
    }
}
