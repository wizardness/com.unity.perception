using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Simulation;
using UnityEngine.Perception.GroundTruth.DataModel;

namespace UnityEngine.Perception.GroundTruth
{
#if false
    public partial class SimulationState
    {
        Dictionary<int, int> m_SequenceMap = new Dictionary<int, int>();

        Sensor ToSensor(PendingFrame pendingFrame, SimulationState simulationState, int captureFileIndex)
        {
            var sensor = new RgbSensor
            {
                Id = "camera",
                sensorType = "camera",
                description = "this is the description of the sensor",
                position = Vector3.zero,
                rotation = Vector3.zero,
                velocity = Vector3.zero,
                acceleration = Vector3.zero,
                imageFormat = "png",
                dimension = Vector2.zero,
                buffer = null
            };

            return sensor;
        }

        // TODO rename this to 'WritePendingFrames'
        void WritePendingCaptures(bool flush = false, bool writeCapturesFromThisFrame = false)
        {
            m_SerializeCapturesSampler.Begin();

            var pendingFramesToWrite = new List<KeyValuePair<PendingFrameId,PendingFrame>>(m_PendingFrames.Count);
            var currentFrame = Time.frameCount;

            foreach (var frame in m_PendingFrames)
            {
                var recordedFrame = m_PendingIdToFrameMap[frame.Value.PendingId];
                if ((writeCapturesFromThisFrame || recordedFrame < currentFrame) &&
                    frame.Value.IsReadyToReport())
                {
                    pendingFramesToWrite.Add(frame);
                }
            }

            foreach (var pf in pendingFramesToWrite)
            {
                m_PendingFrames.Remove(pf.Key);
            }

            IEnumerable<Sensor> ConvertToSensors(PendingFrame frame, SimulationState simulationState)
            {
                return frame.sensors.Values.Where(s => s.IsReadyToReport()).Select(s => s.ToSensor());
            }

            Frame ConvertToFrameData(PendingFrame pendingFrame, SimulationState simState)
            {
                var frameId = m_PendingIdToFrameMap[pendingFrame.PendingId];
                var frame = new Frame(frameId, pendingFrame.PendingId.Sequence, pendingFrame.PendingId.Step);

                frame.sensors = ConvertToSensors(pendingFrame, simState);
#if false
                foreach (var annotation in pendingFrame.annotations.Values)
                {
                    frame.annotations.Add(annotation);
                }

                foreach (var metric in pendingFrame.metrics.Values)
                {
                    frame.metrics.Add(metric);
                }
#endif
                return frame;
            }

            void Write(List<KeyValuePair<PendingFrameId, PendingFrame>> frames, SimulationState simulationState)
            {
#if true
                // TODO this needs to be done properly, we need to wait on all of the frames to come back so we
                // can report them, right now we are just going to jam up this thread waiting for them, also could
                // result in an endless loop if the frame never comes back
                while (frames.Any())
                {
                    var frame = frames.First();
                    if (frame.Value.IsReadyToReport())
                    {
                        frames.Remove(frame);
                        var converted = ConvertToFrameData(frame.Value, simulationState);
                        m_TotalFrames++;
                        consumerEndpoint.OnFrameGenerated(converted);
                    }
                }
#else
                foreach (var pendingFrame in frames)
                {
                    var frame = ConvertToFrameData(pendingFrame.Value, simulationState);
                    GetActiveConsumer()?.OnFrameGenerated(frame);
                }
#endif
            }

            if (flush)
            {
                Write(pendingFramesToWrite, this);
            }
            else
            {
                var req = Manager.Instance.CreateRequest<AsyncRequest<WritePendingCaptureRequestData>>();
                req.data = new WritePendingCaptureRequestData()
                {
                    PendingFrames = pendingFramesToWrite,
                    SimulationState = this
                };
                req.Enqueue(r =>
                {
                    Write(r.data.PendingFrames, r.data.SimulationState);
                    return AsyncRequest.Result.Completed;
                });
                req.Execute(AsyncRequest.ExecutionContext.JobSystem);
            }

            m_SerializeCapturesSampler.End();

            if (ReadyToShutdown)
            {
                var metadata = new CompletionMetadata()
                {
                    unityVersion = m_SimulationMetadata.unityVersion,
                    perceptionVersion = m_SimulationMetadata.perceptionVersion,
                    renderPipeline = m_SimulationMetadata.renderPipeline,
                    totalFrames = m_TotalFrames
                };

                consumerEndpoint.OnSimulationCompleted(metadata);
            }


            m_CaptureFileIndex++;
        }

        struct WritePendingCaptureRequestData
        {
            public List<KeyValuePair<PendingFrameId, PendingFrame>> PendingFrames;
            public SimulationState SimulationState;
        }
    }
#endif
}
