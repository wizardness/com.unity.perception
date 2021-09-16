using System;
using Unity.Simulation;
using UnityEngine;
using UnityEngine.Perception.GroundTruth.DataModel;

#pragma warning disable 649
namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    /// Global manager for frame scheduling and output capture for simulations.
    /// Data capture follows the schema defined in *TODO: Expose schema publicly*
    /// </summary>
    public class DatasetCapture : MonoBehaviour
    {
        public static DatasetCapture Instance { get; protected set; }
        public ConsumerEndpoint activeConsumer;
        SimulationState m_SimulationState;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                Debug.LogError($"The simulation started with more than one instance of DatasetCapture, destroying this one");
            }
            else
            {
                Instance = this;
            }
        }

        internal SimulationState simulationState
        {
            get { return m_SimulationState ?? (m_SimulationState = CreateSimulationData()); }
            private set => m_SimulationState = value;
        }

        /// <summary>
        /// The json metadata schema version the DatasetCapture's output conforms to.
        /// </summary>
        public static string SchemaVersion => "0.0.1";

        /// <summary>
        /// Called when the simulation ends. The simulation ends on playmode exit, application exit, or when <see cref="ResetSimulation"/> is called.
        /// </summary>
        public  event Action SimulationEnding;

        public SensorHandle RegisterSensor(SensorDefinition sensor)
        {
            return simulationState.AddSensor(sensor, sensor.simulationDeltaTime);
        }

        public void RegisterMetric(MetricDefinition metricDefinition)
        {
            simulationState.RegisterMetric(metricDefinition);
        }

        public void RegisterAnnotationDefinition(AnnotationDefinition definition)
        {
            simulationState.RegisterAnnotationDefinition(definition);
        }

        /// <summary>
        /// Starts a new sequence in the capture.
        /// </summary>
        public  void StartNewSequence() => simulationState.StartNewSequence();

        internal bool IsValid(string id) => simulationState.Contains(id);

        SimulationState CreateSimulationData()
        {
            return new SimulationState();
        }

        [RuntimeInitializeOnLoadMethod]
         void OnInitializeOnLoad()
        {
            Manager.Instance.ShutdownNotification += ResetSimulation;
        }

        /// <summary>
        /// Stop the current simulation and start a new one. All pending data is written to disk before returning.
        /// </summary>
        public  void ResetSimulation()
        {
            //this order ensures that exceptions thrown by End() do not prevent the state from being reset
            SimulationEnding?.Invoke();
            var oldSimulationState = simulationState;
            simulationState = CreateSimulationData();
            oldSimulationState.End();
        }
    }

    /// <summary>
    /// Capture trigger modes for sensors.
    /// </summary>
    public enum CaptureTriggerMode
    {
        /// <summary>
        /// Captures happen automatically based on a start frame and frame delta time.
        /// </summary>
        Scheduled,
        /// <summary>
        /// Captures should be triggered manually through calling the manual capture method of the sensor using this trigger mode.
        /// </summary>
        Manual
    }

    public enum FutureType
    {
        Sensor,
        Annotation,
        Metric
    }

    public interface IAsyncFuture<T> where T : SimulationState.IPendingId
    {
        T GetId();

        FutureType GetFutureType();
        bool IsPending();
    }

    public struct AsyncSensorFuture : IAsyncFuture<SimulationState.SPendingSensorId>
    {
        public AsyncSensorFuture(SimulationState.SPendingSensorId id, SimulationState simulationState)
        {
            m_Id = id;
            m_SimulationState = simulationState;
        }

        SimulationState.SPendingSensorId m_Id;
        SimulationState m_SimulationState;

        public SimulationState.SPendingSensorId GetId()
        {
            return m_Id;
        }

        public FutureType GetFutureType()
        {
            return FutureType.Sensor;
        }

        public bool IsPending()
        {
            return m_SimulationState.IsPending(this);
        }

        public void Report(Sensor sensor)
        {
            m_SimulationState.ReportAsyncResult(this, sensor);
        }
    }

    public struct AsyncAnnotationFuture : IAsyncFuture<SimulationState.SPendingCaptureId>
    {
        public AsyncAnnotationFuture(SimulationState.SPendingCaptureId id, SimulationState simulationState)
        {
            m_Id = id;
            m_SimulationState = simulationState;
        }

        SimulationState.SPendingCaptureId m_Id;
        SimulationState m_SimulationState;

        public SimulationState.SPendingCaptureId GetId()
        {
            return m_Id;
        }

        public FutureType GetFutureType()
        {
            return FutureType.Annotation;
        }

        public bool IsPending()
        {
            return m_SimulationState.IsPending(this);
        }

        public void Report(Annotation annotation)
        {
            m_SimulationState.ReportAsyncResult(this, annotation);
        }
    }

    public struct AsyncMetricFuture : IAsyncFuture<SimulationState.SPendingCaptureId>
    {
        public AsyncMetricFuture(SimulationState.SPendingCaptureId id, SimulationState simulationState)
        {
            m_Id = id;
            m_SimulationState = simulationState;
        }

        SimulationState.SPendingCaptureId m_Id;
        SimulationState m_SimulationState;

        public SimulationState.SPendingCaptureId GetId()
        {
            return m_Id;
        }

        public FutureType GetFutureType()
        {
            return FutureType.Metric;
        }

        public bool IsPending()
        {
            return m_SimulationState.IsPending(this);
        }

        public void Report(Metric metric)
        {
            m_SimulationState.ReportAsyncResult(this, metric);
        }
    }

    /// <summary>
    /// A handle to a sensor managed by the <see cref="DatasetCapture"/>. It can be used to check whether the sensor
    /// is expected to capture this frame and report captures, annotations, and metrics regarding the sensor.
    /// </summary>
    public struct SensorHandle : IDisposable, IEquatable<SensorHandle>
    {
        public string Id { get; internal set; }

        internal SensorHandle(string id)
        {
            Id = id ?? string.Empty;
        }

        /// <summary>
        /// Whether the sensor is currently enabled. When disabled, the DatasetCapture will no longer schedule frames for running captures on this sensor.
        /// </summary>
        public bool Enabled
        {
            get => DatasetCapture.Instance.simulationState.IsEnabled(this);
            set
            {
                CheckValid();
                DatasetCapture.Instance.simulationState.SetEnabled(this, value);
            }
        }

        public void ReportAnnotation(AnnotationDefinition definition, Annotation annotation)
        {
            if (!ShouldCaptureThisFrame)
                throw new InvalidOperationException("Annotation reported on SensorHandle in frame when its ShouldCaptureThisFrame is false.");
            if (!definition.IsValid())
                throw new ArgumentException("The given annotationDefinition is invalid", nameof(definition));

            DatasetCapture.Instance.simulationState.ReportAnnotation(this, definition, annotation);
        }

        /// <summary>
        /// Creates an async annotation for reporting the values for an annotation during a future frame.
        /// </summary>
        /// <param name="annotationDefinition">The AnnotationDefinition of this annotation.</param>
        /// <returns>Returns a handle to the <see cref="AsyncAnnotation"/>, which can be used to report annotation data during a subsequent frame.</returns>
        /// <exception cref="InvalidOperationException">Thrown if this method is called during a frame where <see cref="ShouldCaptureThisFrame"/> is false.</exception>
        /// <exception cref="ArgumentException">Thrown if the given AnnotationDefinition is invalid.</exception>
        public AsyncAnnotationFuture ReportAnnotationAsync(AnnotationDefinition annotationDefinition)
        {
            if (!ShouldCaptureThisFrame)
                throw new InvalidOperationException("Annotation reported on SensorHandle in frame when its ShouldCaptureThisFrame is false.");
            if (!annotationDefinition.IsValid())
                throw new ArgumentException("The given annotationDefinition is invalid", nameof(annotationDefinition));

            return DatasetCapture.Instance.simulationState.ReportAnnotationAsync(annotationDefinition, this);
        }

        public AsyncSensorFuture ReportSensorAsync(SensorDefinition sensorDefinition)
        {
            if (!ShouldCaptureThisFrame)
                throw new InvalidOperationException("Annotation reported on SensorHandle in frame when its ShouldCaptureThisFrame is false.");
            if (!sensorDefinition.IsValid())
                throw new ArgumentException("The given annotationDefinition is invalid", nameof(sensorDefinition));

            return DatasetCapture.Instance.simulationState.ReportSensorAsync(sensorDefinition);
        }

        public void ReportSensor(SensorDefinition definition, Sensor sensor)
        {
            if (!ShouldCaptureThisFrame)
                throw new InvalidOperationException("Annotation reported on SensorHandle in frame when its ShouldCaptureThisFrame is false.");
            if (!definition.IsValid())
                throw new ArgumentException("The given annotationDefinition is invalid", nameof(definition));

           DatasetCapture.Instance.simulationState.ReportSensor(definition, sensor);
        }

        /// <summary>
        /// Whether the sensor should capture this frame. Sensors are expected to call this method each frame to determine whether
        /// they should capture during the frame. Captures should only be reported when this is true.
        /// </summary>
        public bool ShouldCaptureThisFrame => DatasetCapture.Instance.simulationState.ShouldCaptureThisFrame(this);

        /// <summary>
        /// Requests a capture from this sensor on the next rendered frame. Can only be used with manual capture mode (<see cref="CaptureTriggerMode.Manual"/>).
        /// </summary>
        public void RequestCapture()
        {
            DatasetCapture.Instance.simulationState.SetNextCaptureTimeToNowForSensor(this);
        }
#if false
        public MetricHandle ReportMetric(MetricDefinition definition, Metric metric)
        {
            if (metric == null)
                throw new ArgumentNullException(nameof(metric));

            if (!ShouldCaptureThisFrame)
                throw new InvalidOperationException($"Sensor-based metrics may only be reported when SensorHandle.ShouldCaptureThisFrame is true");

            return DatasetCapture.Instance.simulationState.ReportMetric(this, definition, metric, default);
        }
#endif
        /// <summary>
        /// Start an async metric for reporting metric values for this frame in a subsequent frame.
        /// </summary>
        /// <param name="metricDefinition">The <see cref="MetricDefinition"/> of the metric</param>
        /// <exception cref="InvalidOperationException">Thrown if <see cref="ShouldCaptureThisFrame"/> is false</exception>
        /// <returns>An <see cref="AsyncMetric"/> which should be used to report the metric values, potentially in a later frame</returns>
        public AsyncMetricFuture ReportMetricAsync(MetricDefinition metricDefinition)
        {
            if (!ShouldCaptureThisFrame)
                throw new InvalidOperationException($"Sensor-based metrics may only be reported when SensorHandle.ShouldCaptureThisFrame is true");
            if (!metricDefinition.IsValid())
                throw new ArgumentException("The passed in metric definition is invalid", nameof(metricDefinition));

            return DatasetCapture.Instance.simulationState.CreateAsyncMetric(metricDefinition, this);
        }

        /// <summary>
        /// Dispose this SensorHandle.
        /// </summary>
        public void Dispose()
        {
            this.Enabled = false;
        }

        /// <summary>
        /// Returns whether this SensorHandle is valid in the current simulation. Nil SensorHandles are never valid.
        /// </summary>
        public bool IsValid => DatasetCapture.Instance.IsValid(this.Id);

        /// <summary>
        /// Returns true if this SensorHandle was default-instantiated.
        /// </summary>
        public bool IsNil => this == default;

        void CheckValid()
        {
            if (!DatasetCapture.Instance.IsValid(this.Id))
                throw new InvalidOperationException("SensorHandle has been disposed or its simulation has ended");
        }

        /// <inheritdoc/>
        public bool Equals(SensorHandle other)
        {
            switch (Id)
            {
                case null when other.Id == null:
                    return true;
                case null:
                    return false;
                default:
                    return Id.Equals(other.Id);
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is SensorHandle other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        /// <summary>
        /// Compares two <see cref="SensorHandle"/> instances for equality.
        /// </summary>
        /// <param name="left">The first SensorHandle.</param>
        /// <param name="right">The second SensorHandle.</param>
        /// <returns>Returns true if the two SensorHandles refer to the same sensor.</returns>
        public static bool operator==(SensorHandle left, SensorHandle right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two <see cref="SensorHandle"/> instances for inequality.
        /// </summary>
        /// <param name="left">The first SensorHandle.</param>
        /// <param name="right">The second SensorHandle.</param>
        /// <returns>Returns false if the two SensorHandles refer to the same sensor.</returns>
        public static bool operator!=(SensorHandle left, SensorHandle right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// A handle to an annotation. Can be used to report metrics on the annotation.
    /// </summary>
    public readonly struct AnnotationHandle : IEquatable<AnnotationHandle>
    {
        readonly AnnotationDefinition m_Definition;

        /// <summary>
        /// The ID of the annotation which will be used in the json metadata.
        /// </summary>
        public string Id => m_Definition.id;

        /// <summary>
        /// The SensorHandle on which the annotation was reported
        /// </summary>
        public readonly SensorHandle SensorHandle;

        internal AnnotationHandle(SensorHandle sensorHandle, AnnotationDefinition definition, int sequence, int step)
        {
            m_Definition = definition;
            SensorHandle = sensorHandle;
        }

        /// <summary>
        /// Returns true if the annotation is nil (created using default instantiation).
        /// </summary>
        public bool IsNil => Id == string.Empty;

        /// <inheritdoc/>
        public bool Equals(AnnotationHandle other)
        {
            return SensorHandle.Equals(other.SensorHandle) && m_Definition.Equals(other.m_Definition);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is AnnotationHandle other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = (Id != null ? StringComparer.InvariantCulture.GetHashCode(Id) : 0);
            return hash;
        }
    }
}
