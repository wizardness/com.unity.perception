using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Simulation;
using UnityEngine.Perception.GroundTruth.Exporters.PerceptionFormat;

namespace UnityEngine.Perception.GroundTruth.Exporters.PerceptionNew
{
    public class PerceptionNewExporter : IDatasetExporter
    {
        public bool prettyPrint = true;

        string m_DirectoryName = string.Empty;

        int m_UnknownFrameCount = 0;

        public string GetRgbCaptureFilename(params(string, object)[] additionalSensorValues)
        {
            var frameArray = additionalSensorValues.Where(p => p.Item1 == "frame").Select(p => p.Item2);
            var frame = frameArray.Any() ? (int)frameArray.First() : m_UnknownFrameCount++;

            return Path.Combine(m_DirectoryName, $"rgb_{frame}.png");
        }

        public void OnSimulationBegin(string directoryName)
        {
            Debug.Log($"SS - New Perception - OnSimBegin");
            m_Metadata = new Metadata
            {
                version = "0.0.1",
                image_width = 0,
                image_height = 0,
                dataset_size = 0
            };

            m_DirectoryName = directoryName + Path.DirectorySeparatorChar + Guid.NewGuid() + Path.DirectorySeparatorChar;
            if (!Directory.Exists(m_DirectoryName))
                Directory.CreateDirectory(m_DirectoryName);
        }

        [Serializable]
        struct Metadata
        {
            public string version;
            public int image_width;
            public int image_height;
            public int dataset_size;
        }

        Metadata m_Metadata;

        public void OnSimulationEnd()
        {
            Debug.Log($"SS - New Perception - OnSimEnd");

            var writePath = Path.Combine(m_DirectoryName, "metadata.json");
            var file = File.CreateText(writePath);

            Debug.Log("SS - New Perception - writing");

            file.Write(JsonUtility.ToJson(m_Metadata, true));
            file.Close();

            Manager.Instance.ConsumerFileProduced(writePath);

            Task.WhenAll(m_PendingTasks);
        }

        public void OnAnnotationRegistered<TSpec>(Guid annotationId, TSpec[] values)
        {
            // Right now, do nothing :-)
        }

        static bool GetFilenameForAnnotation(object rawData, out string filename)
        {
            filename = string.Empty;
            if (rawData is BoundingBox2DLabeler.BoundingBoxValue bbox)
            {
                var frame = bbox.frame;
                filename = $"frame_{frame}_bounding_bocx_2d.json";
                return true;
            }

            return false;
        }

        // TODO - handle the 1000's of file writes we will be doing in a more intelligent fashion. Perhaps create a bg thread
        // that reads json records off of a queue and writes them out

        List<Task> m_PendingTasks = new List<Task>();

        public Task ProcessPendingCaptures(List<SimulationState.PendingCapture> pendingCaptures, SimulationState simState, int captureFileIndex)
        {
            foreach (var cap in pendingCaptures)
            {
                foreach (var (annotation, annotationData) in cap.Annotations)
                {
                    // Create a file for the annotation

#if false
                    if (annotationData.RawValues.Any())
                    {
                        var first = annotationData.RawValues.First();

                        if (GetFilenameForAnnotation(first, frame, out var filename))
                        {
                            var json = new StringBuilder("{");
                            json.Append(annotationData.ValuesJson);
                            json.Append("}");
                            m_PendingTasks.Add(AnnotationHandler.WriteOutJson(m_DirectoryName, filename, json.ToString()));
#if false
                            // Need to revisit this and handle this in a performant way
                            var jObject = PerceptionExporter.JObjectFromAnnotation((annotation, annotationData));
                            PerceptionExporter.WriteJObjectToFile(jObject, m_DirectoryName, filename);
#endif
                        }
                    }
#endif
                    foreach (var rawValue in annotationData.RawValues)
                    {

                        if (GetFilenameForAnnotation(rawValue, out var filename))
                        {
#if true
                            var json = new StringBuilder();
                            json.Append(annotationData.ValuesJson);
                            m_PendingTasks.Add(AnnotationHandler.WriteOutJson(m_DirectoryName, filename, json.ToString()));
#else
                            // Need to revisit this and handle this in a performant way
                            var jObject = PerceptionExporter.JObjectFromAnnotation((annotation, annotationData));
                            PerceptionExporter.WriteJObjectToFile(jObject, m_DirectoryName, filename);
#endif
                        }
                    }
                }
            }
            return Task.CompletedTask;
        }

        public Task OnCaptureReported(int frame, int width, int height, string filename)
        {
            m_Metadata.dataset_size++;
            m_Metadata.image_height = height;
            m_Metadata.image_width = width;
            return null;
        }
    }
}
