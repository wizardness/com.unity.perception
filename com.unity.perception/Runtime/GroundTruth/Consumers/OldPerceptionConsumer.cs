using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Perception.GroundTruth.DataModel;
using Formatting = Newtonsoft.Json.Formatting;

namespace UnityEngine.Perception.GroundTruth.Consumers
{
    public class PerceptionResolver : DefaultContractResolver
    {
        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var contract = base.CreateObjectContract(objectType);
            if (objectType == typeof(Vector3) ||
                objectType == typeof(Vector2) ||
                objectType == typeof(Color) ||
                objectType == typeof(Quaternion))
            {
                contract.Converter = PerceptionConverter.Instance;
            }

            return contract;
        }
    }

    public class PerceptionConverter : JsonConverter
    {
        public static PerceptionConverter Instance = new PerceptionConverter();

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
                    writer.WriteStartObject();
                    writer.WritePropertyName("r");
                    writer.WriteValue(rgba.r);
                    writer.WritePropertyName("g");
                    writer.WriteValue(rgba.g);
                    writer.WritePropertyName("b");
                    writer.WriteValue(rgba.b);
                    writer.WritePropertyName("a");
                    writer.WriteValue(rgba.a);
                    writer.WriteEndObject();
                    break;
                }
                case Quaternion quaternion:
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("x");
                    writer.WriteValue(quaternion.x);
                    writer.WritePropertyName("y");
                    writer.WriteValue(quaternion.y);
                    writer.WritePropertyName("z");
                    writer.WriteValue(quaternion.z);
                    writer.WritePropertyName("w");
                    writer.WriteValue(quaternion.w);
                    writer.WriteEndObject();
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
            if (objectType == typeof(Quaternion)) return true;
            return objectType == typeof(Color32);
        }
    }

    public class OldPerceptionConsumer : ConsumerEndpoint
    {
        static readonly string version = "0.1.1";

        public string baseDirectory = "D:/PerceptionOutput/KickinItOldSchool";
        public int capturesPerFile = 150;
        public int metricsPerFile = 150;

        internal JsonSerializer Serializer { get; }= new JsonSerializer { ContractResolver = new PerceptionResolver()};

        JsonSerializer m_JsonSerializer = new JsonSerializer();
        string m_CurrentPath;
        string m_DatasetPath;
        string m_RgbPath;
        string m_LogsPath;

        [Serializable]
        struct SensorInfo
        {
            public string id;
            public string modality;
            public string description;
        }

        Dictionary<string, SensorInfo> m_SensorMap = new Dictionary<string, SensorInfo>();
        Dictionary<string, AnnotationDefinition> m_RegisteredAnnotations = new Dictionary<string, AnnotationDefinition>();
        Dictionary<string, MetricDefinition> m_RegisteredMetrics = new Dictionary<string, MetricDefinition>();

        List<PerceptionCapture> m_CurrentCaptures = new List<PerceptionCapture>();

        protected override bool IsComplete()
        {
            return true;
        }

        internal string VerifyDirectoryWithGuidExists(string directoryPrefix, bool appendGuid = true)
        {
            var dirs = Directory.GetDirectories(m_CurrentPath);
            var found = string.Empty;

            foreach (var dir in dirs)
            {
                var dirName = new DirectoryInfo(dir).Name;
                if (dirName.StartsWith(directoryPrefix))
                {
                    found = dir;
                    break;
                }
            }

            if (found == string.Empty)
            {
                var dirName = appendGuid ? $"{directoryPrefix}{Guid.NewGuid().ToString()}" : directoryPrefix;
                found = Path.Combine(m_CurrentPath, dirName);
                Directory.CreateDirectory(found);
            }

            return found;
        }

        public override void OnAnnotationRegistered(AnnotationDefinition annotationDefinition)
        {
            if (m_RegisteredAnnotations.ContainsKey(annotationDefinition.id))
            {
                Debug.LogError("Tried to register an annotation twice");
                return;
            }

            m_RegisteredAnnotations[annotationDefinition.id] = annotationDefinition;
        }

        public override void OnMetricRegistered(MetricDefinition metricDefinition)
        {
            if (m_RegisteredMetrics.ContainsKey(metricDefinition.id))
            {
                Debug.LogError("Tried to register a metric twice");
                return;
            }

            m_RegisteredMetrics[metricDefinition.id] = metricDefinition;
        }

        public override void OnSensorRegistered(SensorDefinition sensor)
        {
            if (m_SensorMap.ContainsKey(sensor.id))
            {
                Debug.LogError("Tried to register a sensor twice");
                return;
            }

            m_SensorMap[sensor.id] = new SensorInfo
            {
                id = sensor.id,
                modality = sensor.modality,
                description = sensor.definition
            };
        }

        public string RemoveDatasetPathPrefix(string path)
        {
            return path.Replace(m_CurrentPath + "\\", string.Empty);
        }

        public override void OnSimulationStarted(SimulationMetadata metadata)
        {
            // Create a directory guid...
            var path = Guid.NewGuid().ToString();

            m_CurrentPath =  Path.Combine(baseDirectory, path);
            Directory.CreateDirectory(m_CurrentPath);

            m_DatasetPath = VerifyDirectoryWithGuidExists("Dataset");
            m_RgbPath = VerifyDirectoryWithGuidExists("RGB");
            m_LogsPath = VerifyDirectoryWithGuidExists("Logs", false);
        }

        public override void OnFrameGenerated(Frame frame)
        {
            var seqId = frame.sequence.ToString();

            // Only support one image file right now
            var path = "";

            var annotations = new JArray();
            RgbSensor rgbSensor = null;
            if (frame.sensors.Count() == 1)
            {
                var sensor = frame.sensors.First();
                if (sensor is RgbSensor rgb)
                {
                    rgbSensor = rgb;
                    path = WriteOutImageFile(frame.frame, rgb);
                }

                foreach (var annotation in sensor.annotations)
                {
                    string defId = null;
                    if (!m_RegisteredAnnotations.TryGetValue(annotation.Id, out var def))
                    {
                        defId = null;
                    }

                    defId = def.id;
                    var json = OldPerceptionJsonFactory.Convert(this, frame, annotation.Id, defId, annotation);
                    if (json != null) annotations.Add(json);
                }
            }

            foreach (var metric in  frame.metrics)
            {
                AddMetricToReport(metric);
            }

            var capture = new PerceptionCapture
            {
                id = $"frame_{frame.frame}",
                filename = RemoveDatasetPathPrefix(path),
                format = "PNG",
                sequence_id = seqId,
                step = frame.step,
                timestamp = frame.timestamp,
                sensor =  PerceptionRgbSensor.Convert(this, rgbSensor, path),
                annotations = annotations
            };

            m_CurrentCaptures.Add(capture);

            WriteCaptures();
        }

        void WriteMetrics(bool flush = false)
        {
            if (flush || m_MetricsReady.Count > metricsPerFile)
            {
                WriteMetricsFile(m_MetricOutCount++, m_MetricsReady);
                m_MetricsReady.Clear();
            }
        }

        void WriteCaptures(bool flush = false)
        {
            if (flush || m_CurrentCaptures.Count >= capturesPerFile)
            {
                WriteCaptureFile(m_CurrentCaptureIndex++, m_CurrentCaptures);
                m_CurrentCaptures.Clear();
            }
        }

        public override void OnSimulationCompleted(CompletionMetadata metadata)
        {
            WriteSensorsFile();
            WriteAnnotationsDefinitionsFile();
            WriteMetricsDefinitionsFile();

            WriteCaptures(true);
            WriteMetrics(true);
        }

        int m_CurrentCaptureIndex = 0;

        string WriteOutImageFile(int frame, RgbSensor rgb)
        {
            var path = Path.Combine(m_RgbPath, $"{rgb.sensorType}_{frame}.png");
            var file = File.Create(path, 4096);
            file.Write(rgb.buffer, 0, rgb.buffer.Length);
            file.Close();
            return path;
        }

        void WriteJTokenToFile(string filePath, PerceptionJson json)
        {
            WriteJTokenToFile(filePath,  JToken.FromObject(json, Serializer));
        }

        void WriteJTokenToFile(string filePath, MetricsJson json)
        {
            WriteJTokenToFile(filePath, JToken.FromObject(json, Serializer));
        }

        static void WriteJTokenToFile(string filePath, JToken json)
        {
            var stringWriter = new StringWriter(new StringBuilder(256), CultureInfo.InvariantCulture);
            using (var jsonTextWriter = new JsonTextWriter(stringWriter))
            {
                jsonTextWriter.Formatting = Formatting.Indented;
                json.WriteTo(jsonTextWriter);
            }

            var contents = stringWriter.ToString();

            File.WriteAllText(filePath, contents);
        }

        void WriteAnnotationsDefinitionsFile()
        {
            var defs = new JArray();

            foreach (var def in m_RegisteredAnnotations.Values)
            {
                defs.Add(OldPerceptionJsonFactory.Convert(this, def.id, def));
            }

            var top = new JObject
            {
                ["version"] = version,
                ["annotation_definitions"] = defs
            };
            var path = Path.Combine(m_DatasetPath, "annotation_definitions.json");
            WriteJTokenToFile(path, top);
        }

        void WriteMetricsDefinitionsFile()
        {
            var defs = new JArray();

            foreach (var def in m_RegisteredMetrics.Values)
            {
                defs.Add(OldPerceptionJsonFactory.Convert(this, def.id, def));
            }

            var top = new JObject
            {
                ["version"] = version,
                ["metric_definitions"] = defs
            };
            var path = Path.Combine(m_DatasetPath, "metric_definitions.json");
            WriteJTokenToFile(path, top);
        }

        void WriteSensorsFile()
        {
            var sub = new JArray();
            foreach (var sensor in m_SensorMap)
            {
                sub.Add(JToken.FromObject(sensor.Value, Serializer));
            }
            var top = new JObject
            {
                ["version"] = version,
                ["sensors"] = sub
            };
            var path = Path.Combine(m_DatasetPath, "sensors.json");
            WriteJTokenToFile(path, top);
        }

        JToken ToJtoken(Metric metric)
        {
            string sensorId = null;
            string annotationId = null;
            string defId = null;

            if (!string.IsNullOrEmpty(metric.sensorId))
            {
                sensorId = m_SensorMap[metric.sensorId].id;
            }

            if (!string.IsNullOrEmpty(metric.annotationId))
            {
                annotationId = m_RegisteredAnnotations[metric.annotationId].id;
            }

            if (m_RegisteredMetrics.TryGetValue(metric.Id, out var def))
            {
                defId = def.id;
            }

            return new JObject
            {
                ["capture_id"] =  sensorId,
                ["annotation_id"] = annotationId,
                ["sequence_id"] = metric.sequenceId.ToString(),
                ["step"] = metric.step,
                ["metric_definition"] = defId,
                ["values"] = JToken.FromObject(metric.Values)
            };
        }

        void WriteMetricsFile(int index, IEnumerable<JToken> metrics)
        {
            var top = new MetricsJson
            {
                version = version,
                metrics = metrics
            };

            var path = Path.Combine(m_DatasetPath, $"metrics_{index:000}.json");
            WriteJTokenToFile(path, top);
        }

        int m_MetricOutCount = 0;
        List<JToken> m_MetricsReady = new List<JToken>();
        void AddMetricToReport(Metric metric)
        {
            m_MetricsReady.Add(ToJtoken(metric));
            WriteMetrics();
        }


        void WriteCaptureFile(int index, IEnumerable<PerceptionCapture> captures)
        {
            var top = new PerceptionJson
            {
                version = version,
                captures = captures
            };

            var path = Path.Combine(m_DatasetPath, $"captures_{index:000}.json");
            WriteJTokenToFile(path, top);
        }

        [Serializable]
        struct PerceptionJson
        {
            public string version;
            public IEnumerable<PerceptionCapture> captures;
        }

        [Serializable]
        struct MetricsJson
        {
            public string version;
            public IEnumerable<JToken> metrics;
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [Serializable]
        struct PerceptionCapture
        {
            public string id;
            public string sequence_id;
            public int step;
            public float timestamp;
            public PerceptionRgbSensor sensor;
            public string filename;
            public string format;
            public JArray annotations;
        }

        public static float[][] ToFloatArray(float3x3 inF3)
        {
            return new[]
            {
                new [] { inF3[0][0], inF3[0][1], inF3[0][2] },
                new [] { inF3[1][0], inF3[1][1], inF3[1][2] },
                new [] { inF3[2][0], inF3[2][1], inF3[2][2] }
            };
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [Serializable]
        struct PerceptionRgbSensor
        {
            public string sensor_id;
            public string modality;
            public Vector3 translation;
            public Vector3 rotation;
            public Vector3 velocity;
            public Vector3 acceleration;
            public float[][] camera_intrinsic;
            public string projection;

            public static PerceptionRgbSensor Convert(OldPerceptionConsumer consumer, RgbSensor inRgb, string path)
            {
                return new PerceptionRgbSensor
                {
                    sensor_id = inRgb.Id,
                    modality = inRgb.sensorType,
                    translation = inRgb.position,
                    rotation = inRgb.rotation,
                    velocity = inRgb.velocity,
                    acceleration = inRgb.acceleration,
                    projection = inRgb.projection,
                    camera_intrinsic = ToFloatArray(inRgb.intrinsics)
                };
            }
        }
    }
}
