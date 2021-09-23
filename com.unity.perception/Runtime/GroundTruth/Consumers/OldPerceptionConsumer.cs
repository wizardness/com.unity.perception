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
                objectType == typeof(Color32))
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

    public class OldPerceptionConsumer : ConsumerEndpoint
    {
        static readonly string version = "0.1.1";

        public string baseDirectory = "D:/PerceptionOutput/KickinItOldSchool";
        public int capturesPerFile = 20;

        internal JsonSerializer Serializer { get; }= new JsonSerializer { ContractResolver = new PerceptionResolver()};

        //JsonSerializer m_JsonSerializer = new JsonSerializer();
        string m_CurrentPath;
        string m_DatasetPath;
        string m_RgbPath;
        string m_LogsPath;

        [Serializable]
        struct SensorInfo
        {
            public Guid id;
            public string modality;
            public string description;
        }

        Dictionary<string, SensorInfo> m_SensorMap = new Dictionary<string, SensorInfo>();
        Dictionary<string, (Guid, AnnotationDefinition)> m_RegisteredAnnotations = new Dictionary<string, (Guid, AnnotationDefinition)>();

        Dictionary<int, Guid> m_SequenceToGuidMap = new Dictionary<int, Guid>();
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

            m_RegisteredAnnotations[annotationDefinition.id] = (Guid.NewGuid(), annotationDefinition);
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
                id = Guid.NewGuid(),
                modality = sensor.modality,
                description = sensor.definition
            };
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
            if (!m_SequenceToGuidMap.TryGetValue(frame.sequence, out var seqId))
            {
                seqId = Guid.NewGuid();
                m_SequenceToGuidMap[frame.sequence] = seqId;
            }

            // Only support one image file right now
            var path = "";

            RgbSensor rgbSensor = null;
            if (frame.sensors.Count() == 1)
            {
                var sensor = frame.sensors.First();
                if (sensor is RgbSensor rgb)
                {
                    rgbSensor = rgb;
                    path = WriteOutImageFile(frame.frame, rgb);
                }
            }
#if false // TODO bring back annotations....
            var annotations = new JArray();
            foreach (var annotation in frame.annotations)
            {
                var labelerId = Guid.NewGuid(); // TODO - we need to get this figured out

                if (!m_RegisteredAnnotations.TryGetValue(annotation.Id, out var def))
                {
                    def.Item1 = Guid.Empty;
                }

                var defId = def.Item1;
                var json = OldPerceptionJsonFactory.Convert(this, frame, labelerId, defId, annotation);
                if (json != null) annotations.Add(json);
            }
#endif
            var capture = new PerceptionCapture
            {
                id = Guid.NewGuid(),
                filename = path,
                format = "PNG",
                sequence_id = seqId,
                step = frame.step,
                timestamp = frame.timestamp,
                sensor = PerceptionRgbSensor.Convert(this, rgbSensor, path),
//                annotations = annotations
            };

            m_CurrentCaptures.Add(capture);

            if (m_CurrentCaptures.Count >= capturesPerFile)
            {
                var toRemove = m_CurrentCaptures;
                m_CurrentCaptures = new List<PerceptionCapture>();
                // Write out a capture file
                WriteCaptureFile(m_CurrentCaptureIndex++, toRemove);
                toRemove.Clear();
            }
        }

        public override void OnSimulationCompleted(CompletionMetadata metadata)
        {
            WriteSensorsFile();
            WriteEgosFile();
            WriteAnnotationsDefinitionsFile();
            WriteMetricsDefinitionsFile();
        }

        int m_CurrentCaptureIndex = 1;

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

            foreach (var (id, def) in m_RegisteredAnnotations.Values)
            {
                defs.Add(OldPerceptionJsonFactory.Convert(this, id, def));
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
            var top = new JObject
            {
                ["version"] = version,
                ["metric_definitions"] = new JArray()
            };
            var path = Path.Combine(m_DatasetPath, "metric_definitions.json");
            WriteJTokenToFile(path, top);
        }

        void WriteEgosFile()
        {
            var top = new JObject
            {
                ["version"] = version,
                ["egos"] = new JArray()
            };
            var path = Path.Combine(m_DatasetPath, "egos.json");
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

        void WriteCaptureFile(int index, IEnumerable<PerceptionCapture> captures)
        {
            var top = new PerceptionJson
            {
                version = version,
                captures = captures
            };

            var path = Path.Combine(m_DatasetPath, $"captures_{index}.json");
            WriteJTokenToFile(path, top);
        }

        public Guid GetIdForSensor(Sensor inSensor)
        {
            if (!m_SensorMap.TryGetValue(inSensor.Id, out var info))
            {
                Debug.LogError("Sensor Id was not available, it should have already been registered");
                return Guid.Empty;
            }

            return info.id;
        }

        [Serializable]
        struct PerceptionJson
        {
            public string version;
            public IEnumerable<PerceptionCapture> captures;
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [Serializable]
        struct PerceptionCapture
        {
            public Guid id;
            public Guid sequence_id;
            public int step;
            public float timestamp;
            public string filename;
            public string format;
            public PerceptionRgbSensor sensor;
            public JArray annotations;
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [Serializable]
        struct PerceptionRgbSensor
        {
            public Guid sensor_id;
            public string modality;
            public Vector3 translation;
            public Vector3 rotation;
            public Vector3 velocity;
            public Vector3 acceleration;

            public static PerceptionRgbSensor Convert(OldPerceptionConsumer consumer, RgbSensor inRgb, string path)
            {
                return new PerceptionRgbSensor
                {
                    sensor_id = consumer.GetIdForSensor(inRgb),
                    modality = inRgb.sensorType,
                    translation = inRgb.position,
                    rotation = inRgb.rotation,
                    velocity = inRgb.velocity,
                    acceleration = inRgb.acceleration
                };
            }
        }
    }
}
