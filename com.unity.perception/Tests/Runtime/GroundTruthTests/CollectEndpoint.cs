﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.Perception.GroundTruth.DataModel;

namespace GroundTruthTests
{
    public class CollectEndpoint : ConsumerEndpoint
    {
        public List<SensorDefinition> sensors = new List<SensorDefinition>();
        public List<AnnotationDefinition> annotationDefinitions = new List<AnnotationDefinition>();
        public List<MetricDefinition> metricDefinitions = new List<MetricDefinition>();

        public struct SimulationRun
        {
            public int TotalFrames => frames.Count;
            public List<Frame> frames;
            public SimulationMetadata metadata;
        }

        public List<SimulationRun> collectedRuns = new List<SimulationRun>();
        public SimulationRun currentRun;

        public override void OnSensorRegistered(SensorDefinition sensor)
        {
            sensors.Add(sensor);
        }

        public override void OnAnnotationRegistered(AnnotationDefinition annotationDefinition)
        {
            annotationDefinitions.Add(annotationDefinition);
        }

        public override void OnMetricRegistered(MetricDefinition metricDefinition)
        {
            metricDefinitions.Add(metricDefinition);
        }

        protected override bool IsComplete()
        {
            return true;
        }

        public override void OnSimulationStarted(SimulationMetadata metadata)
        {
            currentRun = new SimulationRun
            {
                frames = new List<Frame>()
            };
            Debug.Log("Collect Enpoint OnSimulationStarted");
        }

        public override void OnFrameGenerated(Frame frame)
        {
            if (currentRun.frames == null)
            {
                Debug.LogError("Current run frames is null, probably means that OnSimulationStarted was never called");
            }

            currentRun.frames?.Add(frame);
            Debug.Log("Collect Enpoint OnFrameGenerted");
        }

        public override void OnSimulationCompleted(CompletionMetadata metadata)
        {
            currentRun.metadata = metadata;
            collectedRuns.Add(currentRun);
            Debug.Log("Collect Enpoint OnSimulationCompleted");
        }
    }
}
