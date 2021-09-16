using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Simulation;

namespace UnityEngine.Perception.GroundTruth.Exporters.PerceptionFormat
{
    public class PerceptionExporter : IDatasetExporter
    {
        const Formatting k_Formatting = Formatting.Indented;
        string outputDirectory = string.Empty;

        public string GetRgbCaptureFilename(params(string, object)[] additionalSensorValues)
        {
            return string.Empty;
        }

        public void OnSimulationBegin(string directoryName)
        {
            Debug.Log($"SS - Perception - OnSimBegin");
            outputDirectory = directoryName;
        }

        public void OnSimulationEnd()
        {
            Debug.Log($"SS - Perception - OnSimEnd");
            // do nothing :-)
        }

        public void OnAnnotationRegistered<TSpec>(Guid annotationId, TSpec[] values)
        {
            // do nothing :-)
        }

        public static void WriteJObjectToFile(JObject jObject, string directory, string filename)
        {
            var stringWriter = new StringWriter(new StringBuilder(256), CultureInfo.InvariantCulture);
            using (var jsonTextWriter = new JsonTextWriter(stringWriter))
            {
                jsonTextWriter.Formatting = k_Formatting;
                jObject.WriteTo(jsonTextWriter);
            }

            var contents = stringWriter.ToString();

            var path = Path.Combine(directory, filename);
            File.WriteAllText(path, contents);

            // TODO what to do about this...
            Manager.Instance.ConsumerFileProduced(path);
        }

        public Task ProcessPendingCaptures(List<SimulationState.PendingCapture> pendingCaptures, SimulationState simState, int captureFileIndex)
        {
            //lazily allocate for fast zero-write frames
            var capturesJArray = new JArray();

            foreach (var pendingCapture in pendingCaptures)
                capturesJArray.Add(JObjectFromPendingCapture(pendingCapture));

            var capturesJObject = new JObject();
            capturesJObject.Add("version", DatasetCapture.SchemaVersion);
            capturesJObject.Add("captures", capturesJArray);

            Debug.Log("SS - perception - writing");

            WriteJObjectToFile(capturesJObject, outputDirectory, $"captures_{captureFileIndex:000}.json");

            // TODO what to do about this...
            return null;
        }

        public Task OnCaptureReported(int frame, int width, int height, string filename)
        {
            // do nothing :-)
            return null;
        }

        public static JToken JObjectFromPendingCapture(SimulationState.PendingCapture pendingCapture)
        {
            var sensorJObject = new JObject();//new SensorCaptureJson
            sensorJObject["sensor_id"] = pendingCapture.SensorHandle.Id.ToString();
            sensorJObject["ego_id"] = pendingCapture.SensorData.egoHandle.Id.ToString();
            sensorJObject["modality"] = pendingCapture.SensorData.modality;
            sensorJObject["translation"] = DatasetJsonUtility.ToJToken(pendingCapture.SensorSpatialData.SensorPose.position);
            sensorJObject["rotation"] = DatasetJsonUtility.ToJToken(pendingCapture.SensorSpatialData.SensorPose.rotation);

            if (pendingCapture.AdditionalSensorValues != null)
            {
                foreach (var(name, value) in pendingCapture.AdditionalSensorValues)
                    sensorJObject.Add(name, DatasetJsonUtility.ToJToken(value));
            }

            var egoCaptureJson = new JObject();
            egoCaptureJson["ego_id"] = pendingCapture.SensorData.egoHandle.Id.ToString();
            egoCaptureJson["translation"] = DatasetJsonUtility.ToJToken(pendingCapture.SensorSpatialData.EgoPose.position);
            egoCaptureJson["rotation"] = DatasetJsonUtility.ToJToken(pendingCapture.SensorSpatialData.EgoPose.rotation);
            egoCaptureJson["velocity"] = pendingCapture.SensorSpatialData.EgoVelocity.HasValue ? DatasetJsonUtility.ToJToken(pendingCapture.SensorSpatialData.EgoVelocity.Value) : null;
            egoCaptureJson["acceleration"] = pendingCapture.SensorSpatialData.EgoAcceleration.HasValue ? DatasetJsonUtility.ToJToken(pendingCapture.SensorSpatialData.EgoAcceleration.Value) : null;

            var capture = new JObject();
            capture["id"] = pendingCapture.Id.ToString();
            capture["sequence_id"] = pendingCapture.SequenceId.ToString();
            capture["step"] = pendingCapture.Step;
            capture["timestamp"] = pendingCapture.Timestamp;
            capture["sensor"] = sensorJObject;
            capture["ego"] = egoCaptureJson;
            capture["filename"] = pendingCapture.Path;
            capture["format"] = GetFormatFromFilename(pendingCapture.Path);

            if (pendingCapture.Annotations.Any())
                capture["annotations"] = new JArray(pendingCapture.Annotations.Select(JObjectFromAnnotation).ToArray());

            return capture;
        }

        public static JObject JObjectFromAnnotation((Annotation, SimulationState.AnnotationData) annotationInfo)
        {
            var annotationJObject = new JObject();
            annotationJObject["id"] = annotationInfo.Item1.Id.ToString();
            annotationJObject["annotation_definition"] = annotationInfo.Item2.AnnotationDefinition.Id.ToString();
            if (annotationInfo.Item2.Path != null)
                annotationJObject["filename"] = annotationInfo.Item2.Path;

            if (annotationInfo.Item2.ValuesJson != null)
                annotationJObject["values"] = annotationInfo.Item2.ValuesJson;

            return annotationJObject;
        }

        static string GetFormatFromFilename(string filename)
        {
            var ext = Path.GetExtension(filename);
            if (ext == null)
                return null;

            if (ext.StartsWith("."))
                ext = ext.Substring(1);

            return ext.ToUpperInvariant();
        }
    }
}
