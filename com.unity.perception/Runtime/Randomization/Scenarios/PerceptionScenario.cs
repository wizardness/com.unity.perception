using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Simulation;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.Rendering;

namespace UnityEngine.Perception.Randomization.Scenarios
{
    /// <summary>
    /// Derive this class to configure perception data capture while coordinating a scenario
    /// </summary>
    /// <typeparam name="T">The type of scenario constants to serialize</typeparam>
    public abstract class PerceptionScenario<T> : Scenario<T> where T : ScenarioConstants, new()
    {
        /// <summary>
        /// The guid used to identify this scenario's Iteration Metric Definition
        /// </summary>
        const string k_ScenarioIterationMetricDefinitionId = "DB1B258E-D1D0-41B6-8751-16F601A2E230";

        /// <summary>
        /// The metric definition used to report the current scenario iteration
        /// </summary>
//        MetricDefinition m_IterationMetricDefinition;

        /// <summary>
        /// The scriptable render pipeline hook used to capture perception data skips the first frame of the simulation
        /// when running locally, so this flag is used to track whether the first frame has been skipped yet.
        /// </summary>
        protected bool m_SkippedFirstFrame;

        /// <inheritdoc/>
        protected override bool isScenarioReadyToStart
        {
            get
            {
                if (!m_SkippedFirstFrame)
                {
                    m_SkippedFirstFrame = true;
                    return false;
                }
                return true;
            }
        }
#if false
        class ShutdownCondition : ICondition
        {
            public bool HasConditionBeenMet()
            {
                if (DatasetCapture.Instance.ReadyToShutdown)
                {
                    Debug.Log("Triggered dc ready");
                }

                return DatasetCapture.Instance.ReadyToShutdown;
            }
        }
#endif
        /// <inheritdoc/>
        protected override void OnStart()
        {
            var md = new MetricDefinition();
            DatasetCapture.Instance.RegisterMetric(md);

//            Manager.Instance.ShutdownCondition = new ShutdownCondition();

            Manager.Instance.ShutdownNotification += () =>
            {
//                DatasetCapture.Instance.ResetSimulation();
                Quit();
            };

#if false
            m_IterationMetricDefinition = DatasetCapture.RegisterMetricDefinition(
                "scenario_iteration", "Iteration information for dataset sequences",
                Guid.Parse(k_ScenarioIterationMetricDefinitionId));

            var randomSeedMetricDefinition = DatasetCapture.RegisterMetricDefinition(
                "random-seed",
                "The random seed used to initialize the random state of the simulation. Only triggered once per simulation.",
                Guid.Parse("14adb394-46c0-47e8-a3f0-99e754483b76"));
            DatasetCapture.ReportMetric(randomSeedMetricDefinition, new[] { genericConstants.randomSeed });
#endif
        }

        /// <inheritdoc/>
        protected override void OnIterationStart()
        {
            DatasetCapture.Instance.StartNewSequence();
#if false
            DatasetCapture.ReportMetric(m_IterationMetricDefinition, new[]
            {
                new IterationMetricData { iteration = currentIteration }
            });
#endif
        }

        /// <inheritdoc/>

#if false
        protected override IEnumerator OnComplete()
        {
            yield return StartCoroutine(DatasetCapture.Instance.ResetSimulation());
#else
        protected override void OnComplete()
        {
            DatasetCapture.Instance.ResetSimulation();

            //Manager.Instance.ShutdownAfterFrames(105);
            //Manager.Instance.Shutdown();
            //DatasetCapture.Instance.ResetSimulation();
#endif
//            Quit();
        }
#if false
        /// <summary>
        /// Used to report a scenario iteration as a perception metric
        /// </summary>
        struct IterationMetricData
        {
            // ReSharper disable once NotAccessedField.Local
            public int iteration;
        }
#endif
    }
}
