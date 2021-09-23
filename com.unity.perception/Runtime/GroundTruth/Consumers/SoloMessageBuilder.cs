using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.Perception.GroundTruth.Exporters.Solo;

namespace UnityEngine.Perception.GroundTruth.Consumers
{
    public class SoloMessageBuilder : ConsumerEndpoint
    {
        public string _baseDirectory = "D:/PerceptionOutput/SoloMessageBuilder";
        public string soloDatasetName = "solo_mb";
        static string currentDirectory = "";

        protected override bool IsComplete()
        {
            return true;
        }

        SimulationMetadata m_CurrentMetadata;

        public override void OnSimulationStarted(SimulationMetadata metadata)
        {
            Debug.Log("SC - On Simulation Started");
            m_CurrentMetadata = metadata;

            if (!Directory.Exists((_baseDirectory)))
                Directory.CreateDirectory(_baseDirectory);

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

        JToken m_FrameToken = null;
        Stack<JToken> m_Tokens = new Stack<JToken>();

        public override void OnFrameGenerated(Frame frame)
        {
            if (m_FrameToken == null)
            {
                m_FrameToken = new JObject();
                m_Tokens.Push(m_FrameToken);
            }
            else
            {
                // do something here to write out previous frame
            }

            var msg = new FrameMessageBuilder();
            FrameMessageBuilder.currentFrame = frame;

            frame.ToMessage(msg);

            // write out current
            var path = GetSequenceDirectoryPath(frame);
            path = Path.Combine(path, $"step{frame.step}.frame_data.json");
            WriteJTokenToFile(path, msg.ToJson());
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

        public override void OnSimulationCompleted(CompletionMetadata metadata)
        {
            Debug.Log("SC - On Simulation Completed");
        }

        class FrameMessageBuilder : IMessageBuilder
        {
            JToken m_Current = new JObject();
            Dictionary<string, FrameMessageBuilder> m_NestedValue = new Dictionary<string, FrameMessageBuilder>();
            Dictionary<string, List<FrameMessageBuilder>> m_NestedArrays = new Dictionary<string, List<FrameMessageBuilder>>();
            public static Frame currentFrame = null;

            public JToken ToJson()
            {
                foreach (var n in m_NestedValue)
                {
                    m_Current[n.Key] = n.Value.ToJson();
                }

                foreach (var n in m_NestedArrays)
                {
                    var jArray = new JArray();
                    foreach (var o in n.Value)
                    {
                       jArray.Add(o.ToJson());
                    }

                    m_Current[n.Key] = jArray;
                }
                return m_Current;
            }

            public void AddInt(string label, int value)
            {
                m_Current[label] = value;
            }

            public void AddIntVector(string label, int[] values)
            {
                m_Current[label] = new JArray(values);
            }

            public void AddFloat(string label, float value)
            {
                m_Current[label] = value;
            }

            public void AddFloatVector(string label, float[] values)
            {
                m_Current[label] = new JArray(values);
            }

            public void AddString(string label, string value)
            {
                m_Current[label] = value;
            }

            public void AddStringVector(string label, object[] values)
            {
                m_Current[label] = new JArray(values);
            }

            public void AddBoolean(string label, bool value)
            {
                m_Current[label] = value;
            }

            public void AddBooleanVector(string label, bool[] values)
            {
                m_Current[label] = new JArray(values);
            }

            // Right now, just for png images
            public void AddPngImage(string label, byte[] value)
            {
                // write out the file
                var path = GetSequenceDirectoryPath(currentFrame);
                path = Path.Combine(path, $"step{currentFrame.step}.{label}.png");
                var file = File.Create(path, 4096);
                file.Write(value, 0, value.Length);
                file.Close();

                // Add the filename to the json
                m_Current["filename"] = path;
            }

            public IMessageBuilder AddNestedMessage(string label)
            {
                var nested = new FrameMessageBuilder();
                m_NestedValue[label] = nested;
                return nested;
            }

            public IMessageBuilder AddNestedMessageToVector(string arrayLabel)
            {
                if (!m_NestedArrays.TryGetValue(arrayLabel, out var nestedList))
                {
                    nestedList = new List<FrameMessageBuilder>();
                    m_NestedArrays[arrayLabel] = nestedList;
                }
                var nested = new FrameMessageBuilder();
                nestedList.Add(nested);
                return nested;
            }
        }
    }
}
