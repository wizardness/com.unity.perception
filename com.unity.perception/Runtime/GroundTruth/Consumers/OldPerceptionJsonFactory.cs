using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Perception.GroundTruth.DataModel;

namespace UnityEngine.Perception.GroundTruth.Consumers
{
    public static class OldPerceptionJsonFactory
    {
        public static JToken Convert(OldPerceptionConsumer consumer, string id, AnnotationDefinition def)
        {
            switch (def)
            {
                case BoundingBox2DLabeler.BoundingBoxAnnotationDefinition b:
                    return JToken.FromObject(LabelConfigurationAnnotationDefinition.Convert(b, "json", b.spec));
                case BoundingBox3DLabeler.BoundingBox3DAnnotationDefinition d:
                    return JToken.FromObject(LabelConfigurationAnnotationDefinition.Convert(d, "json", d.spec));
                case InstanceSegmentationLabeler.InstanceSegmentationDefinition d:
                    return JToken.FromObject(LabelConfigurationAnnotationDefinition.Convert(d, "PNG", d.spec));
                case SemanticSegmentationLabeler.SemanticSegmentationDefinition d:
                    return JToken.FromObject(PerceptionSemanticSegmentationAnnotationDefinition.Convert(consumer, d));
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

        [Serializable]
        struct LabelConfigurationAnnotationDefinition
        {
            public string id;
            public string name;
            public string description;
            public string format;
            public IdLabelConfig.LabelEntrySpec[] spec;

            public static LabelConfigurationAnnotationDefinition Convert(AnnotationDefinition def, string format, IdLabelConfig.LabelEntrySpec[] spec)
            {
                return new LabelConfigurationAnnotationDefinition
                {
                    id = def.id,
                    name = def.id,
                    description = def.description,
                    format = format,
                    spec = spec
                };
            }
        }

        [Serializable]
        struct GenericMetricDefinition
        {
            public string id;
            public string name;
            public string description;

            public static GenericMetricDefinition Convert(string id, MetricDefinition def)
            {
                return new GenericMetricDefinition
                {
                    id = id,
                    name = def.id,
                    description = def.description
                };
            }
        }

        struct LabelConfigMetricDefinition
        {
            public string id;
            public string name;
            public string description;
            public IdLabelConfig.LabelEntrySpec[] spec;

            public static LabelConfigMetricDefinition Convert(string id, MetricDefinition def, IdLabelConfig.LabelEntrySpec[] spec)
            {
                return new LabelConfigMetricDefinition
                {
                    id = id,
                    name = def.id,
                    description = def.description,
                    spec = spec
                };
            }
        }
    }

    [Serializable]
    public struct PerceptionKeyPointValue
    {
        public string id;
        public string annotation_definition;
        public List<Entry> values;

        [Serializable]
        public struct Keypoint
        {
            public int index;
            public Vector2 location;
            public int state;

            public static Keypoint Convert(KeypointLabeler.Keypoint kp)
            {
                return new Keypoint
                {
                    index = kp.index,
                    location = kp.location,
                    state = kp.state
                };
            }
        }

        [Serializable]
        public struct Entry
        {
            public int label_id;
            public uint instance_id;
            public string template_guid;
            public string pose;
            public Keypoint[] keypoints;

            public static Entry Convert(KeypointLabeler.Annotation.Entry entry)
            {
                return new Entry
                {
                    label_id = entry.labelId,
                    instance_id = entry.instanceId,
                    template_guid = entry.templateGuid,
                    pose = entry.pose,
                    keypoints = entry.keypoints.Select(Keypoint.Convert).ToArray()
                };
            }
        }

        public static PerceptionKeyPointValue Convert(OldPerceptionConsumer consumer, KeypointLabeler.Annotation kp)
        {
            return new PerceptionKeyPointValue
            {
                id = kp.Id,
                annotation_definition = kp.description,
                values = kp.entries.Select(Entry.Convert).ToList()
            };
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

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [Serializable]
    struct PerceptionInstanceSegmentationValue
    {
        [Serializable]
        internal struct Entry
        {
            public int instance_id;
            public Color32 color;

            internal static Entry Convert(InstanceSegmentationLabeler.InstanceSegmentation.Entry entry)
            {
                return new Entry
                {
                    instance_id = entry.instanceId,
                    color = entry.rgba
                };
            }
        }

        public string id;
        public string annotation_definition;
        public string filename;
        public List<Entry> values;

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
            return new PerceptionInstanceSegmentationValue
            {
                id = Guid.NewGuid().ToString(),
                annotation_definition = Guid.NewGuid().ToString(),
                filename = consumer.RemoveDatasetPathPrefix(CreateFile(consumer, frame, annotation)),
                values = annotation.instances.Select(Entry.Convert).ToList()
            };
        }
    }

    [Serializable]
    struct LabelDefinitionEntry
    {
        public int label_id;
        public string label_name;
    }

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

            internal static Entry Convert(BoundingBox3DLabeler.BoundingBoxAnnotation.Entry entry)
            {
                return new Entry
                {
                    label_id = entry.labelId,
                    label_name = entry.labelName,
                    instance_id = (uint)entry.instanceId,
                    translation = entry.translation,
                    size = entry.size,
                    rotation = entry.rotation,
                    velocity = entry.velocity,
                    acceleration = entry.acceleration
                };
            }
        }

        public string id;
        public string annotation_definition;
        public List<Entry> values;

        public static PerceptionBounding3dBoxAnnotationValue Convert(OldPerceptionConsumer consumer, string labelerId, string defId, BoundingBox3DLabeler.BoundingBoxAnnotation annotation)
        {
            return new PerceptionBounding3dBoxAnnotationValue
            {
                id = labelerId,
                annotation_definition = defId,
                values = annotation.boxes.Select(Entry.Convert).ToList()
            };
        }
    }

    [Serializable]
    struct PerceptionSemanticSegmentationAnnotationDefinition
    {
        [Serializable]
        internal struct Entry
        {
            public string label_name;
            public Color32 pixel_value;

            internal static Entry Convert(SemanticSegmentationLabeler.SemanticSegmentationDefinition.DefinitionEntry e)
            {
                return new Entry
                {
                    label_name = e.labelName,
                    pixel_value = e.pixelValue
                };
            }
        }

        public string id;
        public string name;
        public string description;
        public string format;
        public List<Entry> spec;

        public static PerceptionSemanticSegmentationAnnotationDefinition Convert(OldPerceptionConsumer consumer, SemanticSegmentationLabeler.SemanticSegmentationDefinition def)
        {
            return new PerceptionSemanticSegmentationAnnotationDefinition
            {
                id = def.id,
                name = def.id,
                description = def.description,
                format = "PNG",
                spec = def.spec.Select(Entry.Convert).ToList()
            };
        }
    }

    [Serializable]
    struct PerceptionKeypointAnnotationDefinition
    {
        [Serializable]
        public struct JointJson
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
        public struct SkeletonJson
        {
            public int joint1;
            public int joint2;
            public Color32 color;

            internal static SkeletonJson Convert(KeypointLabeler.Definition.SkeletonDefinition skel)
            {
                return new SkeletonJson
                {
                    joint1 = skel.joint1,
                    joint2 = skel.joint2,
                    color = skel.color
                };
            }
        }

        [Serializable]
        public struct KeypointJson
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

    [SuppressMessage("ReSharper", "InconsistentNaming")]
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
