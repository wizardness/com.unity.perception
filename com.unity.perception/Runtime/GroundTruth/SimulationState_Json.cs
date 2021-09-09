using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using Unity.Simulation;
using UnityEngine.Perception.GroundTruth.SoloDesign;

// ReSharper disable NotAccessedField.Local
// ReSharper disable CoVariantArrayConversion

namespace UnityEngine.Perception.GroundTruth
{
    public partial class SimulationState
    {
        const Formatting k_Formatting = Formatting.Indented;

        /// <summary>
        /// Writes sensors.json, egos.json, metric_definitions.json, and annotation_definitions.json
        /// </summary>
        public void WriteReferences()
        {
            var egoReference = new JObject();
            egoReference["version"] = DatasetCapture.SchemaVersion;

            WriteJObjectToFile(egoReference, "egos.json");

            var sensorReferenceDoc = new JObject();
            sensorReferenceDoc["version"] = DatasetCapture.SchemaVersion;
            sensorReferenceDoc["sensors"] = new JArray(m_Sensors.Select(kvp =>
            {
                var sensorReference = new JObject();
                sensorReference["id"] = kvp.Key.Id.ToString();
                sensorReference["modality"] = kvp.Value.modality;
                if (kvp.Value.description != null)
                    sensorReference["description"] = kvp.Value.description;

                return sensorReference;
            }).ToArray());
            WriteJObjectToFile(sensorReferenceDoc, "sensors.json");

            if (m_AdditionalInfoTypeData.Count > 0)
            {
                var annotationDefinitionsJArray = new JArray();
                var metricDefinitionsJArray = new JArray();
                foreach (var typeInfo in m_AdditionalInfoTypeData)
                {
                    var typeJObject = new JObject();
                    typeJObject.Add("id", new JValue(typeInfo.id.ToString()));
                    typeJObject.Add("name", new JValue(typeInfo.name));
                    if (typeInfo.description != null)
                        typeJObject.Add("description", new JValue(typeInfo.description));

                    if (typeInfo.format != null)
                        typeJObject.Add("format", new JValue(typeInfo.format));

                    if (typeInfo.specValues != null)
                    {
                        var specValues = new JArray();
                        foreach (var value in typeInfo.specValues)
                        {
                            specValues.Add(DatasetJsonUtility.ToJToken(value));
                        }
                        typeJObject.Add("spec", specValues);
                    }

                    switch (typeInfo.additionalInfoKind)
                    {
                        case AdditionalInfoKind.Annotation:
                            annotationDefinitionsJArray.Add(typeJObject);
                            break;
                        case AdditionalInfoKind.Metric:
                            metricDefinitionsJArray.Add(typeJObject);
                            break;
                        default:
                            throw new NotSupportedException("Unsupported info kind");
                    }
                }

                if (annotationDefinitionsJArray.Count > 0)
                {
                    var annotationDefinitionsJObject = new JObject();
                    annotationDefinitionsJObject.Add("version", DatasetCapture.SchemaVersion);
                    annotationDefinitionsJObject.Add("annotation_definitions", annotationDefinitionsJArray);
                    WriteJObjectToFile(annotationDefinitionsJObject, "annotation_definitions.json");
                }

                if (metricDefinitionsJArray.Count > 0)
                {
                    var metricDefinitionsJObject = new JObject();
                    metricDefinitionsJObject.Add("version", DatasetCapture.SchemaVersion);
                    metricDefinitionsJObject.Add("metric_definitions", metricDefinitionsJArray);
                    WriteJObjectToFile(metricDefinitionsJObject, "metric_definitions.json");
                }
            }
//            Debug.Log($"Dataset written to {Path.GetDirectoryName(OutputDirectory)}");
        }

        void WriteJObjectToFile(JObject jObject, string filename)
        {
#if false
            m_JsonToStringSampler.Begin();
            var stringWriter = new StringWriter(new StringBuilder(256), CultureInfo.InvariantCulture);
            using (var jsonTextWriter = new JsonTextWriter(stringWriter))
            {
                jsonTextWriter.Formatting = k_Formatting;
                jObject.WriteTo(jsonTextWriter);
            }

            var contents = stringWriter.ToString();
            m_JsonToStringSampler.End();

            m_WriteToDiskSampler.Begin();

            var path = Path.Combine(OutputDirectory, filename);
//            Debug.Log($"ss - sensors.json - {path}");
            File.WriteAllText(path, contents);
            Manager.Instance.ConsumerFileProduced(path);
            m_WriteToDiskSampler.End();
#endif
        }

        int m_currentReportedSequence = 0;
        Dictionary<int, int> m_SequenceMap = new Dictionary<int, int>();

        Sensor ToSensor(PendingCapture pendingCapture, SimulationState simulationState, int captureFileIndex)
        {
            var sensor = new RgbSensor
            {
                Id = "camera",
                sensorType = "camera",
                description = "this is the description of the sensor",
                position = Vector3.zero,
                rotation = Vector3.zero,
                velocity = Vector3.zero,
                acceleration = Vector3.zero,
//                metadata = new Dictionary<string, object>(),
                imageFormat = "png",
                dimension = Vector2.zero,
                buffer = null
            };

            return sensor;
        }

        Frame ToFrame(PendingCapture pendingCapture, SimulationState simulationState, int captureFileIndex)
        {
            if (!m_SequenceMap.TryGetValue(pendingCapture.SequenceId, out var seqId))
            {
                seqId = m_currentReportedSequence++;
                m_SequenceMap[pendingCapture.SequenceId] = seqId;
            }

            return new Frame(pendingCapture.FrameCount, seqId, pendingCapture.Step);
        }




        void WritePendingCaptures(bool flush = false, bool writeCapturesFromThisFrame = false)
        {
//            if (!flush && m_PendingCaptures.Count < k_MinPendingCapturesBeforeWrite)
//                return;

            m_SerializeCapturesSampler.Begin();

            var pendingCapturesToWrite = new List<PendingCapture>(m_PendingCaptures.Count);
            var frameCountNow = Time.frameCount;
            for (var i = 0; i < m_PendingCaptures.Count; i++)
            {
                var pendingCapture = m_PendingCaptures[i];
                if ((writeCapturesFromThisFrame || pendingCapture.FrameCount < frameCountNow) &&
                    pendingCapture.Annotations.All(a => a.Item2 != null))
                {
                    pendingCapturesToWrite.Add(pendingCapture);
                    m_PendingCaptures.RemoveAt(i);
                    i--; //decrement i because we removed an element
                }
            }

            if (pendingCapturesToWrite.Count == 0)
            {
                m_SerializeCapturesSampler.End();
                return;
            }
#if false
            BoundingBoxAnnotation ToBoundingBox(Annotation annotation, AnnotationData data)
            {
                var bbox = new BoundingBoxAnnotation
                {
                    Id = "bounding box",
                    sensorId = "camera",
                    description = "Labeled bounding boxes",
                    annotationType = "bounding box labeler",
//                    metadata = new Dictionary<string, object>(),
                    boxes = new List<BoundingBoxAnnotation.Entry>()
                };

                foreach (var d in data.RawValues)
                {
                    if (d is BoundingBox2DLabeler.BoundingBoxValue e)
                    {
                        var entry = new BoundingBoxAnnotation.Entry
                        {
                            instanceId = (int)e.instance_id,
                            labelId = e.label_id,
                            labelName = e.label_name,
                            origin = new Vector2{x = e.x, y = e.y},
                            dimension = new Vector2{x = e.width, y = e.height}
                        };

                        bbox.boxes.Add(entry);
                    }
                }

                return bbox;
            }
#endif
#if false
            InstanceSegmentation ToInstanceSegmentation(AnnotationHandle annotation, AnnotationData data, params(string,object)[] sensorValues)
            {
                var seg = new InstanceSegmentation
                {
                    Id = "instance segmentation",
                    sensorId = "camera",
                    description = "instance segmentation blah blah blah",
                    annotationType = "instance segmentation labeler",
//                    metadata = new Dictionary<string, object>(),
                    instances = new List<InstanceSegmentation.Entry>(),
                    dimension = Vector2.zero,
                    imageFormat = "png"
                };

                foreach (var sv in sensorValues)
                {
                    switch (sv.Item1)
                    {
                        case "camera_width":
                            seg.dimension.x = (int)sv.Item2;
                            break;
                        case "camera_height":
                            seg.dimension.y = (int)sv.Item2;
                            break;
                    }
                }


                foreach (var d in data.RawValues)
                {
                    if (d is InstanceSegmentationLabeler.InstanceData i)
                    {
                        seg.buffer = i.buffer;

                        foreach (var color in i.colors)
                        {
                            var entry = new InstanceSegmentation.Entry
                            {
                                instanceId = (int)color.instance_id,
                                rgba = color.color
                            };

                            seg.instances.Add(entry);
                        }

                    }
                }

                return seg;
            }
#endif
#if true
            List<Sensor> ConvertToSensors(PendingCapture capture, SimulationState simulationState)
            {
                var dim = new Vector2();
                var buffer = new byte[0];

                foreach (var sv in capture.AdditionalSensorValues)
                {
                    switch (sv.Item1)
                    {
                        case "camera_width":
                            dim.x = (int)sv.Item2;
                            break;
                        case "camera_height":
                            dim.y = (int)sv.Item2;
                            break;
                        case "buffer":
                            buffer = (byte[])sv.Item2;
                            break;
                    }
                }

                return new List<Sensor>
                {
                    new RgbSensor
                    {
                        Id = "camera",
                        sensorType = capture.SensorData.modality,
                        imageFormat = ".png",
                        dimension = dim,
                        position = capture.SensorSpatialData.EgoPose.position,
                        rotation = capture.SensorSpatialData.EgoPose.rotation.eulerAngles,
                        velocity = capture.SensorSpatialData.EgoVelocity ?? Vector3.zero,
                        acceleration = capture.SensorSpatialData.EgoAcceleration ?? Vector3.zero,
                        buffer = buffer,
//                        metadata = new Dictionary<string, object>()
                    }
                };
            }
#endif
#if true
            Frame ConvertToFrameData(PendingCapture capture, SimulationState simState, int captureFileIndex)
            {
                if (!m_SequenceMap.TryGetValue(capture.SequenceId, out var seq))
                {
                    seq = m_currentReportedSequence++;
                    m_SequenceMap[capture.SequenceId] = seq;
                }

                var frame = new Frame(capture.FrameCount, seq, capture.Step);

                frame.sensors = ConvertToSensors(capture, simState);

                foreach (var (handle, annotation) in capture.Annotations)
                {
                    frame.annotations.Add(annotation);
                }

#if false
                foreach (var (annotation, data) in capture.Annotations)
                {
                    SoloDesign.Annotation soloAnnotation = null;
                    var supported = false;

                    switch (data.AnnotationDefinition.Id.ToString())
                    {
#if false
                        case "f9f22e05-443f-4602-a422-ebe4ea9b55cb":
                            soloAnnotation = ToBoundingBox(annotation, data);
                            supported = true;
                            break;
#endif
                        case "1ccebeb4-5886-41ff-8fe0-f911fa8cbcdf":
                            soloAnnotation = ToInstanceSegmentation(annotation, data, capture.AdditionalSensorValues);
                            supported = true;
                            break;
                    }

                    if (supported) frame.annotations.Add(soloAnnotation);

                }
#endif
                return frame;
            }
#endif
            void Write(List<PendingCapture> pendingCaptures, SimulationState simulationState, int captureFileIndex)
            {
                foreach (var pendingCapture in pendingCaptures)
                {
                    var frame = ConvertToFrameData(pendingCapture, simulationState, captureFileIndex);
                    GetActiveConsumer()?.OnFrameGenerated(frame);
                }

                //GetActiveReporter()?.ProcessPendingCaptures(pendingCaptures, simulationState);
#if false
                simulationState.m_SerializeCapturesAsyncSampler.Begin();

                //lazily allocate for fast zero-write frames
                var capturesJArray = new JArray();

                foreach (var pendingCapture in pendingCaptures)
                    capturesJArray.Add(JObjectFromPendingCapture(pendingCapture));

                var capturesJObject = new JObject();
                capturesJObject.Add("version", DatasetCapture.SchemaVersion);
                capturesJObject.Add("captures", capturesJArray);

                simulationState.WriteJObjectToFile(capturesJObject,
                    $"captures_{captureFileIndex:000}.json");
                simulationState.m_SerializeCapturesAsyncSampler.End();
#endif
            }

            if (flush)
            {
                Write(pendingCapturesToWrite, this, m_CaptureFileIndex);
            }
            else
            {
                var req = Manager.Instance.CreateRequest<AsyncRequest<WritePendingCaptureRequestData>>();
                req.data = new WritePendingCaptureRequestData()
                {
                    CaptureFileIndex = m_CaptureFileIndex,
                    PendingCaptures = pendingCapturesToWrite,
                    SimulationState = this
                };
                req.Enqueue(r =>
                {
                    Write(r.data.PendingCaptures, r.data.SimulationState, r.data.CaptureFileIndex);
                    return AsyncRequest.Result.Completed;
                });
                req.Execute(AsyncRequest.ExecutionContext.JobSystem);
            }

            m_SerializeCapturesSampler.End();
            m_CaptureFileIndex++;
        }
#if false
        struct WritePendingMetricRequestData
        {
            public List<PendingMetric> PendingMetrics;
            public int MetricFileIndex;
        }

        void WritePendingMetrics(bool flush = false)
        {
            if (!flush && m_PendingMetrics.Count < k_MinPendingMetricsBeforeWrite)
                return;

            var pendingMetricsToWrite = new List<PendingMetric>(m_PendingMetrics.Count);
            m_SerializeMetricsSampler.Begin();
            for (var i = 0; i < m_PendingMetrics.Count; i++)
            {
                var metric = m_PendingMetrics[i];
                if (metric.IsAssigned)
                {
                    pendingMetricsToWrite.Add(metric);
                    m_PendingMetrics.RemoveAt(i);
                    i--; //decrement i because we removed an element
                }
            }

            if (pendingMetricsToWrite.Count == 0)
            {
                m_SerializeMetricsSampler.End();
                return;
            }

            void Write(List<PendingMetric> pendingMetrics, SimulationState simState, int metricsFileIndex)
            {
#if false
                GetActiveReporter()?.ProcessPendingMetrics(pendingMetrics, simState);
#endif
#if false
                m_SerializeMetricsAsyncSampler.Begin();
                var jArray = new JArray();
                foreach (var pendingMetric in pendingMetrics)
                    jArray.Add(JObjectFromPendingMetric(pendingMetric));

                var metricsJObject = new JObject();
                metricsJObject.Add("version", DatasetCapture.SchemaVersion);
                metricsJObject.Add("metrics", jArray);

                WriteJObjectToFile(metricsJObject, $"metrics_{metricsFileIndex:000}.json");
                m_SerializeMetricsAsyncSampler.End();
#endif
            }

            if (flush)
            {
                Write(pendingMetricsToWrite, this, m_MetricsFileIndex);
            }
            else
            {
                var req = Manager.Instance.CreateRequest<AsyncRequest<WritePendingMetricRequestData>>();
                req.data = new WritePendingMetricRequestData()
                {
                    MetricFileIndex = m_MetricsFileIndex,
                    PendingMetrics = pendingMetricsToWrite
                };
                req.Enqueue(r =>
                {
                    Write(r.data.PendingMetrics, this, r.data.MetricFileIndex);
                    return AsyncRequest.Result.Completed;
                });
                req.Execute();
            }

            m_MetricsFileIndex++;
            m_SerializeMetricsSampler.End();
        }

        static JObject JObjectFromPendingMetric(PendingMetric metric)
        {
            var jObject = new JObject();
#if false
            jObject["capture_id"] = metric.CaptureId == Guid.Empty ? new JRaw("null") : new JValue(metric.CaptureId.ToString());
            jObject["annotation_id"] = metric.annotationHandle.IsNil ? new JRaw("null") : new JValue(metric.annotationHandle.Id.ToString());
            jObject["sequence_id"] = metric.SequenceId.ToString();
            jObject["step"] = metric.Step;
            jObject["metric_definition"] = metric.MetricDefinition.Id.ToString();
            jObject["values"] = metric.Values;
#endif
            return jObject;
        }
#endif
#if false
        /// <summary>
        /// Creates the json representation of the given PendingCapture. Static because this should not depend on any SimulationState members,
        /// which may have changed since the capture was reported.
        /// </summary>
        static JToken JObjectFromPendingCapture(PendingCapture pendingCapture)
        {
            var sensorJObject = new JObject();//new SensorCaptureJson
            sensorJObject["sensor_id"] = pendingCapture.SensorHandle.Id.ToString();
            sensorJObject["modality"] = pendingCapture.SensorData.modality;
            sensorJObject["translation"] = DatasetJsonUtility.ToJToken(pendingCapture.SensorSpatialData.SensorPose.position);
            sensorJObject["rotation"] = DatasetJsonUtility.ToJToken(pendingCapture.SensorSpatialData.SensorPose.rotation);

            if (pendingCapture.AdditionalSensorValues != null)
            {
                foreach (var(name, value) in pendingCapture.AdditionalSensorValues)
                    sensorJObject.Add(name, DatasetJsonUtility.ToJToken(value));
            }

            var egoCaptureJson = new JObject();
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

            if (pendingCapture.Annotations.Any())
                capture["annotations"] = new JArray(pendingCapture.Annotations.Select(JObjectFromAnnotation).ToArray());

            return capture;
        }
#endif
#if false
        static JObject JObjectFromAnnotation((AnnotationHandle, AnnotationData) annotationInfo)
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
#endif
        struct WritePendingCaptureRequestData
        {
            public List<PendingCapture> PendingCaptures;
            public int CaptureFileIndex;
            public SimulationState SimulationState;
        }
    }
}
