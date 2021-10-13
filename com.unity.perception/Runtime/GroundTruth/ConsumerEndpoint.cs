using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Simulation;
using UnityEngine.Perception.GroundTruth.DataModel;

namespace UnityEngine.Perception.GroundTruth
{
    [Serializable]
    public abstract class ConsumerEndpoint : ScriptableObject, ICloneable
    {
        IEnumerator WaitForComplete()
        {
            yield return new WaitUntil(IsComplete);
        }

        protected abstract bool IsComplete();

        public abstract object Clone();

        /// <summary>
        /// Called when the simulation begins. Provides simulation wide metadata to
        /// the consumer.
        /// </summary>
        /// <param name="metadata">Metadata describing the active simulation</param>
        public abstract void SimulationStarted(SimulationMetadata metadata);

        public virtual void SensorRegistered(SensorDefinition sensor) { }

        public virtual void AnnotationRegistered(AnnotationDefinition annotationDefinition) { }
        public virtual void MetricRegistered(MetricDefinition metricDefinition) { }

        /// <summary>
        /// Called at the end of each frame. Contains all of the generated data for the
        /// frame. This method is called after the frame has entirely finished processing.
        /// </summary>
        /// <param name="frame">The frame data.</param>
        public abstract void FrameGenerated(Frame frame);

        /// <summary>
        /// Called at the end of the simulation. Contains metadata describing the entire
        /// simulation process.
        /// </summary>
        /// <param name="metadata">Metadata describing the entire simulation process</param>
        public abstract void SimulationCompleted(CompletionMetadata metadata);
    }
}
