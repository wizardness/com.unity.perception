using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Perception.GroundTruth.DataModel;

// ReSharper disable InconsistentNaming
// ReSharper disable NotAccessedField.Local
namespace UnityEngine.Perception.GroundTruth.Consumers
{
    public static class OldPerceptionJsonFactory
    {
        public static JToken Convert(OldPerceptionConsumer consumer, string id, AnnotationDefinition annotationDefinition)
        {
            switch (annotationDefinition)
            {
                case BoundingBox2DLabeler.BoundingBoxAnnotationDefinition def:
                    return JToken.FromObject(LabelConfigurationAnnotationDefinition.Convert(def, "json", def.spec));
                case BoundingBox3DLabeler.BoundingBox3DAnnotationDefinition def:
                    return JToken.FromObject(LabelConfigurationAnnotationDefinition.Convert(def, "json", def.spec));
                case InstanceSegmentationLabeler.InstanceSegmentationDefinition def:
                    return JToken.FromObject(LabelConfigurationAnnotationDefinition.Convert(def, "PNG", def.spec));
                case SemanticSegmentationLabeler.SemanticSegmentationDefinition def:
                    return JToken.FromObject(PerceptionSemanticSegmentationAnnotationDefinition.Convert(def, "PNG"));
                case KeypointLabeler.Definition kp:
                    return JToken.FromObject(PerceptionKeypointAnnotationDefinition.Convert(consumer, kp));
            }

            return null;
        }

        public static JToken Convert(OldPerceptionConsumer consumer, string id, MetricDefinition def)
        {
            switch (def)
            {
                case ObjectCountLabeler.ObjectCountMetricDefinition casted:
                    return JToken.FromObject(LabelConfigMetricDefinition.Convert(id, def, casted.spec));
                case RenderedObjectInfoLabeler.MetricDefinition casted:
                    return JToken.FromObject(LabelConfigMetricDefinition.Convert(id, def, casted.spec));
                default:
                    return JToken.FromObject(GenericMetricDefinition.Convert(id, def));
            }
        }

        public static JToken Convert(OldPerceptionConsumer consumer, Frame frame, string labelerId, string defId, Annotation annotation)
        {
            switch (annotation)
            {
                case InstanceSegmentationLabeler.InstanceSegmentation i:
                {
                    return JToken.FromObject(PerceptionInstanceSegmentationValue.Convert(consumer, frame.frame, i), consumer.Serializer);
                }
                case BoundingBox2DLabeler.BoundingBoxAnnotation b:
                {
                    return JToken.FromObject(PerceptionBoundingBoxAnnotationValue.Convert(consumer, labelerId, defId, b), consumer.Serializer);
                }
                case BoundingBox3DLabeler.BoundingBoxAnnotation b:
                {
                    return JToken.FromObject(PerceptionBounding3dBoxAnnotationValue.Convert(consumer, labelerId, defId, b), consumer.Serializer);
                }
                case SemanticSegmentationLabeler.SemanticSegmentation s:
                {
                    return JToken.FromObject(PerceptionSemanticSegmentationValue.Convert(consumer, frame.frame, s), consumer.Serializer);
                }
                case KeypointLabeler.Annotation kp:
                {
                    return JToken.FromObject(PerceptionKeyPointValue.Convert(consumer, kp), consumer.Serializer);
                }
            }

            return null;
        }
    }

    [Serializable]
    struct LabelConfigMetricDefinition
    {
        LabelConfigMetricDefinition(string id, string name, string description, IdLabelConfig.LabelEntrySpec[] spec)
        {
            this.id = id;
            this.name = name;
            this.description = description;
            this.spec = spec;
        }

        public string id;
        public string name;
        public string description;
        public IdLabelConfig.LabelEntrySpec[] spec;

        public JToken ToJToken()
        {
            return JToken.FromObject(this);
        }

        public static LabelConfigMetricDefinition Convert(string id, MetricDefinition def, IdLabelConfig.LabelEntrySpec[] spec)
        {
            return new LabelConfigMetricDefinition(id, def.id, def.description, spec);
        }
    }

    [Serializable]
    struct LabelConfigurationAnnotationDefinition
    {
        public string id;
        public string name;
        public string description;
        public string format;
        public IdLabelConfig.LabelEntrySpec[] spec;

        LabelConfigurationAnnotationDefinition(AnnotationDefinition def, string format, IdLabelConfig.LabelEntrySpec[] spec)
        {
            id = def.id;
            name = def.id;
            description = def.description;
            this.format = format;
            this.spec = spec;
        }

        public static LabelConfigurationAnnotationDefinition Convert(AnnotationDefinition def, string format, IdLabelConfig.LabelEntrySpec[] spec)
        {
            return new LabelConfigurationAnnotationDefinition(def, format, spec);
        }
    }

    [Serializable]
    struct GenericMetricDefinition
    {
        public string id;
        public string name;
        public string description;

        public GenericMetricDefinition(string id, MetricDefinition def)
        {
            this.id = id;
            name = def.id;
            description = def.description;
        }

        public static GenericMetricDefinition Convert(string id, MetricDefinition def)
        {
            return new GenericMetricDefinition(id, def);
        }
    }

    [Serializable]
    struct PerceptionKeyPointValue
    {
        public string id;
        public string annotation_definition;
        public List<Entry> values;

        [Serializable]
        internal struct Keypoint
        {
            public int index;
            public Vector2 location;
            public int state;

            Keypoint(KeypointLabeler.Keypoint kp)
            {
                index = kp.index;
                location = kp.location;
                state = kp.state;
            }

            public static Keypoint Convert(KeypointLabeler.Keypoint kp)
            {
                return new Keypoint(kp);
            }
        }

        [Serializable]
        internal struct Entry
        {
            public int label_id;
            public uint instance_id;
            public string template_guid;
            public string pose;
            public Keypoint[] keypoints;

            Entry(KeypointLabeler.Annotation.Entry entry)
            {
                label_id = entry.labelId;
                instance_id = entry.instanceId;
                template_guid = entry.templateGuid;
                pose = entry.pose;
                keypoints = entry.keypoints.Select(Keypoint.Convert).ToArray();
            }

            public static Entry Convert(KeypointLabeler.Annotation.Entry entry)
            {
                return new Entry(entry);
            }
        }

        PerceptionKeyPointValue(KeypointLabeler.Annotation kp)
        {
            id = kp.Id;
            annotation_definition = kp.description;
            values = kp.entries.Select(Entry.Convert).ToList();
        }

        public static PerceptionKeyPointValue Convert(OldPerceptionConsumer consumer, KeypointLabeler.Annotation kp)
        {
            return new PerceptionKeyPointValue(kp);
        }
    }

    [Serializable]
    struct PerceptionSemanticSegmentationValue
    {
        public string id;
        public string annotation_definition;
        public string filename;

        static string CreateFile(OldPerceptionConsumer consumer, int frame, SemanticSegmentationLabeler.SemanticSegmentation annotation)
        {
            var path = consumer.VerifyDirectoryWithGuidExists("SemanticSegmentation");
            path = Path.Combine(path, $"segmentation_{frame}.png");
            var file = File.Create(path, 4096);
            file.Write(annotation.buffer, 0, annotation.buffer.Length);
            file.Close();
            return path;
        }

        public static PerceptionSemanticSegmentationValue Convert(OldPerceptionConsumer consumer, int frame, SemanticSegmentationLabeler.SemanticSegmentation annotation)
        {
            return new PerceptionSemanticSegmentationValue
            {
                id = Guid.NewGuid().ToString(),
                annotation_definition = Guid.NewGuid().ToString(),
                filename = consumer.RemoveDatasetPathPrefix(CreateFile(consumer, frame, annotation)),
            };
        }
    }

    [Serializable]
    struct PerceptionInstanceSegmentationValue
    {
        internal struct Entry
        {
            public int instance_id;
            public Color32 color;

            Entry(InstanceSegmentationLabeler.InstanceSegmentation.Entry entry)
            {
                instance_id = entry.instanceId;
                color = entry.rgba;
            }

            internal static Entry Convert(InstanceSegmentationLabeler.InstanceSegmentation.Entry entry)
            {
                return new Entry(entry);
            }
        }

        public string id;
        public string annotation_definition;
        public string filename;
        public List<Entry> values;

        PerceptionInstanceSegmentationValue(OldPerceptionConsumer consumer, int frame, InstanceSegmentationLabeler.InstanceSegmentation annotation)
        {
            id = annotation.Id;
            annotation_definition = annotation.description;
            filename = consumer.RemoveDatasetPathPrefix(CreateFile(consumer, frame, annotation));
            values = annotation.instances.Select(Entry.Convert).ToList();
        }

        static string CreateFile(OldPerceptionConsumer consumer, int frame, InstanceSegmentationLabeler.InstanceSegmentation annotation)
        {
            var path = consumer.VerifyDirectoryWithGuidExists("InstanceSegmentation");
            path = Path.Combine(path, $"Instance_{frame}.png");
            var file = File.Create(path, 4096);
            file.Write(annotation.buffer, 0, annotation.buffer.Length);
            file.Close();
            return path;
        }

        public static PerceptionInstanceSegmentationValue Convert(OldPerceptionConsumer consumer, int frame, InstanceSegmentationLabeler.InstanceSegmentation annotation)
        {
            return new PerceptionInstanceSegmentationValue(consumer, frame, annotation);
        }
    }

    [Serializable]
    struct PerceptionBounding3dBoxAnnotationValue
    {
        [Serializable]
        internal struct Entry
        {
            public int label_id;
            public string label_name;
            public uint instance_id;
            public Vector3 translation;
            public Vector3 size;
            public Quaternion rotation;
            public Vector3 velocity;
            public Vector3 acceleration;

            Entry(BoundingBox3DLabeler.BoundingBoxAnnotation.Entry entry)
            {
                label_id = entry.labelId;
                label_name = entry.labelName;
                instance_id = entry.instanceId;
                translation = entry.translation;
                size = entry.size;
                rotation = entry.rotation;
                velocity = entry.velocity;
                acceleration = entry.acceleration;
            }

            internal static Entry Convert(BoundingBox3DLabeler.BoundingBoxAnnotation.Entry entry)
            {
                return new Entry(entry);
            }
        }

        public string id;
        public string annotation_definition;
        public List<Entry> values;

        PerceptionBounding3dBoxAnnotationValue(string labelerId, string defId, BoundingBox3DLabeler.BoundingBoxAnnotation annotation)
        {
            id = labelerId;
            annotation_definition = defId;
            values = annotation.boxes.Select(Entry.Convert).ToList();
        }

        public static PerceptionBounding3dBoxAnnotationValue Convert(OldPerceptionConsumer consumer, string labelerId, string defId, BoundingBox3DLabeler.BoundingBoxAnnotation annotation)
        {
            return new PerceptionBounding3dBoxAnnotationValue(labelerId, defId, annotation);
        }
    }

    [Serializable]
    struct PerceptionSemanticSegmentationAnnotationDefinition
    {
        internal struct Entry
        {
            public string label_name;
            public Color32 pixel_value;

            Entry(SemanticSegmentationLabeler.SemanticSegmentationDefinition.DefinitionEntry e)
            {
                label_name = e.labelName;
                pixel_value = e.pixelValue;
            }

            internal static Entry Convert(SemanticSegmentationLabeler.SemanticSegmentationDefinition.DefinitionEntry e)
            {
                return new Entry(e);
            }
        }

        public string id;
        public string name;
        public string description;
        public string format;
        public List<Entry> spec;

        PerceptionSemanticSegmentationAnnotationDefinition(SemanticSegmentationLabeler.SemanticSegmentationDefinition def, string format)
        {
            id = def.id;
            name = def.id;
            description = def.description;
            spec = def.spec.Select(Entry.Convert).ToList();
            this.format = format;
        }

        public static PerceptionSemanticSegmentationAnnotationDefinition Convert(SemanticSegmentationLabeler.SemanticSegmentationDefinition def, string format)
        {
            return new PerceptionSemanticSegmentationAnnotationDefinition(def, format);
        }
    }

    [Serializable]
    struct PerceptionKeypointAnnotationDefinition
    {
        [Serializable]
        internal struct JointJson
        {
            public string label;
            public int index;
            public Color32 color;

            internal static JointJson Convert(KeypointLabeler.Definition.JointDefinition joint)
            {
                return new JointJson
                {
                    label = joint.label,
                    index = joint.index,
                    color = joint.color
                };
            }
        }

        [Serializable]
        internal struct SkeletonJson
        {
            public int joint1;
            public int joint2;
            public Color32 color;

            internal static SkeletonJson Convert(KeypointLabeler.Definition.SkeletonDefinition skeleton)
            {
                return new SkeletonJson
                {
                    joint1 = skeleton.joint1,
                    joint2 = skeleton.joint2,
                    color = skeleton.color
                };
            }
        }

        [Serializable]
        internal struct KeypointJson
        {
            public string template_id;
            public string template_name;
            public JointJson[] key_points;
            public SkeletonJson[] skeleton;

            internal static KeypointJson Convert(KeypointLabeler.Definition.Entry e)
            {
                return new KeypointJson
                {
                    template_id = e.templateId,
                    template_name = e.templateName,
                    key_points = e.keyPoints.Select(JointJson.Convert).ToArray(),
                    skeleton = e.skeleton.Select(SkeletonJson.Convert).ToArray()
                };
            }
        }

        public string id;
        public string name;
        public string description;
        public string format;
        public List<KeypointJson> spec;

        public static PerceptionKeypointAnnotationDefinition Convert(OldPerceptionConsumer consumer, KeypointLabeler.Definition def)
        {
            return new PerceptionKeypointAnnotationDefinition
            {
                id = def.id,
                name = def.id,
                description = def.description,
                format = "json",
                spec = def.entries.Select(KeypointJson.Convert).ToList()
            };
        }
    }

    [Serializable]
    struct PerceptionBoundingBoxAnnotationValue
    {
        [Serializable]
        internal struct Entry
        {
            public int label_id;
            public string label_name;
            public uint instance_id;
            public float x;
            public float y;
            public float width;
            public float height;

            internal static Entry Convert(BoundingBox2DLabeler.BoundingBoxAnnotation.Entry entry)
            {
                return new Entry
                {
                    label_id = entry.labelId,
                    label_name = entry.labelName,
                    instance_id = (uint)entry.instanceId,
                    x = entry.origin.x,
                    y = entry.origin.y,
                    width = entry.dimension.x,
                    height = entry.dimension.y
                };
            }
        }

        public string id;
        public string annotation_definition;
        public List<Entry> values;

        public static PerceptionBoundingBoxAnnotationValue Convert(OldPerceptionConsumer consumer, string labelerId, string defId, BoundingBox2DLabeler.BoundingBoxAnnotation annotation)
        {
            return new PerceptionBoundingBoxAnnotationValue
            {
                id = labelerId,
                annotation_definition = defId,
                values = annotation.boxes.Select(Entry.Convert).ToList()
            };
        }
    }
}
