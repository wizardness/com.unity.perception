using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Perception.GroundTruth.DataModel;

namespace UnityEngine.Perception.GroundTruth.Consumers
{
    [CreateAssetMenu(fileName = "SoloConfiguration", menuName = "Perception/Endpoint/Solo Consumer", order = 3)]
    public class SoloConsumer : ConsumerEndpoint
    {
        public string _baseDirectory = "D:/PerceptionOutput/SoloConsumer";
        public string soloDatasetName = "solo";

        static string currentDirectory = "";
        SimulationMetadata m_CurrentMetadata;

        bool m_IsComplete = false;

        protected override bool IsComplete()
        {
            return m_IsComplete;
        }

        public override object Clone()
        {
            var newOne = ScriptableObject.CreateInstance<SoloConsumer>();
            newOne._baseDirectory = _baseDirectory;
            newOne.soloDatasetName = soloDatasetName;
            return newOne;
        }

        public override void OnSimulationStarted(SimulationMetadata metadata)
        {
            Debug.Log("SC - On Simulation Started");
            m_CurrentMetadata = metadata;

            var i = 0;
            while (true)
            {
                var n = $"{soloDatasetName}_{i++}";
                n = Path.Combine(_baseDirectory, n);
                if (!Directory.Exists(n))
                {
                    Directory.CreateDirectory(n);
                    currentDirectory = n;
                    break;
                }
            }
        }

        static string GetSequenceDirectoryPath(Frame frame)
        {
            var path = $"sequence.{frame.sequence}";

            // verify that a directory already exists for a sequence,
            // if not, create it.
            path = Path.Combine(currentDirectory, path);

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

        static void WriteJTokenToFile(string filePath, JToken jToken)
        {
            var stringWriter = new StringWriter(new StringBuilder(256), CultureInfo.InvariantCulture);
            using (var jsonTextWriter = new JsonTextWriter(stringWriter))
            {
                jsonTextWriter.Formatting = Formatting.Indented;
                jToken.WriteTo(jsonTextWriter);
            }

            var contents = stringWriter.ToString();

            File.WriteAllText(filePath, contents);
        }

        public override void OnFrameGenerated(Frame frame)
        {
            var path = GetSequenceDirectoryPath(frame);
            path = Path.Combine(path, $"step{frame.step}.frame_data.json");

            WriteJTokenToFile(path, ToFrame(frame));
        }

        public override void OnSimulationCompleted(CompletionMetadata metadata)
        {
            var path = Path.Combine(currentDirectory, "metadata.json");
            WriteJTokenToFile(path, ToMetadata(metadata));

            m_IsComplete = true;
        }

        static JToken ToMetadata(CompletionMetadata metadata)
        {
            var sequences = new JArray();
            if (metadata.sequences != null)
            {
                foreach (var sequence in metadata.sequences)
                {
                    sequences.Add(new JObject
                    {
                        ["id"] = sequence.id,
                        ["number_of_steps"] = sequence.numberOfSteps
                    });
                }

                sequences.Add(new JObject());
            }

            var json = new JObject
            {
                ["unity_version"] = metadata.unityVersion,
                ["perception_version"] = metadata.perceptionVersion,
                ["render_pipeline"] = metadata.renderPipeline,
                ["total_frames"] = metadata.totalFrames,
                ["sequences"] = sequences
            };

            return json;
        }

        static JToken ToFrame(Frame frame)
        {
            var frameJson = new JObject
            {
                ["frame"] = frame.frame,
                ["sequence"] = frame.sequence,
                ["step"] = frame.step
            };

            var captures = new JArray();
            var metrics = new JArray();

            foreach (var sensor in frame.sensors)
            {
                switch (sensor)
                {
                    case RgbSensor rgb:
                        captures.Add(ConvertSensor(frame, rgb));
                        break;
                }
            }

            frameJson["captures"] = captures;

            foreach (var metric in frame.metrics)
            {
                switch (metric)
                {
                    case ObjectCountLabeler.ObjectCountMetric objCount:
                        metrics.Add(ConvertMetric(frame, objCount));
                        break;
                    case RenderedObjectInfoLabeler.RenderedObjectInfoMetric info:
                        metrics.Add(ConvertMetric(frame, info));
                        break;
                }
            }

            frameJson["metrics"] = metrics;

            return frameJson;
        }

        static JArray FromVector3(Vector3 vector3)
        {
            return new JArray
            {
                vector3.x, vector3.y, vector3.z
            };
        }

        static JArray FromVector2(Vector2 vector2)
        {
            return new JArray
            {
                vector2.x, vector2.y
            };
        }

        static JArray FromColor32(Color32 color)
        {
            return new JArray
            {
                color.r, color.g, color.b, color.a
            };
        }

        static JToken ToSensorHeader(Frame frame, Sensor sensor)
        {
            var token = new JObject
            {
                ["Id"] = sensor.Id,
                ["sensorType"] = sensor.sensorType,
                ["position"] = FromVector3(sensor.position),
                ["rotation"] = FromVector3(sensor.rotation),
                ["velocity"] = FromVector3(sensor.velocity),
                ["acceleration"] = FromVector3(sensor.acceleration)
            };
            return token;
        }

        static JToken ConvertSensor(Frame frame, RgbSensor sensor)
        {
            // write out the png data
            var path = GetSequenceDirectoryPath(frame);

            path = Path.Combine(path, $"step{frame.step}.{sensor.sensorType}.{sensor.imageFormat}");
#if true
            var file = File.Create(path, 4096);
            file.Write(sensor.buffer, 0, sensor.buffer.Length);
            file.Close();
#else
            var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            allTasks.Add(fs.WriteAsync(sensor.buffer, 0, sensor.buffer.Length));

#endif
            var outRgb = ToSensorHeader(frame, sensor);
            outRgb["fileName"] = path;
            outRgb["imageFormat"] = sensor.imageFormat.ToString();
            outRgb["dimension"] = FromVector2(sensor.dimension);

            var annotations = new JArray();


            foreach (var annotation in sensor.annotations)
            {
                switch (annotation)
                {
                    case BoundingBox2DLabeler.BoundingBoxAnnotation bbox:
                        annotations.Add(ConvertAnnotation(frame, bbox));
                        break;
                    case InstanceSegmentationLabeler.InstanceSegmentation seg:
                        annotations.Add(ConvertAnnotation(frame, seg));
                        break;
                    case SemanticSegmentationLabeler.SemanticSegmentation seg:
                        annotations.Add(ConvertAnnotation(frame, seg));
                        break;
                    case BoundingBox3DLabeler.BoundingBoxAnnotation bbox:
                        annotations.Add(ConvertAnnotation(frame, bbox));
                        break;
                    case KeypointLabeler.Annotation keypoint:
                        annotations.Add(ConvertAnnotation(frame, keypoint));
                        break;
                }
            }

            outRgb["annotations"] = annotations;

            return outRgb;
        }

        static JToken ToAnnotationHeader(Frame frame, Annotation annotation)
        {
            return new JObject
            {
                ["Id"] = annotation.Id,
                ["definition"] = annotation.description,
                ["sequence"] = frame.sequence,
                ["step"] = frame.step,
                ["sensor"] = annotation.sensorId
            };
        }

        static JToken ToMetricHeader(Frame frame, Metric metric)
        {
            return new JObject
            {
                ["sensorId"] = metric.sensorId,
                ["annotationId"] = metric.annotationId,
                ["description"] = metric.description
            };
        }

        static JToken ConvertAnnotation(Frame frame, KeypointLabeler.Annotation keypoint)
        {
            var outBox = ToAnnotationHeader(frame, keypoint);
            var values = new JArray();

            foreach (var kp in keypoint.entries)
            {
                var keypoints = new JArray();

                foreach (var k in kp.keypoints)
                {
                    keypoints.Add(new JObject
                    {
                        ["index"] = k.index,
                        ["location"] = FromVector2(k.location),
                        ["state"] = k.state
                    });
                }

                values.Add(new JObject
                {
                    ["instance_id"] = kp.instanceId,
                    ["label_id"] = kp.labelId,
                    ["template_guid"] = kp.templateGuid,
                    ["pose"] = kp.pose,
                    ["keypoints"] = keypoints
                });
            }

            outBox["values"] = values;

            return outBox;
        }

        static JToken ConvertAnnotation(Frame frame, BoundingBox3DLabeler.BoundingBoxAnnotation bbox)
        {
            var outBox = ToAnnotationHeader(frame, bbox);
            var values = new JArray();

            foreach (var box in bbox.boxes)
            {
                values.Add(new JObject
                {
                    ["frame"] = frame.frame,
                    ["label_name"] = box.labelName,
                    ["instance_id"] = box.instanceId,
                    ["translation"] = FromVector3(box.translation),
                    ["size"] = FromVector3(box.size),
                    ["rotation"] = FromVector3(box.rotation.eulerAngles),
                    ["velocity"] = FromVector3(box.velocity),
                    ["acceleration"] = FromVector3(box.acceleration)
                });
            }

            outBox["values"] = values;

            return outBox;
        }

        static JToken ConvertAnnotation(Frame frame, BoundingBox2DLabeler.BoundingBoxAnnotation bbox)
        {
            var outBox = ToAnnotationHeader(frame, bbox);
            var values = new JArray();

            foreach (var box in bbox.boxes)
            {
                values.Add(new JObject
                {
                    ["frame"] = frame.frame,
                    ["label_name"] = box.labelName,
                    ["instance_id"] = box.instanceId,
                    ["origin"] = FromVector2(box.origin),
                    ["dimension"] = FromVector2(box.dimension)
                });
            }

            outBox["values"] = values;

            return outBox;
        }

        static JToken ConvertMetric(Frame frame, ObjectCountLabeler.ObjectCountMetric count)
        {
            var outCount = ToMetricHeader(frame, count);
            var values = new JArray();

            foreach (var i in count.objectCounts)
            {
                values.Add(new JObject
                {
                    ["label_name"] = i.labelName,
                    ["count"] = i.count
                });
            }

            outCount["object_counts"] = values;
            return outCount;
        }

        static JToken ConvertMetric(Frame frame, RenderedObjectInfoLabeler.RenderedObjectInfoMetric info)
        {
            var outCount = ToMetricHeader(frame, info);
            var values = new JArray();

            foreach (var i in info.objectInfo)
            {
                values.Add(new JObject
                {
                    ["label_id"] = i.label_id,
                    ["instance_id"] = i.instance_id,
                    ["instance_color"] = FromColor32(i.instance_color),
                    ["visible_pixels"] = i.visible_pixels
                });
            }

            outCount["object_info"] = values;
            return outCount;
        }

        static JToken ConvertAnnotation(Frame frame, InstanceSegmentationLabeler.InstanceSegmentation segmentation)
        {
            // write out the png data
            var path = GetSequenceDirectoryPath(frame);

            path = Path.Combine(path,$"step{frame.step}.instance.segmentation.{segmentation.imageFormat}");
            var file = File.Create(path, 4096);
            file.Write(segmentation.buffer, 0, segmentation.buffer.Length);
            file.Close();

            var outSeg = ToAnnotationHeader(frame, segmentation);
            var values = new JArray();

            foreach (var i in segmentation.instances)
            {
                values.Add(new JObject
                {
                    ["instance_id"] = i.instanceId,
                    ["rgba"] = FromColor32(i.rgba)
                });
            }

            outSeg["imageFormat"] = segmentation.imageFormat;
            outSeg["dimension"] = FromVector2(segmentation.dimension);
            outSeg["imagePath"] = path;
            outSeg["instances"] = values;

            return outSeg;
        }

        static JToken ConvertAnnotation(Frame frame, SemanticSegmentationLabeler.SemanticSegmentation segmentation)
        {
            // write out the png data
            var path = GetSequenceDirectoryPath(frame);

            path = Path.Combine(path,$"step{frame.step}.semantic.segmentation.{segmentation.imageFormat}");
            var file = File.Create(path, 4096);
            file.Write(segmentation.buffer, 0, segmentation.buffer.Length);
            file.Close();

            var outSeg = ToAnnotationHeader(frame, segmentation);
            var values = new JArray();

            foreach (var i in segmentation.instances)
            {
                values.Add(new JObject
                {
                    ["label_name"] = i.labelName,
                    ["rgba"] = FromColor32(i.pixelValue)
                });
            }

            outSeg["imageFormat"] = segmentation.imageFormat;
            outSeg["dimension"] = FromVector2(segmentation.dimension);
            outSeg["imagePath"] = path;
            outSeg["instances"] = values;

            return outSeg;
        }
    }
}
