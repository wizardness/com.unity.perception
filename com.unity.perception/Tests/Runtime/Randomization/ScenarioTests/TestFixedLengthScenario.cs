using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.Perception.Randomization.Scenarios;

namespace RandomizationTests.ScenarioTests
{
    [AddComponentMenu("")]
    class TestFixedLengthScenario : FixedLengthScenario
    {
#if false
        protected override IEnumerator OnComplete()
        {
            yield return StartCoroutine(DatasetCapture.Instance.ResetSimulation());
        }
#else
        protected override void OnComplete()
        {
            DatasetCapture.Instance.ResetSimulation();
        }
#endif
    }
}
