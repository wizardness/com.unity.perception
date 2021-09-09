using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine.Perception.GroundTruth.Exporters.Solo;

namespace UnityEngine.Perception.GroundTruth.SoloDesign
{
    internal static class Utils
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

    /// <summary>
    /// Data generated from a perception simulation will be pushed to an active
    /// consumer.
    /// </summary>
    public abstract class PerceptionConsumer : MonoBehaviour
    {
        /// <summary>
        /// Called when the simulation begins. Provides simulation wide metadata to
        /// the consumer.
        /// </summary>
        /// <param name="metadata">Metadata describing the active simulation</param>
        public abstract void OnSimulationStarted(SimulationMetadata metadata);

        public virtual void OnSensorRegistered(SensorDefinition sensor) { }

        public virtual void OnAnnotationRegistered(AnnotationDefinition annotationDefinition)
        {

        }
        public virtual void OnMetricRegistered(MetricDefinition metricDefinition) { }

        /// <summary>
        /// Called at the end of each frame. Contains all of the generated data for the
        /// frame. This method is called after the frame has entirely finished processing.
        /// </summary>
        /// <param name="frame">The frame data.</param>
        public abstract void OnFrameGenerated(Frame frame);

        /// <summary>
        /// Called at the end of the simulation. Contains metadata describing the entire
        /// simulation process.
        /// </summary>
        /// <param name="metadata">Metadata describing the entire simulation process</param>
        public abstract void OnSimulationCompleted(CompletionMetadata metadata);
    }

    /// <summary>
    /// Metadata describing the simulation.
    /// </summary>
    [Serializable]
    public class SimulationMetadata
    {
        public SimulationMetadata()
        {
            unityVersion = "figure out how to do unity version";
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

    public interface IMessageProducer
    {
        void ToMessage(IMessageBuilder builder);
    }

    [Serializable]
    public class SensorDefinition : IMessageProducer
    {
        public SensorDefinition(string id, string modality, string definition)
        {
            this.id = id;
            this.modality = modality;
            this.definition = definition;
            this.firstCaptureFrame = 0;
            this.captureTriggerMode = string.Empty;
            this.simulationDeltaTime = 0.0f;
            this.framesBetweenCaptures = 0;
            this.manualSensorsAffectTiming = false;
        }

        public string id;
        public string modality;
        public string definition;
        public float firstCaptureFrame;
        public string captureTriggerMode;
        public float simulationDeltaTime;
        public int framesBetweenCaptures;
        public bool manualSensorsAffectTiming;

        public void ToMessage(IMessageBuilder builder)
        {
            builder.AddString("id", id);
            builder.AddString("modality", modality);
            builder.AddString("definition", definition);
            builder.AddFloat("first_capture_frame", firstCaptureFrame);
            builder.AddString("capture_trigger_mode", captureTriggerMode);
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
    public class BoundingBoxAnnotationDefinition : AnnotationDefinition
    {
        static readonly string k_Id = "bounding box";
        static readonly string k_Description = "Bounding box for each labeled object visible to the sensor";
        static readonly string k_AnnotationType = "bounding box";

        public BoundingBoxAnnotationDefinition() : base(k_Id, k_Description, k_AnnotationType) { }

        public BoundingBoxAnnotationDefinition(IEnumerable<Entry> spec)
            : base(k_Id, k_Description, k_AnnotationType)
        {
            this.spec = spec;
        }

        [Serializable]
        public struct Entry : IMessageProducer
        {
            public Entry(int id, string name)
            {
                labelId = id;
                labelName = name;
            }

            public int labelId;
            public string labelName;
            public void ToMessage(IMessageBuilder builder)
            {
                builder.AddInt("label_id", labelId);
                builder.AddString("label_name", labelName);
            }
        }

        public IEnumerable<Entry> spec;

        public override void ToMessage(IMessageBuilder builder)
        {
            base.ToMessage(builder);
            foreach (var e in spec)
            {
                var nested = builder.AddNestedMessageToVector("spec");
                e.ToMessage(nested);
            }
        }
    }

    public class MetricDefinition : IMessageProducer
    {
        public void ToMessage(IMessageBuilder builder)
        {
            throw new NotImplementedException();
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
            annotations = new List<Annotation>();

            metrics = new List<Metric>();
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
        public List<Sensor> sensors;
        /// <summary>
        /// A list of all of the annotations recorded recorded for the frame.
        /// </summary>
        public List<Annotation> annotations;

        /// <summary>
        /// A list of all of the metrics recorded recorded for the frame.
        /// </summary>
        public List<Metric> metrics;

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
            foreach (var annotation in annotations)
            {
                var nested = builder.AddNestedMessageToVector("annotations");
                annotation.ToMessage(nested);
            }
        }
    }
#if false
    public class SoloConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            switch (value)
            {
                case Vector3 v3:
                {
                    writer.WriteStartArray();
                    writer.WriteValue(v3.x);
                    writer.WriteValue(v3.y);
                    writer.WriteValue(v3.z);
                    writer.WriteEndArray();
                    break;
                }
                case Vector2 v2:
                {
                    writer.WriteStartArray();
                    writer.WriteValue(v2.x);
                    writer.WriteValue(v2.y);
                    writer.WriteEndArray();
                    break;
                }
                case Color32 rgba:
                {
                    writer.WriteStartArray();
                    writer.WriteValue(rgba.r);
                    writer.WriteValue(rgba.g);
                    writer.WriteValue(rgba.b);
                    writer.WriteValue(rgba.a);
                    writer.WriteEndArray();
                    break;
                }
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return null;
        }

        public override bool CanConvert(Type objectType)
        {
            if (objectType == typeof(Vector3)) return true;
            if (objectType == typeof(Vector2)) return true;
            return objectType == typeof(Color32);
        }
    }
#endif
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

        public virtual void ToMessage(IMessageBuilder builder)
        {
            builder.AddString("id", Id);
            builder.AddString("sensor_id", sensorType);
            builder.AddFloatVector("position", Utils.ToFloatVector(position));
            builder.AddFloatVector("rotation", Utils.ToFloatVector(rotation));
            builder.AddFloatVector("velocity", Utils.ToFloatVector(velocity));
            builder.AddFloatVector("acceleration", Utils.ToFloatVector(acceleration));
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
#if false
    /// <summary>
    /// Bounding boxes for all of the labeled objects in a capture
    /// </summary>
    [Serializable]
    public class BoundingBoxAnnotation : Annotation
    {
        public struct Entry
        {
            // The instance ID of the object
            public int instanceId;

            public int labelId;

            // The type of the object
            public string labelName;

            /// <summary>
            /// (xy) pixel location of the object's bounding box
            /// </summary>
            public Vector2 origin;
            /// <summary>
            /// (width/height) dimensions of the bounding box
            /// </summary>
            public Vector2 dimension;

            public void ToMessage(IMessageBuilder builder)
            {
                builder.AddInt("instance_id", instanceId);
                builder.AddInt("label_id", labelId);
                builder.AddString("label_name", labelName);
                builder.AddFloatVector("origin", new[] { origin.x, origin.y });
                builder.AddFloatVector("dimension", new[] { dimension.x, dimension.y });
            }
        }

        /// <summary>
        /// The bounding boxes recorded by the annotator
        /// </summary>
        public List<Entry> boxes;

        public override void ToMessage(IMessageBuilder builder)
        {
            base.ToMessage(builder);
            foreach (var e in boxes)
            {
                var nested = builder.AddNestedMessageToVector("values");
                e.ToMessage(nested);
            }
        }
    }
#endif

    [Serializable]
    public class InstanceSegmentationDefinition : AnnotationDefinition
    {
        static readonly string k_Id = "instance segmentation";
        static readonly string k_Description = "You know the deal";
        static readonly string k_AnnotationType = "instance segmentation";

        public InstanceSegmentationDefinition() : base(k_Id, k_Description, k_AnnotationType) { }
    }

    /// <summary>
    /// The instance segmentation image recorded for a capture. This
    /// includes the data that associates a pixel color to an object.
    /// </summary>
    [Serializable]
    public class InstanceSegmentation : Annotation
    {
        public struct Entry
        {
            /// <summary>
            /// The instance ID associated with a pixel color
            /// </summary>
            public int instanceId;
            /// <summary>
            /// The color (rgba) value
            /// </summary>
            public Color32 rgba;

            internal void ToMessage(IMessageBuilder builder)
            {
                builder.AddInt("instance_id", instanceId);
                builder.AddIntVector("rgba", new[] { (int)rgba.r, (int)rgba.g, (int)rgba.b, (int)rgba.a });
            }
        }

        /// <summary>
        /// This instance to pixel map
        /// </summary>
        public List<Entry> instances;

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
            builder.AddFloatVector("dimension", new[] { dimension.x, dimension.y });
            builder.AddPngImage("instance_segmentation", buffer);

            foreach (var e in instances)
            {
                var nested = builder.AddNestedMessageToVector("instances");
                e.ToMessage(nested);
            }
        }
    }

    /// <summary>
    /// Abstract class that holds the common data found in all
    /// metrics. Concrete instances of this class will add
    /// data for their specific metric type.
    /// </summary>
    [Serializable]
    public abstract class Metric
    {
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
    }

    /// <summary>
    /// The object count metric records how many of a particular object are
    /// present in a capture.
    /// </summary>
    [Serializable]
    public class ObjectCountMetric : Metric
    {
        public struct Entry
        {
            /// <summary>
            /// The label of the category
            /// </summary>
            public string labelName;
            /// <summary>
            /// The number of instances for a particular category.
            /// </summary>
            public int count;
        }

        /// <summary>
        ///  The object counts
        /// </summary>
        public List<Entry> objectCounts;
    }
}

