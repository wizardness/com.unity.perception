using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.Simulation;
using UnityEngine;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace UnityEngine.Perception.GroundTruth
{
    public class SimulationState
    {
        public static int TimeOutFrameCount = 60;

        public enum ExecutionStateType
        {
            NotStarted,
            Starting,
            Running,
            ShuttingDown,
            Complete
        }

        internal bool IsRunning()
        {
            return !IsNotRunning();
        }

        internal bool IsNotRunning()
        {
            return ExecutionState == ExecutionStateType.NotStarted || ExecutionState == ExecutionStateType.Complete;
        }

        internal ExecutionStateType ExecutionState { get; private set; }

        HashSet<SensorHandle> m_ActiveSensors = new HashSet<SensorHandle>();
        Dictionary<SensorHandle, SensorData> m_Sensors = new Dictionary<SensorHandle, SensorData>();

        internal ConsumerEndpoint consumerEndpoint { get; set; }

        int m_SequenceId = 0;

        HashSet<string> _Ids = new HashSet<string>();

        // Always use the property SequenceTimeMs instead
        int m_FrameCountLastUpdatedSequenceTime;
        float m_SequenceTimeDoNotUse;
        float m_UnscaledSequenceTimeDoNotUse;

        int m_FrameCountLastStepIncremented = -1;
        int m_TotalFrames = 0;
        int m_Step = -1;

        List<AdditionalInfoTypeData> m_AdditionalInfoTypeData = new List<AdditionalInfoTypeData>();

        Dictionary<PendingFrameId, int> m_PendingIdToFrameMap = new Dictionary<PendingFrameId, int>();
        //Dictionary<PendingFrameId, PendingFrame> m_PendingFrames = new Dictionary<PendingFrameId, PendingFrame>();
        SortedDictionary<PendingFrameId, PendingFrame> m_PendingFrames = new SortedDictionary<PendingFrameId, PendingFrame>();

        CustomSampler m_SerializeCapturesSampler = CustomSampler.Create("SerializeCaptures");
        CustomSampler m_SerializeCapturesAsyncSampler = CustomSampler.Create("SerializeCapturesAsync");
        CustomSampler m_JsonToStringSampler = CustomSampler.Create("JsonToString");
        CustomSampler m_WriteToDiskSampler = CustomSampler.Create("WriteJsonToDisk");
        CustomSampler m_SerializeMetricsSampler = CustomSampler.Create("SerializeMetrics");
        CustomSampler m_SerializeMetricsAsyncSampler = CustomSampler.Create("SerializeMetricsAsync");
        CustomSampler m_GetOrCreatePendingCaptureForThisFrameSampler = CustomSampler.Create("GetOrCreatePendingCaptureForThisFrame");
        float m_LastTimeScale;

        //A sensor will be triggered if sequenceTime is within includeThreshold seconds of the next trigger
        const float k_SimulationTimingAccuracy = 0.01f;

        public SimulationState(Type endpointType)
        {
            ExecutionState = ExecutionStateType.NotStarted;

            m_SimulationMetadata = new SimulationMetadata()
            {
                unityVersion = Application.unityVersion,
                perceptionVersion = DatasetCapture.PerceptionVersion,
            };

            if (!endpointType.IsSubclassOf(typeof(ConsumerEndpoint)))
                throw new InvalidOperationException($"Cannot add non-endpoint type {endpointType.Name} to consumer endpoint list");
            consumerEndpoint = (ConsumerEndpoint)Activator.CreateInstance(endpointType);
        }

        bool readyToShutdown => !m_PendingFrames.Any();

        public interface IPendingId
        {
            PendingFrameId AsFrameId();
            PendingSensorId AsSensorId();
        }

        public readonly struct PendingFrameId : IPendingId, IEquatable<PendingFrameId>, IEquatable<PendingSensorId>, IEquatable<PendingCaptureId>, IComparable<PendingFrameId>
        {
            public PendingFrameId(int sequence, int step)
            {
                Sequence = sequence;
                Step = step;
            }

            public bool IsValid()
            {
                return Sequence >= 0 && Step >= 0;
            }

            public int Sequence { get; }
            public int Step { get; }

            public PendingFrameId AsFrameId()
            {
                return this;
            }

            public PendingSensorId AsSensorId()
            {
                return new PendingSensorId(string.Empty,this);
            }

            public bool Equals(PendingFrameId other)
            {
                return Sequence == other.Sequence && Step == other.Step;
            }

            public bool Equals(PendingSensorId other)
            {
                var otherId = other.AsFrameId();
                return Sequence == otherId.Sequence && Step == otherId.Step;
            }

            public bool Equals(PendingCaptureId other)
            {
                var otherId = other.AsFrameId();
                return Sequence == otherId.Sequence && Step == otherId.Step;
            }

            public int CompareTo(PendingFrameId other)
            {
                if (Sequence == other.Sequence) return Step - other.Step;
                return Sequence - other.Sequence;
            }

            public override bool Equals(object obj)
            {
                return obj is PendingFrameId other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Sequence * 397) ^ Step;
                }
            }
        }

        public readonly struct PendingSensorId : IPendingId, IEquatable<PendingSensorId>, IEquatable<PendingFrameId>, IEquatable<PendingCaptureId>
        {
            public PendingSensorId(string sensorId, int sequence, int step)
            {
                SensorId = sensorId;
                m_FrameId = new PendingFrameId(sequence, step);
            }

            public PendingSensorId(string sensorId, PendingFrameId frameId)
            {
                SensorId = sensorId;
                m_FrameId = frameId;
            }

            public bool IsValid()
            {
                return m_FrameId.IsValid() && !string.IsNullOrEmpty(SensorId);
            }

            public string SensorId { get; }

            readonly PendingFrameId m_FrameId;
            public PendingFrameId AsFrameId()
            {
                return m_FrameId;
            }

            public PendingSensorId AsSensorId()
            {
                return this;
            }

            public bool Equals(PendingSensorId other)
            {
                return SensorId == other.SensorId && m_FrameId.Equals(other.m_FrameId);
            }

            public bool Equals(PendingFrameId other)
            {
                return m_FrameId.Equals(other);
            }

            public bool Equals(PendingCaptureId other)
            {
                return Equals(other.SensorId);
            }

            public override bool Equals(object obj)
            {
                return obj is PendingSensorId other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((SensorId != null ? SensorId.GetHashCode() : 0) * 397) ^ m_FrameId.GetHashCode();
                }
            }
        }

        public readonly struct PendingCaptureId : IPendingId, IEquatable<PendingCaptureId>, IEquatable<PendingSensorId>, IEquatable<PendingFrameId>
        {
            public PendingCaptureId(string sensorId, string captureId, int sequence, int step)
            {
                CaptureId = captureId;
                SensorId = new PendingSensorId(sensorId, sequence, step);
            }

            public PendingCaptureId(string captureId, PendingSensorId frameId)
            {
                CaptureId = captureId;
                SensorId = frameId;
            }

            public string CaptureId { get; }

            public PendingSensorId SensorId { get; }

            public PendingFrameId AsFrameId()
            {
                return SensorId.AsFrameId();
            }

            public PendingSensorId AsSensorId()
            {
                return SensorId;
            }

            public bool IsValid()
            {
                return SensorId.IsValid() && !string.IsNullOrEmpty(CaptureId);
            }

            public bool Equals(PendingCaptureId other)
            {
                return CaptureId == other.CaptureId && SensorId.Equals(other.SensorId);
            }

            public bool Equals(PendingSensorId other)
            {
                return SensorId.Equals(other);
            }

            public bool Equals(PendingFrameId other)
            {
                return SensorId.AsFrameId().Equals(other);
            }

            public override bool Equals(object obj)
            {
                return obj is PendingCaptureId other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((CaptureId != null ? CaptureId.GetHashCode() : 0) * 397) ^ SensorId.GetHashCode();
                }
            }


        }

        public class PendingSensor
        {
            public PendingSensor(PendingSensorId id)
            {
                m_Id = id;
                m_SensorData = null;
                Annotations = new Dictionary<PendingCaptureId, Annotation>();
                Metrics = new Dictionary<PendingCaptureId, Metric>();
            }

            public PendingSensor(PendingSensorId id, Sensor sensorData) : this(id)
            {
                m_SensorData = sensorData;
            }

            public Sensor ToSensor()
            {
                if (!IsReadyToReport()) return null;
                m_SensorData.annotations = Annotations.Select(kvp => kvp.Value);
                m_SensorData.metrics = Metrics.Select(kvp => kvp.Value);
                return m_SensorData;
            }

            PendingSensorId m_Id;
            Sensor m_SensorData;
            public Dictionary<PendingCaptureId, Annotation> Annotations { get; private set; }
            public Dictionary<PendingCaptureId, Metric> Metrics { get; private set; }

            public bool IsPending<T>(IAsyncFuture<T> asyncFuture) where T : IPendingId
            {
                switch (asyncFuture.GetFutureType())
                {
                    case FutureType.Sensor:
                        return m_SensorData == null;
                    case FutureType.Annotation:
                    {
                        return asyncFuture.GetId() is PendingCaptureId captureId && Annotations.ContainsKey(captureId) && Annotations[captureId] == null;
                    }
                    case FutureType.Metric:
                    {
                        return asyncFuture.GetId() is PendingCaptureId captureId && Metrics.ContainsKey(captureId) && Metrics[captureId] == null;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            public bool ReportAsyncResult<T>(IAsyncFuture<T> asyncFuture, object result) where T : IPendingId
            {
                switch (asyncFuture.GetFutureType())
                {
                    case FutureType.Sensor:
                        if (result is Sensor sensor)
                        {
                            m_SensorData = sensor;
                            return true;
                        }
                        return false;
                    case FutureType.Annotation:
                    {
                        if (result is Annotation annotation && asyncFuture.GetId() is PendingCaptureId capId)
                        {
                            Annotations[capId] = annotation;
                            return true;
                        }

                        return false;
                    }
                    case FutureType.Metric:
                    {
                        if (result is Metric metric && asyncFuture.GetId() is PendingCaptureId capId)
                        {
                            Metrics[capId] = metric;
                            return true;
                        }

                        return false;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            public bool IsReadyToReport()
            {
                return
                    m_SensorData != null &&
                    Metrics.All(i => i.Value != null) &&
                    Annotations.All(i => i.Value != null);
            }
        }

        public class PendingFrame
        {
            SimulationState m_SimulationState;

            public PendingFrameId PendingId { get; }
            public float Timestamp { get; set; }
            internal Dictionary<PendingSensorId, PendingSensor> sensors = new Dictionary<PendingSensorId, PendingSensor>();
            public bool CaptureReported { get; set; } = false;
            public PendingFrame(PendingFrameId pendingFrameId, float timestamp, SimulationState simState)
            {
                PendingId = pendingFrameId;
                Timestamp = timestamp;
                m_SimulationState = simState;
            }

            public bool IsReadyToReport()
            {
                return sensors.All(sensor => sensor.Value.IsReadyToReport());
            }

            public PendingSensor GetOrCreatePendingSensor(PendingSensorId sensorId)
            {
                return GetOrCreatePendingSensor(sensorId, out var _);
            }

            public PendingSensor GetOrCreatePendingSensor(PendingSensorId sensorId, out bool created)
            {
                created = false;

                if (!sensors.TryGetValue(sensorId, out var pendingSensor))
                {
                    pendingSensor = new PendingSensor(sensorId);
                    sensors[sensorId] = pendingSensor;
                    created = true;
                }

                return pendingSensor;
            }

            public bool IsPending<T>(IAsyncFuture<T> asyncFuture) where T : IPendingId
            {
                var sensorId = asyncFuture.GetId().AsSensorId();
                if (!sensorId.IsValid()) return false;

                return
                    sensors.TryGetValue(sensorId, out var pendingSensor) &&
                    pendingSensor.IsPending(asyncFuture);
            }

            public bool ReportAsyncResult<T>(IAsyncFuture<T> asyncFuture, object result) where T : IPendingId
            {
                var sensorId = asyncFuture.GetId().AsSensorId();
                if (!sensorId.IsValid()) return false;

                var sensor = GetOrCreatePendingSensor(sensorId);
                sensor.ReportAsyncResult(asyncFuture, result);

                return true;
            }
        }

        public struct SensorData
        {
            public string modality;
            public string description;
            public float firstCaptureTime;
            public CaptureTriggerMode captureTriggerMode;
            public float renderingDeltaTime;
            public int framesBetweenCaptures;
            public bool manualSensorAffectSimulationTiming;

            public float sequenceTimeOfNextCapture;
            public float sequenceTimeOfNextRender;
            public int lastCaptureFrameCount;
        }

        enum AdditionalInfoKind
        {
            Metric,
            Annotation
        }

        struct AdditionalInfoTypeData : IEquatable<AdditionalInfoTypeData>
        {
            public string name;
            public string description;
            public string format;
            public Guid id;
            public Array specValues;
            public AdditionalInfoKind additionalInfoKind;

            public override string ToString()
            {
                return $"{nameof(name)}: {name}, {nameof(description)}: {description}, {nameof(format)}: {format}, {nameof(id)}: {id}";
            }

            public bool Equals(AdditionalInfoTypeData other)
            {
                var areMembersEqual = additionalInfoKind == other.additionalInfoKind &&
                    string.Equals(name, other.name, StringComparison.InvariantCulture) &&
                    string.Equals(description, other.description, StringComparison.InvariantCulture) &&
                    string.Equals(format, other.format, StringComparison.InvariantCulture) &&
                    id.Equals(other.id);

                if (!areMembersEqual)
                    return false;

                if (specValues == other.specValues)
                    return true;
                if (specValues == null || other.specValues == null)
                    return false;
                if (specValues.Length != other.specValues.Length)
                    return false;

                for (var i = 0; i < specValues.Length; i++)
                {
                    if (!specValues.GetValue(i).Equals(other.specValues.GetValue(i)))
                        return false;
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                return obj is AdditionalInfoTypeData other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    // ReSharper disable NonReadonlyMemberInGetHashCode
                    var hashCode = (name != null ? StringComparer.InvariantCulture.GetHashCode(name) : 0);
                    hashCode = (hashCode * 397) ^ (description != null ? StringComparer.InvariantCulture.GetHashCode(description) : 0);
                    hashCode = (hashCode * 397) ^ (format != null ? StringComparer.InvariantCulture.GetHashCode(format) : 0);
                    hashCode = (hashCode * 397) ^ id.GetHashCode();
                    return hashCode;
                }
            }
        }
#if false
        internal void ReportCapture(SensorHandle sensorHandle, string filename, SensorSpatialData sensorSpatialData, params(string, object)[] additionalSensorValues)
        {
            var sensorData = m_Sensors[sensorHandle];
            var pendingCapture = GetOrCreatePendingCaptureForThisFrame(sensorHandle, out _);

            if (pendingCapture.CaptureReported)
                throw new InvalidOperationException($"Capture for frame {Time.frameCount} already reported for sensor {this}");

            pendingCapture.CaptureReported = true;
            pendingCapture.AdditionalSensorValues = additionalSensorValues;
            pendingCapture.SensorSpatialData = sensorSpatialData;

            sensorData.lastCaptureFrameCount = Time.frameCount;
            m_Sensors[sensorHandle] = sensorData;

            // SB - maybe this can all be moved to the other capture area
            var width = -1;
            var height = -1;
            var fullPath = filename;
            var frameCount = 0;
            var buffer = new byte[0];

            foreach (var i in additionalSensorValues)
            {
                switch (i.Item1)
                {
                    case "camera_width":
                        width = (int)i.Item2;
                        break;
                    case "camera_height":
                        height = (int)i.Item2;
                        break;
                    case "full_path":
                        fullPath = (string)i.Item2;
                        break;
                    case "frame":
                        frameCount = (int)i.Item2;
                        break;
                    case "":
                        buffer = (byte[])i.Item2;
                        break;
                }
            }

            var trans = pendingCapture.SensorSpatialData.EgoPose.position;
            var rot = pendingCapture.SensorSpatialData.EgoPose.rotation;
            var velocity = pendingCapture.SensorSpatialData.EgoVelocity ?? Vector3.zero;
            var accel = pendingCapture.SensorSpatialData.EgoAcceleration ?? Vector3.zero;
       }
#endif
        /// <summary>
        /// Use this to get the current step when it is desirable to ensure the step has been allocated for this frame. Steps should only be allocated in frames where a capture or metric is reported.
        /// </summary>
        /// <returns>The current step</returns>
        int AcquireStep()
        {
            EnsureStepIncremented();
            EnsureSequenceTimingsUpdated();
            return m_Step;
        }

        // ReSharper restore InconsistentNaming

        /// <summary>
        /// The simulation time that has elapsed since the beginning of the sequence.
        /// </summary>
        public float SequenceTime
        {
            get
            {
                //TODO: Can this be replaced with Time.time - sequenceTimeStart?
                if (ExecutionState != ExecutionStateType.Running)
                    return 0;

                EnsureSequenceTimingsUpdated();

                return m_SequenceTimeDoNotUse;
            }
        }

        /// <summary>
        /// The unscaled simulation time that has elapsed since the beginning of the sequence. This is the time that should be used for scheduling sensors
        /// </summary>
        public float UnscaledSequenceTime
        {
            get
            {
                //TODO: Can this be replaced with Time.time - sequenceTimeStart?
                if (ExecutionState != ExecutionStateType.Running)
                    return 0;

                EnsureSequenceTimingsUpdated();
                return m_UnscaledSequenceTimeDoNotUse;
            }
        }

        void EnsureSequenceTimingsUpdated()
        {
            if (ExecutionState != ExecutionStateType.Running)
            {
                ResetTimings();
            }
            else if (m_FrameCountLastUpdatedSequenceTime != Time.frameCount)
            {
                m_SequenceTimeDoNotUse += Time.deltaTime;
                if (Time.timeScale > 0)
                    m_UnscaledSequenceTimeDoNotUse += Time.deltaTime / Time.timeScale;

                CheckTimeScale();

                m_FrameCountLastUpdatedSequenceTime = Time.frameCount;
            }
        }

        void CheckTimeScale()
        {
            if (m_LastTimeScale != Time.timeScale)
                Debug.LogError($"Time.timeScale may not change mid-sequence. This can cause sensors to get out of sync and corrupt the data. Previous: {m_LastTimeScale} Current: {Time.timeScale}");

            m_LastTimeScale = Time.timeScale;
        }

        void EnsureStepIncremented()
        {
            if (m_FrameCountLastStepIncremented != Time.frameCount)
            {
                m_FrameCountLastStepIncremented = Time.frameCount;
                m_Step++;
            }
        }

        public void StartNewSequence()
        {
            ResetTimings();
            m_FrameCountLastStepIncremented = -1;
            m_SequenceId++;
            m_Step = -1;
            foreach (var kvp in m_Sensors.ToArray())
            {
                var sensorData = kvp.Value;
                sensorData.sequenceTimeOfNextCapture = GetSequenceTimeOfNextCapture(sensorData);
                sensorData.sequenceTimeOfNextRender = 0;
                m_Sensors[kvp.Key] = sensorData;
            }
        }

        void ResetTimings()
        {
            m_FrameCountLastUpdatedSequenceTime = Time.frameCount;
            m_SequenceTimeDoNotUse = 0;
            m_UnscaledSequenceTimeDoNotUse = 0;
            m_LastTimeScale = Time.timeScale;
        }

        string RegisterId(string requestedId)
        {
            var id = requestedId;
            var i = 0;
            while (_Ids.Contains(id))
            {
                id = $"{requestedId}_{i++}";
            }

            _Ids.Add(id);
            return id;
        }

        public SensorHandle AddSensor(SensorDefinition sensor, float renderingDeltaTime)
        {
            var sensorData = new SensorData()
            {
                modality = sensor.modality,
                description = sensor.definition,
                firstCaptureTime = UnscaledSequenceTime + sensor.firstCaptureFrame * renderingDeltaTime,
                captureTriggerMode = CaptureTriggerMode.Scheduled, // TODO fix this
                renderingDeltaTime = renderingDeltaTime,
                framesBetweenCaptures = sensor.framesBetweenCaptures,
                manualSensorAffectSimulationTiming = sensor.manualSensorsAffectTiming,
                lastCaptureFrameCount = -1
            };
            sensorData.sequenceTimeOfNextCapture = GetSequenceTimeOfNextCapture(sensorData);
            sensorData.sequenceTimeOfNextRender = UnscaledSequenceTime;

            sensor.id = RegisterId(sensor.id);
            var sensorHandle = new SensorHandle(sensor.id);

            m_ActiveSensors.Add(sensorHandle);
            m_Sensors.Add(sensorHandle, sensorData);

            consumerEndpoint.OnSensorRegistered(sensor);

            if (ExecutionState == ExecutionStateType.NotStarted)
            {
                ExecutionState = ExecutionStateType.Starting;
            }

            return sensorHandle;
        }

#if false
        public void AddSensor(EgoHandle egoHandle, string modality, string description, float firstCaptureFrame, CaptureTriggerMode captureTriggerMode, float renderingDeltaTime, int framesBetweenCaptures, bool manualSensorAffectSimulationTiming, SensorHandle sensor)
        {
            var sensorData = new SensorData()
            {
                modality = modality,
                description = description,
                firstCaptureTime = UnscaledSequenceTime + firstCaptureFrame * renderingDeltaTime,
                captureTriggerMode = captureTriggerMode,
                renderingDeltaTime = renderingDeltaTime,
                framesBetweenCaptures = framesBetweenCaptures,
                manualSensorAffectSimulationTiming = manualSensorAffectSimulationTiming,
                egoHandle = egoHandle,
                lastCaptureFrameCount = -1
            };
            sensorData.sequenceTimeOfNextCapture = GetSequenceTimeOfNextCapture(sensorData);
            sensorData.sequenceTimeOfNextRender = UnscaledSequenceTime;
            m_ActiveSensors.Add(sensor);
            m_Sensors.Add(sensor, sensorData);
            m_Ids.Add(sensor.Id);

            GetActiveConsumer()?.OnSensorRegistered(new SensorDefinition("camera", modality, description));
        }
#endif
        float GetSequenceTimeOfNextCapture(SensorData sensorData)
        {
            // If the first capture hasn't happened yet, sequenceTimeNextCapture field won't be valid
            if (sensorData.firstCaptureTime >= UnscaledSequenceTime)
            {
                return sensorData.captureTriggerMode == CaptureTriggerMode.Scheduled? sensorData.firstCaptureTime : float.MaxValue;
            }

            return sensorData.sequenceTimeOfNextCapture;
        }

        public bool Contains(string id) => _Ids.Contains(id);

        public bool IsEnabled(SensorHandle sensorHandle) => m_ActiveSensors.Contains(sensorHandle);

        public void SetEnabled(SensorHandle sensorHandle, bool value)
        {
            if (!value)
                m_ActiveSensors.Remove(sensorHandle);
            else
                m_ActiveSensors.Add(sensorHandle);
        }

        static void CheckDatasetAllowed()
        {
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException("Dataset generation is only supported in play mode.");
            }
        }

        SimulationMetadata m_SimulationMetadata;
#if false
        internal void TryToClearOut()
        {
            if (ReadyToShutdown) return;

            WritePendingCaptures(true, true);
        }
#endif
        public void Update()
        {
            // If there aren't any sensors then we are currently stateless?
            if (ExecutionState == ExecutionStateType.NotStarted)
            {
                return;
            }

            if (ExecutionState == ExecutionStateType.Starting)
            {
                UpdateStarting();
            }

            if (ExecutionState == ExecutionStateType.Running)
            {
                UpdateRunning();
            }

            if (ExecutionState == ExecutionStateType.ShuttingDown)
            {
                UpdateShuttdingDown();
            }

            if (ExecutionState == ExecutionStateType.NotStarted)
            {
                UpdateComplete();
            }
        }

        void UpdateStarting()
        {
            m_SimulationMetadata = new SimulationMetadata()
            {
                unityVersion = Application.unityVersion,
                perceptionVersion = DatasetCapture.PerceptionVersion,
            };

            consumerEndpoint.OnSimulationStarted(m_SimulationMetadata);

            //simulation starts now
            m_FrameCountLastUpdatedSequenceTime = Time.frameCount;
            m_LastTimeScale = Time.timeScale;

            ExecutionState = ExecutionStateType.Running;
        }

        void UpdateRunning()
        {
            EnsureSequenceTimingsUpdated();

            //update the active sensors sequenceTimeNextCapture and lastCaptureFrameCount
            foreach (var activeSensor in m_ActiveSensors)
            {
                var sensorData = m_Sensors[activeSensor];

#if UNITY_EDITOR
                if (UnityEditor.EditorApplication.isPaused)
                {
                    //When the user clicks the 'step' button in the editor, frames will always progress at .02 seconds per step.
                    //In this case, just run all sensors each frame to allow for debugging
                    Debug.Log($"Frame step forced all sensors to synchronize, changing frame timings.");

                    sensorData.sequenceTimeOfNextRender = UnscaledSequenceTime;
                    sensorData.sequenceTimeOfNextCapture = UnscaledSequenceTime;
                }
#endif

                if (Mathf.Abs(sensorData.sequenceTimeOfNextRender - UnscaledSequenceTime) < k_SimulationTimingAccuracy)
                {
                    //means this frame fulfills this sensor's simulation time requirements, we can move target to next frame.
                    sensorData.sequenceTimeOfNextRender += sensorData.renderingDeltaTime;
                }

                if (activeSensor.ShouldCaptureThisFrame)
                {
                    if (sensorData.captureTriggerMode.Equals(CaptureTriggerMode.Scheduled))
                    {
                        sensorData.sequenceTimeOfNextCapture += sensorData.renderingDeltaTime * (sensorData.framesBetweenCaptures + 1);
                        Debug.Assert(sensorData.sequenceTimeOfNextCapture > UnscaledSequenceTime,
                            $"Next scheduled capture should be after {UnscaledSequenceTime} but is {sensorData.sequenceTimeOfNextCapture}");
                        while (sensorData.sequenceTimeOfNextCapture <= UnscaledSequenceTime)
                            sensorData.sequenceTimeOfNextCapture += sensorData.renderingDeltaTime * (sensorData.framesBetweenCaptures + 1);
                    }
                    else if (sensorData.captureTriggerMode.Equals(CaptureTriggerMode.Manual))
                    {
                        sensorData.sequenceTimeOfNextCapture = float.MaxValue;
                    }

                    sensorData.lastCaptureFrameCount = Time.frameCount;
                }

                m_Sensors[activeSensor] = sensorData;
            }

            //find the deltatime required to land on the next active sensor that needs simulation
            var nextFrameDt = float.PositiveInfinity;
            foreach (var activeSensor in m_ActiveSensors)
            {
                float thisSensorNextFrameDt = -1;

                var sensorData = m_Sensors[activeSensor];
                if (sensorData.captureTriggerMode.Equals(CaptureTriggerMode.Scheduled))
                {
                    thisSensorNextFrameDt = sensorData.sequenceTimeOfNextRender - UnscaledSequenceTime;

                    Debug.Assert(thisSensorNextFrameDt > 0f, "Sensor was scheduled to capture in the past but got skipped over.");
                }
                else if (sensorData.captureTriggerMode.Equals(CaptureTriggerMode.Manual) && sensorData.manualSensorAffectSimulationTiming)
                {
                    thisSensorNextFrameDt = sensorData.sequenceTimeOfNextRender - UnscaledSequenceTime;
                }

                if (thisSensorNextFrameDt > 0f && thisSensorNextFrameDt < nextFrameDt)
                {
                    nextFrameDt = thisSensorNextFrameDt;
                }
            }

            if (float.IsPositiveInfinity(nextFrameDt))
            {
                //means no sensor is controlling simulation timing, so we set Time.captureDeltaTime to 0 (default) which means the setting does not do anything
                nextFrameDt = 0;
            }

            WritePendingCaptures();

            Time.captureDeltaTime = nextFrameDt;
        }

        void UpdateShuttdingDown()
        {
            if (_Ids.Count == 0)
            {
                ExecutionState = ExecutionStateType.NotStarted;
                return;
            }

            WritePendingCaptures(true, true);
#if false
            if (m_AdditionalInfoTypeData.Any())
            {
                List<IdLabelConfig.LabelEntrySpec> labels = new List<IdLabelConfig.LabelEntrySpec>();

                foreach (var infoTypeData in m_AdditionalInfoTypeData)
                {
                    if (infoTypeData.specValues == null) continue;

                    foreach (var spec in infoTypeData.specValues)
                    {
                        if (spec is IdLabelConfig.LabelEntrySpec entrySpec)
                        {
                            labels.Add(entrySpec);
                        }
                    }

//                    Debug.Log($"adt: {infoTypeData}");

                }
            }

//            WriteReferences();
#endif
            Time.captureDeltaTime = 0;

            OnComplete();
        }

        void OnComplete()
        {
            if (_Ids.Count == 0)
                return;

            if (readyToShutdown)
            {
                var metadata = new CompletionMetadata()
                {
                    unityVersion = m_SimulationMetadata.unityVersion,
                    perceptionVersion = m_SimulationMetadata.perceptionVersion,
                    renderPipeline = m_SimulationMetadata.renderPipeline,
                    totalFrames = m_TotalFrames
                };

                consumerEndpoint.OnSimulationCompleted(metadata);

                ExecutionState = ExecutionStateType.NotStarted;

                VerifyNoMorePendingFrames();
            }
        }

        void UpdateComplete()
        {
            VerifyNoMorePendingFrames();
        }

        void VerifyNoMorePendingFrames()
        {
            if (m_PendingFrames.Count > 0)
                Debug.LogError($"Simulation ended with pending {m_PendingFrames.Count} annotations (final id): ({m_PendingFrames.Last().Key.Sequence},{m_PendingFrames.Last().Key.Step})");
        }

        public void SetNextCaptureTimeToNowForSensor(SensorHandle sensorHandle)
        {
            if (!m_Sensors.ContainsKey(sensorHandle))
                return;

            var data = m_Sensors[sensorHandle];
            data.sequenceTimeOfNextCapture = UnscaledSequenceTime;
            m_Sensors[sensorHandle] = data;
        }

        public bool ShouldCaptureThisFrame(SensorHandle sensorHandle)
        {
            if (!m_Sensors.ContainsKey(sensorHandle))
                return false;

            var data = m_Sensors[sensorHandle];
            if (data.lastCaptureFrameCount == Time.frameCount)
                return true;

            return data.sequenceTimeOfNextCapture - UnscaledSequenceTime < k_SimulationTimingAccuracy;
        }
#if false
        public IEnumerator End()
        {
            if (_Ids.Count == 0)
                yield break;

            while (m_PendingFrames.Count > 0)
            {
                WritePendingCaptures(true, true);
                yield return null;
            }

            if (m_PendingFrames.Count > 0)
                Debug.LogError($"Simulation ended with pending annotations: {string.Join(", ", m_PendingFrames.Select(c => $"id:{c.Key}"))}");

#if false
            WritePendingMetrics(true);
            if (m_PendingMetrics.Count > 0)
                Debug.LogError($"Simulation ended with pending metrics: {string.Join(", ", m_PendingMetrics.Select(c => $"id:{c.MetricId} step:{c.Step}"))}");
#endif
            if (m_AdditionalInfoTypeData.Any())
            {
                List<IdLabelConfig.LabelEntrySpec> labels = new List<IdLabelConfig.LabelEntrySpec>();

                foreach (var infoTypeData in m_AdditionalInfoTypeData)
                {
                    if (infoTypeData.specValues == null) continue;

                    foreach (var spec in infoTypeData.specValues)
                    {
                        if (spec is IdLabelConfig.LabelEntrySpec entrySpec)
                        {
                            labels.Add(entrySpec);
                        }
                    }

//                    Debug.Log($"adt: {infoTypeData}");

                }
            }

//            WriteReferences();
            Time.captureDeltaTime = 0;
            IsRunning = false;

            var metadata = new CompletionMetadata()
            {
                unityVersion = m_SimulationMetadata.unityVersion,
                perceptionVersion = m_SimulationMetadata.perceptionVersion,
                renderPipeline = m_SimulationMetadata.renderPipeline,
                totalFrames = m_TotalFrames
            };

            consumerEndpoint.OnSimulationCompleted(metadata);
        }
#else
        internal bool CapturesLeft()
        {
            return m_PendingFrames.Count > 0;
        }

        public void End()
        {
// This is just here for debug reasons, this here will always report errors, but they get cleaned up in shutdown
//            VerifyNoMorePendingFrames();


            ExecutionState = ExecutionStateType.ShuttingDown;
        }
#endif
        public void RegisterAnnotationDefinition(AnnotationDefinition definition)
        {
            definition.id = RegisterId(definition.id);
            consumerEndpoint.OnAnnotationRegistered(definition);
        }

        public void RegisterMetric(MetricDefinition definition)
        {
            definition.id = RegisterId(definition.id);
            consumerEndpoint.OnMetricRegistered(definition);
        }

        void RegisterAdditionalInfoType<TSpec>(string name, TSpec[] specValues, string description, string format, Guid id, AdditionalInfoKind additionalInfoKind)
        {
            CheckDatasetAllowed();
            var annotationDefinitionInfo = new AdditionalInfoTypeData()
            {
                additionalInfoKind = additionalInfoKind,
                name = name,
                description = description,
                format = format,
                id = id,
                specValues = specValues
            };
#if false
            if (!m_Ids.Add(id.ToString()))
            {
                foreach (var existingAnnotationDefinition in m_AdditionalInfoTypeData)
                {
                    if (existingAnnotationDefinition.id == id)
                    {
                        if (existingAnnotationDefinition.Equals(annotationDefinitionInfo))
                        {
                            return;
                        }

                        throw new ArgumentException($"{id} has already been registered to an AnnotationDefinition or MetricDefinition with different information.\nExisting: {existingAnnotationDefinition}");
                    }
                }

                throw new ArgumentException($"Id {id} is already in use. Ids must be unique.");
            }
#endif
            m_AdditionalInfoTypeData.Add(annotationDefinitionInfo);
        }

        public PendingSensorId ReportSensor(SensorHandle handle, Sensor sensor)
        {
            var step = AcquireStep();
            var pendingSensorId = new PendingSensorId(handle.Id, m_SequenceId, step);
            var pendingFrame = GetOrCreatePendingFrame(pendingSensorId.AsFrameId());
            pendingFrame.sensors[pendingSensorId] = new PendingSensor(pendingSensorId, sensor);
            return pendingSensorId;
        }

        public PendingCaptureId ReportAnnotation(SensorHandle sensorHandle, AnnotationDefinition definition, Annotation annotation)
        {
            var step = AcquireStep();
            var sensorId = new PendingCaptureId(sensorHandle.Id, definition.id, m_SequenceId, step);
            var pendingFrame = GetOrCreatePendingFrame(sensorId.AsFrameId());

            var sensor = pendingFrame.GetOrCreatePendingSensor(sensorId.SensorId);

            var annotationId = new PendingCaptureId(sensorHandle.Id, definition.id, m_SequenceId, step);
            sensor.Annotations[annotationId] = annotation;
            return annotationId;
        }

        PendingFrame GetOrCreatePendingFrame(PendingFrameId pendingId)
        {
            return GetOrCreatePendingFrame(pendingId, out var _);
        }

        PendingFrame GetOrCreatePendingFrame(PendingFrameId pendingId, out bool created)
        {
            created = false;
            m_GetOrCreatePendingCaptureForThisFrameSampler.Begin();
            EnsureStepIncremented();

            if (!m_PendingFrames.TryGetValue(pendingId, out var pendingFrame))
            {
                pendingFrame = new PendingFrame(pendingId, SequenceTime, this);
                m_PendingFrames[pendingId] = pendingFrame;
                m_PendingIdToFrameMap[pendingId] = Time.frameCount;
                created = true;
            }

            m_GetOrCreatePendingCaptureForThisFrameSampler.End();
            return pendingFrame;
        }

        public AsyncAnnotationFuture ReportAnnotationAsync(AnnotationDefinition annotationDefinition, SensorHandle sensorHandle)
        {
            return new AsyncAnnotationFuture(ReportAnnotation(sensorHandle, annotationDefinition, null), this);
        }

        public AsyncSensorFuture ReportSensorAsync(SensorHandle handle)
        {
            return new AsyncSensorFuture(ReportSensor(handle, null), this);
        }

        public bool IsPending<T>(IAsyncFuture<T> asyncFuture) where T : IPendingId
        {
            return
                m_PendingFrames.TryGetValue(asyncFuture.GetId().AsFrameId(), out var pendingFrame) &&
                pendingFrame.IsPending(asyncFuture);
        }

        PendingFrame GetPendingFrame<T>(IAsyncFuture<T> future) where T : IPendingId
        {
            return GetPendingFrame(future.GetId().AsFrameId());
        }

        PendingFrame GetPendingFrame(PendingFrameId id)
        {
            return m_PendingFrames[id];
        }

        bool ReportAsyncResultGeneric<T>(IAsyncFuture<T> asyncFuture, object result) where T : IPendingId
        {
            if (!asyncFuture.IsPending()) return false;

            var pendingFrame = GetPendingFrame(asyncFuture);

            if (pendingFrame == null) return false;

            return pendingFrame.ReportAsyncResult(asyncFuture, result);
        }

        public bool ReportAsyncResult(AsyncSensorFuture asyncFuture, Sensor sensor)
        {
            return ReportAsyncResultGeneric(asyncFuture, sensor);
        }

        public bool ReportAsyncResult(AsyncAnnotationFuture asyncFuture, Annotation annotation)
        {
            return ReportAsyncResultGeneric(asyncFuture, annotation);
        }

        public bool ReportAsyncResult(AsyncMetricFuture asyncFuture, Metric metric)
        {
            return ReportAsyncResultGeneric(asyncFuture, metric);
        }

        public AsyncMetricFuture CreateAsyncMetric(MetricDefinition metricDefinition, SensorHandle sensorHandle = default, AnnotationHandle annotationHandle = default)
        {
            EnsureStepIncremented();
            var pendingId = ReportMetric(sensorHandle, metricDefinition, null, annotationHandle);
            return new AsyncMetricFuture(pendingId, this);
        }

        public PendingCaptureId ReportMetric(SensorHandle sensor, MetricDefinition definition, Metric metric)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(metric));

            var pendingId = new PendingCaptureId(sensor.Id, definition.id, m_SequenceId, AcquireStep());
            var pendingFrame = GetOrCreatePendingFrame(pendingId.AsFrameId());

            if (pendingFrame == null)
                throw new InvalidOperationException($"Could not get or create a pending frame for {pendingId}");

            var pendingSensor = pendingFrame.GetOrCreatePendingSensor(pendingId.SensorId);

            pendingSensor.Metrics[pendingId] = metric;
            return pendingId;
        }

        public PendingCaptureId ReportMetric(SensorHandle sensor, MetricDefinition definition, Metric metric, AnnotationHandle annotation)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(metric));

            var pendingId = new PendingCaptureId(sensor.Id, definition.id, m_SequenceId, AcquireStep());
            var pendingFrame = GetOrCreatePendingFrame(pendingId.AsFrameId());

            if (pendingFrame == null)
                throw new InvalidOperationException($"Could not get or create a pending frame for {pendingId}");

            var pendingSensor = pendingFrame.GetOrCreatePendingSensor(pendingId.SensorId);

            pendingSensor.Metrics[pendingId] = metric;
            return pendingId;
        }

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
                imageFormat = RgbSensor.ImageFormat.PNG,
                dimension = Vector2.zero,
                buffer = null
            };

            return sensor;
        }

        // TODO rename this to 'ReportPendingFrames'
        void WritePendingCaptures(bool flush = false, bool writeCapturesFromThisFrame = false)
        {
            m_SerializeCapturesSampler.Begin();

            // TODO do not new these each frame
            var pendingFramesToWrite = new Queue<KeyValuePair<PendingFrameId,PendingFrame>>(m_PendingFrames.Count);
            var timedOutFrames = new List<KeyValuePair<PendingFrameId, PendingFrame>>(m_PendingFrames.Count);

            var currentFrame = Time.frameCount;

            // Write out each frame until we reach one that is not ready to write yet, this is in order to
            // assure that all reports happen in sequential order
            foreach (var frame in m_PendingFrames)
            {
                var recordedFrame = m_PendingIdToFrameMap[frame.Value.PendingId];

                if ((writeCapturesFromThisFrame || recordedFrame < currentFrame) &&
                    frame.Value.IsReadyToReport())
                {
                    pendingFramesToWrite.Enqueue(frame);
                }
                else if (currentFrame > recordedFrame + TimeOutFrameCount)
                {
                    timedOutFrames.Add(frame);
                }
                else
                {
                    break;
                }
            }

            foreach (var pf in pendingFramesToWrite)
            {
                m_PendingFrames.Remove(pf.Key);
            }

            foreach (var pf in timedOutFrames)
            {
                Debug.LogError($"Frame {pf.Key} timed out");
                m_PendingFrames.Remove(pf.Key);
            }

            IEnumerable<Sensor> ConvertToSensors(PendingFrame frame, SimulationState simulationState)
            {
                return frame.sensors.Values.Where(s => s.IsReadyToReport()).Select(s => s.ToSensor());
            }

            Frame ConvertToFrameData(PendingFrame pendingFrame, SimulationState simState)
            {
                var frameId = m_PendingIdToFrameMap[pendingFrame.PendingId];
                var frame = new Frame(frameId, pendingFrame.PendingId.Sequence, pendingFrame.PendingId.Step, pendingFrame.Timestamp);

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

            void Write(Queue<KeyValuePair<PendingFrameId, PendingFrame>> frames, SimulationState simulationState)
            {
#if true
                // TODO this needs to be done properly, we need to wait on all of the frames to come back so we
                // can report them, right now we are just going to jam up this thread waiting for them, also could
                // result in an endless loop if the frame never comes back
                while (frames.Any())
                {
                    var converted = ConvertToFrameData(frames.Dequeue().Value, simulationState);
                    m_TotalFrames++;

                    if (converted == null)
                    {
                        Debug.LogError("Could not convert frame data");
                    }

                    if (consumerEndpoint == null)
                    {
                        Debug.LogError("Consumer endpoint is null");
                    }

                    consumerEndpoint.OnFrameGenerated(converted);
                }
#else
                foreach (var pendingFrame in frames)
                {
                    var frame = ConvertToFrameData(pendingFrame.Value, simulationState);
                    GetActiveConsumer()?.OnFrameGenerated(frame);
                }
#endif
            }
#if false
            if (flush)
            {
#endif
                Write(pendingFramesToWrite, this);
#if false
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
#endif
            m_SerializeCapturesSampler.End();
        }
#if false
        struct WritePendingCaptureRequestData
        {
            public Queue<KeyValuePair<PendingFrameId, PendingFrame>> PendingFrames;
            public SimulationState SimulationState;
        }
#endif
    }
}
