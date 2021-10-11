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
        MetricDefinition m_IterationMetricDefinition;

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

        /// <inheritdoc/>
        protected override void OnStart()
        {
            Manager.Instance.ShutdownNotification += () =>
            {
                Quit();
            };

            m_IterationMetricDefinition = new MetricDefinition("scenario_iteration", "Iteration information for dataset sequences");
            DatasetCapture.Instance.RegisterMetric(m_IterationMetricDefinition);

            var randomSeedMetricDefinition = new MetricDefinition("random-seed", "The random seed used to initialize the random state of the simulation. Only triggered once per simulation.");
            DatasetCapture.Instance.RegisterMetric(randomSeedMetricDefinition);

            DatasetCapture.Instance.ReportMetric(randomSeedMetricDefinition, new object[] { genericConstants.randomSeed });
        }

        /// <inheritdoc/>
        protected override void OnIterationStart()
        {
            DatasetCapture.Instance.StartNewSequence();

            DatasetCapture.Instance.ReportMetric(m_IterationMetricDefinition, new object[]
            {
                new IterationMetricData { iteration = currentIteration }
            });
        }

        static IEnumerator WaitUntilWritesAreComplete()
        {
            var dcWatcher = new DatasetCapture.WaitUntilComplete();
            yield return dcWatcher;
        }

        /// <inheritdoc/>
        protected override void OnComplete()
        {
            DatasetCapture.Instance.ResetSimulation();
            StartCoroutine(WaitUntilWritesAreComplete());
            Quit();
        }

        /// <summary>
        /// Used to report a scenario iteration as a perception metric
        /// </summary>
        struct IterationMetricData
        {
            // ReSharper disable once NotAccessedField.Local
            public int iteration;
        }
    }
}
