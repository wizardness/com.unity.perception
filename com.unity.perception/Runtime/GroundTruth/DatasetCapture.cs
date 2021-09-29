using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Simulation;
using UnityEngine;
using UnityEngine.Perception.GroundTruth.Consumers;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.Rendering;

#pragma warning disable 649
namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    /// Global manager for frame scheduling and output capture for simulations.
    /// Data capture follows the schema defined in *TODO: Expose schema publicly*
    /// </summary>
    public class DatasetCapture
    {
        static DatasetCapture s_Instance;

        SimulationState m_ActiveSimulation;
        List<SimulationState> m_ShuttingDownSimulations = new List<SimulationState>();

        public static DatasetCapture Instance => s_Instance ?? (s_Instance = new DatasetCapture());

        //SimulationState m_SimulationState = null;
        //List<SimulationState> m_ShuttingDownSimulationStates = new List<SimulationState>();

        Type m_ActiveConsumerType = typeof(SoloConsumer);

        public bool CanBeShutdown()
        {
            Debug.Log($"DC::CanBeShutdown, ready: {m_ReadyToShutdown}, active: {m_ActiveSimulation.IsNotRunning()}, shutting down #: {m_ShuttingDownSimulations.Count}, shutting down ready: {m_ShuttingDownSimulations.All(s => s.IsNotRunning())}");
            return m_ReadyToShutdown && m_ActiveSimulation.IsNotRunning() && m_ShuttingDownSimulations.All(s => s.IsNotRunning());
        }

        public class WaitUntilComplete : CustomYieldInstruction
        {
            public override bool keepWaiting => !Instance.CanBeShutdown();
        }

        class AllCapturesCompleteShutdownCondition : ICondition
        {
            public bool HasConditionBeenMet()
            {
                if (Instance.m_ReadyToShutdown)
                    Debug.Log("In here, but I doubt it");

                return Instance.CanBeShutdown();
            }
        }

        bool m_automaticShutdown = true;

        public bool automaticShutdown
        {
            get => m_automaticShutdown;
            set
            {
                switch (value)
                {
                    case true when !m_automaticShutdown:
                        Manager.Instance.ShutdownCondition = new AllCapturesCompleteShutdownCondition();
                        break;
                    case false when m_automaticShutdown:
                        Manager.Instance.ShutdownCondition = null;
                        break;
                }

                m_automaticShutdown = value;
            }

        }

        DatasetCapture()
        {
            Debug.Log("Dataset Capture NEW!!!");

            if (automaticShutdown)
                Manager.Instance.ShutdownCondition = new AllCapturesCompleteShutdownCondition();
            Manager.Instance.ShutdownNotification += OnApplicationShutdown;
        }

        internal SimulationState currentSimulation
        {
            get => m_ActiveSimulation ?? (m_ActiveSimulation = CreateSimulationData());
        }
#if false
        internal SimulationState simulationState
        {
            get
            {
                if (!m_Simulations.Any()) m_Simulations.Add(CreateSimulationData());
                return m_Simulations.Last();
            }
            private set => m_Simulations.Add(value);
        }
#endif
        /// <summary>
        /// The json metadata schema version the DatasetCapture's output conforms to.
        /// </summary>
        public static string SchemaVersion => "0.0.1";

        public static string PerceptionVersion => "0.8.0-preview.4";

        /// <summary>
        /// Called when the simulation ends. The simulation ends on playmode exit, application exit, or when <see cref="ResetSimulation"/> is called.
        /// </summary>
        public event Action SimulationEnding;

        public SensorHandle RegisterSensor(SensorDefinition sensor)
        {
            return currentSimulation.AddSensor(sensor, sensor.simulationDeltaTime);
        }

        public void RegisterMetric(MetricDefinition metricDefinition)
        {
            currentSimulation.RegisterMetric(metricDefinition);
        }

        public void RegisterAnnotationDefinition(AnnotationDefinition definition)
        {
            currentSimulation.RegisterAnnotationDefinition(definition);
        }

        /// <summary>
        /// Starts a new sequence in the capture.
        /// </summary>
        public void StartNewSequence() => currentSimulation.StartNewSequence();

        internal bool IsValid(string id) => currentSimulation.Contains(id);

        static ConsumerEndpoint s_Endpoint;
        static Type s_EndpointType = typeof(SoloConsumer);

        public static void SetEndpoint(ConsumerEndpoint endpoint)
        {
            // TODO I think that we need to do some checking to make sure we're not running

            s_Endpoint = endpoint;
            Instance.currentSimulation.consumerEndpoint = endpoint;
        }

        public static bool SetEndpointType(Type inType)
        {
            if (inType.IsSubclassOf(typeof(ConsumerEndpoint)))
            {
                Debug.Log("Setting endpoint type");
                s_EndpointType = inType;
                return true;
            }

            Debug.Log("Not setting endpoint type");
            return false;
        }

        static SimulationState CreateSimulationData()
        {
            if (s_Endpoint != null) return new SimulationState(s_Endpoint.GetType());
            if (s_EndpointType != null) return new SimulationState(s_EndpointType);
            throw new InvalidOperationException("Dataset capture cannot create a new simulation state without either a valid consumer endpoint or an endpoint type to instantiate.");
        }

        [RuntimeInitializeOnLoadMethod]
        void OnInitializeOnLoad()
        {
            Manager.Instance.ShutdownNotification += OnApplicationShutdown;
        }

        bool m_ReadyToShutdown = false;

        void OnApplicationShutdown()
        {
            ResetSimulation();
            m_ReadyToShutdown = true;
        }

        public void Update()
        {
            currentSimulation.Update();

            foreach (var simulation in m_ShuttingDownSimulations)
            {
                simulation.Update();
            }

            m_ShuttingDownSimulations.RemoveAll(sim => sim.ExecutionState == SimulationState.ExecutionStateType.Complete);
        }

        public void ResetSimulation()
        {
            SimulationEnding?.Invoke();

            if (m_ActiveSimulation.IsRunning())
            {
                var old = m_ActiveSimulation;
                m_ShuttingDownSimulations.Add(old);
                old.End();
                m_ReadyToShutdown = true;
            }

            m_ActiveSimulation = CreateSimulationData();
        }
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

        bool IsValid();

        bool IsPending();
    }

    public struct AsyncSensorFuture : IAsyncFuture<SimulationState.PendingSensorId>
    {
        public AsyncSensorFuture(SimulationState.PendingSensorId id, SimulationState simulationState)
        {
            m_Id = id;
            m_SimulationState = simulationState;
        }

        SimulationState.PendingSensorId m_Id;
        SimulationState m_SimulationState;

        public SimulationState.PendingSensorId GetId()
        {
            return m_Id;
        }

        public FutureType GetFutureType()
        {
            return FutureType.Sensor;
        }

        public bool IsValid()
        {
            return m_SimulationState.IsRunning();
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

    public struct AsyncAnnotationFuture : IAsyncFuture<SimulationState.PendingCaptureId>
    {
        public AsyncAnnotationFuture(SimulationState.PendingCaptureId id, SimulationState simulationState)
        {
            m_Id = id;
            m_SimulationState = simulationState;
        }

        SimulationState.PendingCaptureId m_Id;
        SimulationState m_SimulationState;

        public SimulationState.PendingCaptureId GetId()
        {
            return m_Id;
        }

        public FutureType GetFutureType()
        {
            return FutureType.Annotation;
        }

        public bool IsValid()
        {
            return m_SimulationState.IsRunning();
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

    public struct AsyncMetricFuture : IAsyncFuture<SimulationState.PendingCaptureId>
    {
        public AsyncMetricFuture(SimulationState.PendingCaptureId id, SimulationState simulationState)
        {
            m_Id = id;
            m_SimulationState = simulationState;
        }

        SimulationState.PendingCaptureId m_Id;
        SimulationState m_SimulationState;

        public SimulationState.PendingCaptureId GetId()
        {
            return m_Id;
        }

        public FutureType GetFutureType()
        {
            return FutureType.Metric;
        }

        public bool IsValid()
        {
            return m_SimulationState.IsRunning();
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

        public override string ToString()
        {
            return Id;
        }

        /// <summary>
        /// Whether the sensor is currently enabled. When disabled, the DatasetCapture will no longer schedule frames for running captures on this sensor.
        /// </summary>
        public bool Enabled
        {
            get => DatasetCapture.Instance.currentSimulation.IsEnabled(this);
            set
            {
                CheckValid();
                DatasetCapture.Instance.currentSimulation.SetEnabled(this, value);
            }
        }

        public void ReportAnnotation(AnnotationDefinition definition, Annotation annotation)
        {
            if (!ShouldCaptureThisFrame)
                throw new InvalidOperationException("Annotation reported on SensorHandle in frame when its ShouldCaptureThisFrame is false.");
            if (!definition.IsValid())
                throw new ArgumentException("The given annotationDefinition is invalid", nameof(definition));

            DatasetCapture.Instance.currentSimulation.ReportAnnotation(this, definition, annotation);
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

            return DatasetCapture.Instance.currentSimulation.ReportAnnotationAsync(annotationDefinition, this);
        }

        public AsyncSensorFuture ReportSensorAsync()
        {
            if (!ShouldCaptureThisFrame)
                throw new InvalidOperationException("Annotation reported on SensorHandle in frame when its ShouldCaptureThisFrame is false.");
            if (!IsValid)
                throw new ArgumentException($"The given annotationDefinition is invalid {Id}");

            return DatasetCapture.Instance.currentSimulation.ReportSensorAsync(this);
        }

        public void ReportSensor(Sensor sensor)
        {
            if (!ShouldCaptureThisFrame)
                throw new InvalidOperationException("Annotation reported on SensorHandle in frame when its ShouldCaptureThisFrame is false.");
            if (!IsValid)
                throw new ArgumentException("The given annotationDefinition is invalid", Id);

           DatasetCapture.Instance.currentSimulation.ReportSensor(this, sensor);
        }

        /// <summary>
        /// Whether the sensor should capture this frame. Sensors are expected to call this method each frame to determine whether
        /// they should capture during the frame. Captures should only be reported when this is true.
        /// </summary>
        public bool ShouldCaptureThisFrame => DatasetCapture.Instance.currentSimulation.ShouldCaptureThisFrame(this);

        /// <summary>
        /// Requests a capture from this sensor on the next rendered frame. Can only be used with manual capture mode (<see cref="CaptureTriggerMode.Manual"/>).
        /// </summary>
        public void RequestCapture()
        {
            DatasetCapture.Instance.currentSimulation.SetNextCaptureTimeToNowForSensor(this);
        }

        public void ReportMetric(MetricDefinition definition, Metric metric)
        {
            if (!ShouldCaptureThisFrame)
                throw new InvalidOperationException("Annotation reported on SensorHandle in frame when its ShouldCaptureThisFrame is false.");
            if (!IsValid)
                throw new ArgumentException("The given annotationDefinition is invalid", Id);

            DatasetCapture.Instance.currentSimulation.ReportMetric(this, definition, metric);
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

            return DatasetCapture.Instance.currentSimulation.CreateAsyncMetric(metricDefinition, this);
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
