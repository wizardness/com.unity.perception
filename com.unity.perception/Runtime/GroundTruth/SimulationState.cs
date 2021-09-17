using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.Profiling;

namespace UnityEngine.Perception.GroundTruth
{
    public partial class SimulationState
    {
        HashSet<SensorHandle> m_ActiveSensors = new HashSet<SensorHandle>();
        Dictionary<SensorHandle, SensorData> m_Sensors = new Dictionary<SensorHandle, SensorData>();

        int m_SequenceId = -1;

        HashSet<string> _Ids = new HashSet<string>();

        // Always use the property SequenceTimeMs instead
        int m_FrameCountLastUpdatedSequenceTime;
        float m_SequenceTimeDoNotUse;
        float m_UnscaledSequenceTimeDoNotUse;

        int m_FrameCountLastStepIncremented = -1;
        int m_Step = -1;

        bool m_HasStarted;
        int m_CaptureFileIndex;
        List<AdditionalInfoTypeData> m_AdditionalInfoTypeData = new List<AdditionalInfoTypeData>();

        Dictionary<SPendingFrameId, int> m_PendingIdToFrameMap = new Dictionary<SPendingFrameId, int>();
        Dictionary<SPendingFrameId, PendingFrame> m_PendingFrames = new Dictionary<SPendingFrameId, PendingFrame>();

        CustomSampler m_SerializeCapturesSampler = CustomSampler.Create("SerializeCaptures");
        CustomSampler m_SerializeCapturesAsyncSampler = CustomSampler.Create("SerializeCapturesAsync");
        CustomSampler m_JsonToStringSampler = CustomSampler.Create("JsonToString");
        CustomSampler m_WriteToDiskSampler = CustomSampler.Create("WriteJsonToDisk");
        CustomSampler m_SerializeMetricsSampler = CustomSampler.Create("SerializeMetrics");
        CustomSampler m_SerializeMetricsAsyncSampler = CustomSampler.Create("SerializeMetricsAsync");
        CustomSampler m_GetOrCreatePendingCaptureForThisFrameSampler = CustomSampler.Create("GetOrCreatePendingCaptureForThisFrame");
        float m_LastTimeScale;

        public bool IsValid(Guid guid) => true; // TODO obvs we need to do this for realz

        public bool IsRunning { get; private set; }

        //A sensor will be triggered if sequenceTime is within includeThreshold seconds of the next trigger
        const float k_SimulationTimingAccuracy = 0.01f;

        public SimulationState()
        {
            IsRunning = true;
        }

        static ConsumerEndpoint GetActiveConsumer()
        {
            return DatasetCapture.Instance.activeConsumer;
        }

        public interface IPendingId
        {
            SPendingFrameId AsFrameId();
            SPendingSensorId AsSensorId();

            bool IsValid();
        }

        public readonly struct SPendingFrameId : IPendingId, IEquatable<SPendingFrameId>, IEquatable<SPendingSensorId>, IEquatable<SPendingCaptureId>
        {
            public SPendingFrameId(int sequence, int step)
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

            public SPendingFrameId AsFrameId()
            {
                return this;
            }

            public SPendingSensorId AsSensorId()
            {
                return new SPendingSensorId(string.Empty,this);
            }

            public bool Equals(SPendingFrameId other)
            {
                return Sequence == other.Sequence && Step == other.Step;
            }

            public bool Equals(SPendingSensorId other)
            {
                var otherId = other.AsFrameId();
                return Sequence == otherId.Sequence && Step == otherId.Step;
            }

            public bool Equals(SPendingCaptureId other)
            {
                var otherId = other.AsFrameId();
                return Sequence == otherId.Sequence && Step == otherId.Step;
            }

            public override bool Equals(object obj)
            {
                return obj is SPendingFrameId other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Sequence * 397) ^ Step;
                }
            }
        }

        public readonly struct SPendingSensorId : IPendingId, IEquatable<SPendingSensorId>, IEquatable<SPendingFrameId>, IEquatable<SPendingCaptureId>
        {
            public SPendingSensorId(string sensorId, int sequence, int step)
            {
                SensorId = sensorId;
                m_FrameId = new SPendingFrameId(sequence, step);
            }

            public SPendingSensorId(string sensorId, SPendingFrameId frameId)
            {
                SensorId = sensorId;
                m_FrameId = frameId;
            }

            public bool IsValid()
            {
                return m_FrameId.IsValid() && !string.IsNullOrEmpty(SensorId);
            }

            public string SensorId { get; }

            readonly SPendingFrameId m_FrameId;
            public SPendingFrameId AsFrameId()
            {
                return m_FrameId;
            }

            public SPendingSensorId AsSensorId()
            {
                return this;
            }

            public bool Equals(SPendingSensorId other)
            {
                return SensorId == other.SensorId && m_FrameId.Equals(other.m_FrameId);
            }

            public bool Equals(SPendingFrameId other)
            {
                return m_FrameId.Equals(other);
            }

            public bool Equals(SPendingCaptureId other)
            {
                return Equals(other.SensorId);
            }

            public override bool Equals(object obj)
            {
                return obj is SPendingSensorId other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((SensorId != null ? SensorId.GetHashCode() : 0) * 397) ^ m_FrameId.GetHashCode();
                }
            }
        }

        public readonly struct SPendingCaptureId : IPendingId, IEquatable<SPendingCaptureId>, IEquatable<SPendingSensorId>, IEquatable<SPendingFrameId>
        {
            public SPendingCaptureId(string sensorId, string captureId, int sequence, int step)
            {
                CaptureId = captureId;
                SensorId = new SPendingSensorId(sensorId, sequence, step);
            }

            public SPendingCaptureId(string captureId, SPendingSensorId frameId)
            {
                CaptureId = captureId;
                SensorId = frameId;
            }

            public string CaptureId { get; }

            public SPendingSensorId SensorId { get; }

            public SPendingFrameId AsFrameId()
            {
                return SensorId.AsFrameId();
            }

            public SPendingSensorId AsSensorId()
            {
                return SensorId;
            }

            public bool IsValid()
            {
                return SensorId.IsValid() && !string.IsNullOrEmpty(CaptureId);
            }

            public bool Equals(SPendingCaptureId other)
            {
                return CaptureId == other.CaptureId && SensorId.Equals(other.SensorId);
            }

            public bool Equals(SPendingSensorId other)
            {
                return SensorId.Equals(other);
            }

            public bool Equals(SPendingFrameId other)
            {
                return SensorId.AsFrameId().Equals(other);
            }

            public override bool Equals(object obj)
            {
                return obj is SPendingCaptureId other && Equals(other);
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
            public PendingSensor(SPendingSensorId id)
            {
                m_Id = id;
                m_SensorData = null;
                Annotations = new Dictionary<SPendingCaptureId, Annotation>();
                Metrics = new Dictionary<SPendingCaptureId, Metric>();
            }

            public PendingSensor(SPendingSensorId id, Sensor sensorData) : this(id)
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

            SPendingSensorId m_Id;
            Sensor m_SensorData;
            public Dictionary<SPendingCaptureId, Annotation> Annotations { get; private set; }
            public Dictionary<SPendingCaptureId, Metric> Metrics { get; private set; }

            public bool IsPending<T>(IAsyncFuture<T> asyncFuture) where T : IPendingId
            {
                switch (asyncFuture.GetFutureType())
                {
                    case FutureType.Sensor:
                        return m_SensorData == null;
                    case FutureType.Annotation:
                    {
                        return asyncFuture.GetId() is SPendingCaptureId captureId && Annotations.ContainsKey(captureId) && Annotations[captureId] == null;
                    }
                    case FutureType.Metric:
                    {
                        return asyncFuture.GetId() is SPendingCaptureId captureId && Metrics.ContainsKey(captureId) && Metrics[captureId] == null;
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
                        if (result is Annotation annotation && asyncFuture.GetId() is SPendingCaptureId capId)
                        {
                            Annotations[capId] = annotation;
                            return true;
                        }

                        return false;
                    }
                    case FutureType.Metric:
                    {
                        if (result is Metric metric && asyncFuture.GetId() is SPendingCaptureId capId)
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
            public SPendingFrameId PendingId { get; }
            public float Timestamp { get; set; }
            internal Dictionary<SPendingSensorId, PendingSensor> sensors = new Dictionary<SPendingSensorId, PendingSensor>();
            public bool CaptureReported { get; set; } = false;
            public PendingFrame(SPendingFrameId pendingFrameId, float timestamp)
            {
                PendingId = pendingFrameId;
                Timestamp = timestamp;
            }

            public bool IsReadyToReport()
            {
                return sensors.All(sensor => sensor.Value.IsReadyToReport());
            }

            public PendingSensor GetOrCreatePendingSensor(SPendingSensorId sensorId)
            {
                return GetOrCreatePendingSensor(sensorId, out var _);
            }

            public PendingSensor GetOrCreatePendingSensor(SPendingSensorId sensorId, out bool created)
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
                return sensor.ReportAsyncResult(asyncFuture, result);
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
                if (!m_HasStarted)
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
                if (!m_HasStarted)
                    return 0;

                EnsureSequenceTimingsUpdated();
                return m_UnscaledSequenceTimeDoNotUse;
            }
        }

        void EnsureSequenceTimingsUpdated()
        {
            if (!m_HasStarted)
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
            m_Step = -1;
            foreach (var kvp in m_Sensors.ToArray())
            {
                var sensorData = kvp.Value;
                sensorData.sequenceTimeOfNextCapture = GetSequenceTimeOfNextCapture(sensorData);
                sensorData.sequenceTimeOfNextRender = 0;
                m_Sensors[kvp.Key] = sensorData;
            }

            m_SequenceId++;
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

            GetActiveConsumer()?.OnSensorRegistered(sensor);

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

        public void Update()
        {
            if (m_ActiveSensors.Count == 0)
                return;

            if (!m_HasStarted)
            {
                GetActiveConsumer()?.OnSimulationStarted(new SimulationMetadata());

                //simulation starts now
                m_FrameCountLastUpdatedSequenceTime = Time.frameCount;
                m_LastTimeScale = Time.timeScale;
                m_HasStarted = true;
            }

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
//            WritePendingMetrics();

            Time.captureDeltaTime = nextFrameDt;
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

        public void End()
        {
            if (_Ids.Count == 0)
                return;

            WritePendingCaptures(true, true);
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

            var metadata = new CompletionMetadata();
            GetActiveConsumer()?.OnSimulationCompleted(metadata);
        }

        public void RegisterAnnotationDefinition(AnnotationDefinition definition)
        {
            definition.id = RegisterId(definition.id);
            GetActiveConsumer()?.OnAnnotationRegistered(definition);
        }

        public void RegisterMetric(MetricDefinition definition)
        {
            definition.id = RegisterId(definition.id);
            GetActiveConsumer()?.OnMetricRegistered(definition);
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

        public SPendingSensorId ReportSensor(SensorDefinition definition, Sensor sensor)
        {
            var step = AcquireStep();
            var pendingSensorId = new SPendingSensorId(definition.id, m_SequenceId, step);
            var pendingFrame = GetOrCreatePendingFrame(pendingSensorId.AsFrameId());
            pendingFrame.sensors[pendingSensorId] = new PendingSensor(pendingSensorId, sensor);
            return pendingSensorId;
        }

        public SPendingCaptureId ReportAnnotation(SensorHandle sensorHandle, AnnotationDefinition definition, Annotation annotation)
        {
            var step = AcquireStep();
            var sensorId = new SPendingCaptureId(sensorHandle.Id, definition.id, m_SequenceId, step);
            var pendingFrame = GetOrCreatePendingFrame(sensorId.AsFrameId());

            var sensor = pendingFrame.GetOrCreatePendingSensor(sensorId.SensorId);

            var annotationId = new SPendingCaptureId(sensorHandle.Id, definition.id, m_SequenceId, step);
            sensor.Annotations[annotationId] = annotation;
            return annotationId;
        }

        PendingFrame GetOrCreatePendingFrame(SPendingFrameId pendingId)
        {
            return GetOrCreatePendingFrame(pendingId, out var _);
        }

        PendingFrame GetOrCreatePendingFrame(SPendingFrameId pendingId, out bool created)
        {
            created = false;
            m_GetOrCreatePendingCaptureForThisFrameSampler.Begin();
            EnsureStepIncremented();

            if (!m_PendingFrames.TryGetValue(pendingId, out var pendingFrame))
            {
                pendingFrame = new PendingFrame(pendingId, SequenceTime);
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

        public AsyncSensorFuture ReportSensorAsync(SensorDefinition sensorDefinition)
        {
            return new AsyncSensorFuture(ReportSensor(sensorDefinition, null), this);
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

        PendingFrame GetPendingFrame(SPendingFrameId id)
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

        public SPendingCaptureId ReportMetric(SensorHandle sensor, MetricDefinition definition, Metric metric, AnnotationHandle annotation)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(metric));

            var pendingId = new SPendingCaptureId(sensor.Id, definition.id, m_SequenceId, AcquireStep());
            var pendingFrame = GetOrCreatePendingFrame(pendingId.AsFrameId());

            if (pendingFrame == null)
                throw new InvalidOperationException($"Could not get or create a pending frame for {pendingId}");

            var pendingSensor = pendingFrame.GetOrCreatePendingSensor(pendingId.SensorId);

            pendingSensor.Metrics[pendingId] = metric;
            return pendingId;
        }
    }
}
