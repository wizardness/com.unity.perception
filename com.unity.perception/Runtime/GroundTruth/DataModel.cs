using System;
using System.Collections.Generic;
using UnityEngine.Perception.GroundTruth.Exporters.Solo;

namespace UnityEngine.Perception.GroundTruth.DataModel
{
    public interface IMessageProducer
    {
        void ToMessage(IMessageBuilder builder);
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

    [Serializable]
    public class SensorDefinition : IMessageProducer
    {
        public SensorDefinition(string id, string modality, string definition)
        {
            this.id = id;
            this.modality = modality;
            this.definition = definition;
            firstCaptureFrame = 0;
            captureTriggerMode = CaptureTriggerMode.Scheduled;
            simulationDeltaTime = 0.0f;
            framesBetweenCaptures = 0;
            manualSensorsAffectTiming = false;
        }

        public virtual bool IsValid()
        {
            return id != string.Empty && definition != string.Empty;
        }

        public string id;
        public string modality;
        public string definition;
        public float firstCaptureFrame;
        public CaptureTriggerMode captureTriggerMode;
        public float simulationDeltaTime;
        public int framesBetweenCaptures;
        public bool manualSensorsAffectTiming;

        public void ToMessage(IMessageBuilder builder)
        {
            builder.AddString("id", id);
            builder.AddString("modality", modality);
            builder.AddString("definition", definition);
            builder.AddFloat("first_capture_frame", firstCaptureFrame);
            builder.AddString("capture_trigger_mode", captureTriggerMode.ToString());
            builder.AddFloat("simulation_delta_time", simulationDeltaTime);
            builder.AddInt("frames_between_captures", framesBetweenCaptures);
            builder.AddBoolean("manual_sensors_affect_timing", manualSensorsAffectTiming);
        }
    }

    [Serializable]
    public abstract class AnnotationDefinition : IMessageProducer
    {
        public string id = string.Empty;
        public string description = string.Empty;
        public string annotationType = string.Empty;

        public AnnotationDefinition() { }

        public AnnotationDefinition(string id, string description, string annotationType)
        {
            this.id = id;
            this.description = description;
            this.annotationType = annotationType;
        }

        public virtual bool IsValid()
        {
            return id != string.Empty && description != string.Empty && annotationType != string.Empty;
        }

        public virtual void ToMessage(IMessageBuilder builder)
        {
            builder.AddString("id", id);
            builder.AddString("description", description);
            builder.AddString("annotation_type", annotationType);
        }
    }

    [Serializable]
    public class MetricDefinition : IMessageProducer
    {
        public string id = string.Empty;
        public string description = string.Empty;
        bool isRegistered { get; set; }= false;

        public MetricDefinition() { }

        public MetricDefinition(string id, string description)
        {
            this.id = id;
            this.description = description;
        }

        public virtual bool IsValid()
        {
            return id != string.Empty && description != string.Empty;
        }

        public virtual void ToMessage(IMessageBuilder builder)
        {
            builder.AddString("id", id);
            builder.AddString("description", description);
        }
    }

    /// <summary>
    /// The top level structure that holds all of the artifacts of a simulation
    /// frame. This is only reported after all of the captures, annotations, and
    /// metrics are ready to report for a single frame.
    /// </summary>
    [Serializable]
    public class Frame : IMessageProducer
    {
        public Frame(int frame, int sequence, int step)
        {
            this.frame = frame;
            this.sequence = sequence;
            this.step = step;
            sensors = new List<Sensor>();

        }

        /// <summary>
        /// The perception frame number of this record
        /// </summary>
        public int frame;
        /// <summary>
        /// The sequence that this record is a part of
        /// </summary>
        public int sequence;
        /// <summary>
        /// The step in the sequence that this record is a part of
        /// </summary>
        public int step;

        public float timestamp;

        /// <summary>
        /// A list of all of the sensor captures recorded for the frame.
        /// </summary>
        public IEnumerable<Sensor> sensors;


        public void ToMessage(IMessageBuilder builder)
        {
            builder.AddInt("frame", frame);
            builder.AddInt("sequence", sequence);
            builder.AddInt("step", step);
            foreach (var s in sensors)
            {
                var nested = builder.AddNestedMessageToVector("sensors");
                s.ToMessage(nested);
            }

        }
    }

    /// <summary>
    /// Abstract sensor class that holds all of the common information for a sensor.
    /// </summary>
    [Serializable]
    public abstract class Sensor : IMessageProducer
    {
        /// <summary>
        /// The unique, human readable ID for the sensor.
        /// </summary>
        public string Id;
        /// <summary>
        /// The type of the sensor.
        /// </summary>
        public string sensorType;

        public string description;

        /// <summary>
        /// The position (xyz) of the sensor in the world.
        /// </summary>
        public Vector3 position;
        /// <summary>
        /// The rotation in euler angles.
        /// </summary>
        public Vector3 rotation;
        /// <summary>
        /// The current velocity (xyz) of the sensor.
        /// </summary>
        public Vector3 velocity;
        /// <summary>
        /// The current acceleration (xyz) of the sensor.
        /// </summary>
        public Vector3 acceleration;

        // TODO put in camera intrinsic
        // TODO put in projection

        /// <summary>
        /// A list of all of the annotations recorded recorded for the frame.
        /// </summary>
        public IEnumerable<Annotation> annotations = new List<Annotation>();

        /// <summary>
        /// A list of all of the metrics recorded recorded for the frame.
        /// </summary>
        public IEnumerable<Metric> metrics = new List<Metric>();


        public virtual void ToMessage(IMessageBuilder builder)
        {
            builder.AddString("id", Id);
            builder.AddString("sensor_id", sensorType);
            builder.AddFloatVector("position", Utils.ToFloatVector(position));
            builder.AddFloatVector("rotation", Utils.ToFloatVector(rotation));
            builder.AddFloatVector("velocity", Utils.ToFloatVector(velocity));
            builder.AddFloatVector("acceleration", Utils.ToFloatVector(acceleration));

            foreach (var annotation in annotations)
            {
                var nested = builder.AddNestedMessageToVector("annotations");
                annotation.ToMessage(nested);
            }

            foreach (var metric in metrics)
            {
                var nested = builder.AddNestedMessageToVector("metrics");
                metric.ToMessage(nested);
            }
        }
    }

    /// <summary>
    /// The concrete class for an RGB sensor.
    /// </summary>
    [Serializable]
    public class RgbSensor : Sensor
    {
        // The format of the image type
        public string imageFormat;

        // The dimensions (width, height) of the image
        public Vector2 dimension;

        // The raw bytes of the image file
        public byte[] buffer;

        public override void ToMessage(IMessageBuilder builder)
        {
            base.ToMessage(builder);
            builder.AddString("image_format", imageFormat);
            builder.AddFloatVector("dimension", Utils.ToFloatVector(dimension));
            builder.AddPngImage("camera", buffer);
        }
    }

    /// <summary>
    /// Abstract class that holds the common data found in all
    /// annotations. Concrete instances of this class will add
    /// data for their specific annotation type.
    /// </summary>
    [Serializable]
    public abstract class Annotation : IMessageProducer
    {
        /// <summary>
        /// The unique, human readable ID for the annotation.
        /// </summary>
        public string Id;
        /// <summary>
        /// The sensor that this annotation is associated with.
        /// </summary>
        public string sensorId;
        /// <summary>
        /// The description of the annotation.
        /// </summary>
        public string description;
        /// <summary>
        /// The type of the annotation, this will map directly to one of the
        /// annotation subclasses that are concrete implementations of this abstract
        /// class.
        /// </summary>
        public string annotationType;

        public virtual void ToMessage(IMessageBuilder builder)
        {
            builder.AddString("id", Id);
            builder.AddString("sensor_id", sensorId);
            builder.AddString("description", description);
            builder.AddString("annotation_type", annotationType);
        }
    }

    /// <summary>
    /// Abstract class that holds the common data found in all
    /// metrics. Concrete instances of this class will add
    /// data for their specific metric type.
    /// </summary>
    [Serializable]
    public abstract class Metric : IMessageProducer
    {
        public string Id;
        /// <summary>
        /// The sensor ID that this metric is associated with
        /// </summary>
        public string sensorId;
        /// <summary>
        /// The annotation ID that this metric is associated with. If the value is none ("")
        /// then the metric is capture wide, and not associated with a specific annotation.
        /// </summary>
        public string annotationId;
        /// <summary>
        /// A human readable description of what this metric is for.
        /// </summary>
        public string description;
        /// <summary>
        /// Additional key/value pair metadata that can be associated with
        /// any metric.
        /// </summary>
        public Dictionary<string, object> metadata;
        public virtual void ToMessage(IMessageBuilder builder)
        {
            builder.AddString("id", Id);
            builder.AddString("sensor_id", sensorId);
            builder.AddString("annotation_id", annotationId);
            builder.AddString("description", description);

        }
    }

    /// <summary>
    /// Metadata describing the simulation.
    /// </summary>
    [Serializable]
    public class SimulationMetadata
    {
        public SimulationMetadata()
        {
            unityVersion = "not_set";
            perceptionVersion = "0.8.0-preview.4";
#if HDRP_PRESENT
            renderPipeline = "HDRP";
#elif URP_PRESENT
            renderPipeline = "URP";
#else
            renderPipeline = "built-in";
#endif
            metadata = new Dictionary<string, object>();
        }

        /// <summary>
        /// The version of the Unity editor executing the simulation.
        /// </summary>
        public string unityVersion;
        /// <summary>
        /// The version of the perception package used to generate the data.
        /// </summary>
        public string perceptionVersion;
        /// <summary>
        /// The render pipeline used to create the data. Currently either URP or HDRP.
        /// </summary>
        public string renderPipeline;
        /// <summary>
        /// Additional key/value pair metadata that can be associated with
        /// the simulation.
        /// </summary>
        public Dictionary<string, object> metadata;

        // We could probably list all of the randomizers here...
    }

    /// <summary>
    /// Metadata describing the final metrics of the simulation.
    /// </summary>
    [Serializable]
    public class CompletionMetadata : SimulationMetadata
    {
        public CompletionMetadata()
            : base() { }

        public struct Sequence
        {
            /// <summary>
            /// The ID of the sequence
            /// </summary>
            public int id;
            /// <summary>
            /// The number of steps in the sequence.
            /// </summary>
            public int numberOfSteps;
        }

        /// <summary>
        /// Total frames processed in the simulation. These frames are distributed
        /// over sequence and steps.
        /// </summary>
        public int totalFrames;
        /// <summary>
        /// A list of all of the sequences and the number of steps in the sequence for
        /// a simulation.
        /// </summary>
        public List<Sequence> sequences;
    }

    static class Utils
    {
        internal static int[] ToIntVector(Color32 c)
        {
            return new[] { (int)c.r, (int)c.g, (int)c.b, (int)c.a };
        }

        internal static float[] ToFloatVector(Vector2 v)
        {
            return new[] { v.x, v.y };
        }

        internal static float[] ToFloatVector(Vector3 v)
        {
            return new[] { v.x, v.y, v.z };
        }
    }
}
