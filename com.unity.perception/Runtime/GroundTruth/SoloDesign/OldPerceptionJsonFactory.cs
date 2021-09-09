using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.Perception.GroundTruth.SoloDesign;

namespace GroundTruth.SoloDesign
{
    public static class OldPerceptionJsonFactory
    {
        public static JToken Convert(OldPerceptionConsumer consumer, Guid id, AnnotationDefinition def)
        {
            switch (def)
            {
                case BoundingBoxAnnotationDefinition b:
                    return JToken.FromObject(PerceptionBoundingBoxAnnotationDefinition.Convert(id, b));
            }

            return null;
        }

        public static JToken Convert(OldPerceptionConsumer consumer, Frame frame, Guid labelerId, Guid defId, Annotation annotation)
        {
            switch (annotation)
            {
                case InstanceSegmentation i:
                {
                    return JToken.FromObject(PerceptionInstanceSegmentationValue.Convert(consumer, frame.frame, i), consumer.Serializer);
                }
                case BoundingBox2DLabeler.BoundingBoxAnnotation b:
                {
                    return JToken.FromObject(PerceptionBoundingBoxAnnotationValue.Convert(consumer, labelerId, defId, b), consumer.Serializer);
                }
            }

            return null;
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

            internal static Entry Convert(InstanceSegmentation.Entry entry)
            {
                return new Entry
                {
                    instance_id = entry.instanceId,
                    color = entry.rgba
                };
            }
        }

        public Guid id;
        public Guid annotation_definition;
        public string filename;
        public List<Entry> values;

        static string CreateFile(OldPerceptionConsumer consumer, int frame, InstanceSegmentation annotation)
        {
            var path = consumer.VerifyDirectoryWithGuidExists("InstanceSegmentation");
            path = Path.Combine(path, $"Instance_{frame}.png");
            var file = File.Create(path, 4096);
            file.Write(annotation.buffer, 0, annotation.buffer.Length);
            file.Close();
            return path;
        }

        public static PerceptionInstanceSegmentationValue Convert(OldPerceptionConsumer consumer, int frame, InstanceSegmentation annotation)
        {
            return new PerceptionInstanceSegmentationValue
            {
                id = Guid.NewGuid(),
                annotation_definition = Guid.NewGuid(),
                filename = CreateFile(consumer, frame, annotation),
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

    [Serializable]
    struct PerceptionBoundingBoxAnnotationDefinition
    {
        public Guid id;
        public string name;
        public string description;
        public string format;
        public LabelDefinitionEntry[] spec;

        public static PerceptionBoundingBoxAnnotationDefinition Convert(Guid inId, BoundingBoxAnnotationDefinition box)
        {
            var specs = new LabelDefinitionEntry[box.spec.Count()];
            var i = 0;

            foreach (var e in box.spec)
            {
                specs[i++] = new LabelDefinitionEntry
                {
                    label_id = e.labelId,
                    label_name = e.labelName
                };

            }

            return new PerceptionBoundingBoxAnnotationDefinition
            {
                id = inId,
                name = box.id,
                description = box.description,
                format = "json",
                spec = specs
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
            public int frame;
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
                    label_id = entry.labelId, // TODO
                    frame = -1, // TODO
                    label_name = entry.labelName,
                    instance_id = (uint)entry.instanceId,
                    x = entry.origin.x,
                    y = entry.origin.y,
                    width = entry.dimension.x,
                    height = entry.dimension.y
                };
            }
        }

        public Guid id;
        public Guid annotation_definition;
        public List<Entry> values;

        public static PerceptionBoundingBoxAnnotationValue Convert(OldPerceptionConsumer consumer, Guid labelerId, Guid defId, BoundingBox2DLabeler.BoundingBoxAnnotation annotation)
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
