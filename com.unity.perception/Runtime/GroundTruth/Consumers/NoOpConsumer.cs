using System;
using UnityEngine.Perception.GroundTruth.DataModel;

namespace UnityEngine.Perception.GroundTruth.Consumers
{
    public class NoOpConsumer : ConsumerEndpoint
    {
        protected override bool IsComplete()
        {
            return true;
        }

        public override object Clone()
        {
            return CreateInstance<NoOpConsumer>();
        }

        public override void OnSimulationStarted(SimulationMetadata metadata)
        {
            // Do nothing, drop everything on the floor
        }

        public override void OnFrameGenerated(Frame frame)
        {
            // Do nothing, drop everything on the floor
        }

        public override void OnSimulationCompleted(CompletionMetadata metadata)
        {
            // Do nothing, drop everything on the floor
        }
    }
}
