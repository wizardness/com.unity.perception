using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityEngine.Perception.GroundTruth.SoloDesign
{
    public class SoloConsumer : PerceptionConsumer
    {
        public string _baseDirectory = "D:/PerceptionOutput/SoloConsumer";
        public string soloDatasetName = "solo";
        static string currentDirectory = "";

        SimulationMetadata m_CurrentMetadata;

        void Start()
        {
            // Only here to get the check mark to show up in Unity Editor
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

        void WriteJTokenToFile(string filePath, JToken jToken)
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

            Debug.Log("SC - On Frame Generated");
        }

        public override void OnSimulationCompleted(CompletionMetadata metadata)
        {
            Debug.Log("SC - On Simulation Completed");
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
            var annotations = new JArray();
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

            foreach (var annotation in frame.annotations)
            {
                switch (annotation)
                {
                    case BoundingBox2DLabeler.BoundingBoxAnnotation bbox:
                        annotations.Add(ConvertAnnotation(frame, bbox));
                        break;
                    case InstanceSegmentation seg:
                        annotations.Add(ConvertAnnotation(frame, seg));
                        break;
                }
            }

            frameJson["captures"] = captures;
            frameJson["annotations"] = annotations;
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
            var file = File.Create(path, 4096);
            file.Write(sensor.buffer, 0, sensor.buffer.Length);
            file.Close();

            var outRgb = ToSensorHeader(frame, sensor);
            outRgb["fileName"] = path;
            outRgb["imageFormat"] = sensor.imageFormat;
            outRgb["dimension"] = FromVector2(sensor.dimension);
            return outRgb;
        }

        static JToken ToAnnotationHeader(Frame frame, Annotation annotation)
        {
            var token = new JObject
            {
                ["Id"] = annotation.Id,
                ["definition"] = annotation.description,
                ["sequence"] = frame.sequence,
                ["step"] = frame.step,
                ["sensor"] = annotation.sensorId
            };
            return token;
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

        static JToken ConvertAnnotation(Frame frame, InstanceSegmentation segmentation)
        {
            // write out the png data
            var path = GetSequenceDirectoryPath(frame);

            path = Path.Combine(path,$"step{frame.step}.segmentation.{segmentation.imageFormat}");
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
    }
}
