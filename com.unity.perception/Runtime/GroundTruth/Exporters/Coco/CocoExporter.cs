using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Simulation;

namespace UnityEngine.Perception.GroundTruth.Exporters.Coco
{
    public class CocoExporter : IDatasetExporter
    {
        bool m_PrettyPrint = true;

        bool m_ReportingObjectDetection;
        bool m_ReportingKeypoints;

        bool m_Initialized;
        string m_DirectoryName = string.Empty;

        string m_RgbCaptureFilename;
        StreamWriter m_RgbCaptureStream;

        string m_ObjectDetectionFilename;
        StreamWriter m_ObjectDetectionStream;
        Task m_ObjectDetectionWritingTask;

        string m_KeypointFilename;
        StreamWriter m_KeypointDetectionStream;
        Task m_KeypointDetectionWritingTask;

        CocoTypes.ObjectDetectionCategories m_ObjectDetectionCategories;
        string m_ObjectDetectionCategoryFilename;
        StreamWriter m_ObjectDetectionCategoryStream;
        Task m_ObjectDetectionCategoryWritingTask;

        CocoTypes.KeypointCategories m_KeypointCategories;
        string m_KeypointCategoryFilename;
        StreamWriter m_KeypointCategoryStream;
        Task m_KeypointCategoryWritingTask;

        Guid m_SessionGuid;

        public string GetRgbCaptureFilename(params(string, object)[] additionalSensorValues)
        {
            return string.Empty;
        }

        public void OnSimulationBegin(string directoryName)
        {
            Debug.Log($"SS - COCO - OnSimBegin");
            m_DirectoryName = directoryName;
            m_DataCaptured = false;
        }

        async Task AwaitAllWrites()
        {
            Debug.Log("SS - coco - writing");

            WriteOutCategories();

            if (m_ObjectDetectionWritingTask != null)
            {
                await m_ObjectDetectionWritingTask;
                await m_ObjectDetectionStream.WriteAsync("]");
            }

            if (m_KeypointDetectionWritingTask != null)
            {
                await m_KeypointDetectionWritingTask;
                await m_KeypointDetectionStream.WriteAsync("]");
            }

            if (m_RgbCaptureWritingTask != null)
            {
                await m_RgbCaptureWritingTask;
                await m_RgbCaptureStream.WriteAsync("]");
            }

            if (m_ObjectDetectionCategoryWritingTask != null)
            {
                await m_ObjectDetectionCategoryWritingTask;
            }

            if (m_KeypointCategoryWritingTask != null)
            {
                await m_KeypointCategoryWritingTask;
            }

            m_RgbCaptureStream?.Close();
            m_ObjectDetectionStream?.Close();
            m_ObjectDetectionCategoryStream?.Close();
            m_KeypointDetectionStream?.Close();
            m_KeypointCategoryStream?.Close();
        }


        public async void OnSimulationEnd()
        {
            Debug.Log($"SS - COCO - OnSimEnd");
            if (!m_DataCaptured) return;

            await AwaitAllWrites();

            if (m_ReportingObjectDetection)
            {
                await WriteObjectDetectionFile();
            }

            if (m_ReportingKeypoints)
            {
                await WriteKeypointFile();
            }

            File.Delete(m_RgbCaptureFilename);

            m_Initialized = false;

        }

        void InitializeCaptureFiles()
        {
            if (m_Initialized) return;

            if (!Directory.Exists(m_DirectoryName))
                Directory.CreateDirectory(m_DirectoryName);

            m_SessionGuid = Guid.NewGuid();

            //var prefix = m_DirectoryName + Path.DirectorySeparatorChar + m_SessionGuid;


            m_RgbCaptureFilename = Path.Combine(m_DirectoryName, m_SessionGuid + "_coco_captures.json");
            m_RgbCaptureStream = File.CreateText(m_RgbCaptureFilename);

            m_ObjectDetectionFilename = Path.Combine(m_DirectoryName, m_SessionGuid + "_coco_box_annotations.json");
            if (m_ReportingObjectDetection)
            {
                m_ObjectDetectionStream = File.CreateText(m_ObjectDetectionFilename);
                m_ObjectDetectionCategoryFilename = Path.Combine(m_DirectoryName, m_SessionGuid + "_coco_obj_detection_categories.json");
            }

            m_KeypointFilename = Path.Combine(m_DirectoryName, m_SessionGuid + "_coco_keypoint_annotations.json");
            if (m_ReportingKeypoints)
            {
                m_KeypointDetectionStream = File.CreateText(m_KeypointFilename);
                m_KeypointCategoryFilename = Path.Combine(m_DirectoryName, m_SessionGuid + "_coco_keypoint_categories.json");
            }

            m_Initialized = true;
        }

        static void AggregateFile(string filename, StringBuilder aggregated, bool skipFirstCharacter = false)
        {
            var sr = new StreamReader(filename);

            var length = (int)sr.BaseStream.Length;
            var start = 0;

            if (length == 0) return;

            var buffer = new char[length];
            sr.Read(buffer, start, length);

            if (skipFirstCharacter)
            {
                length--;
                start++;
            }

            aggregated.Append(buffer, start, length);

            sr.Dispose();
        }

        async Task WriteObjectDetectionFile()
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.Append("{");

            // Create the json header
            CreateHeaderInfo(stringBuilder);
            stringBuilder.Append(",");

            CreateLicenseInfo(stringBuilder);

            // Read in the contents of the captures
            stringBuilder.Append(",\"images\":");
            AggregateFile(m_RgbCaptureFilename, stringBuilder);

            // Read in the contents of the object detection
            stringBuilder.Append(",\"annotations\":");
            AggregateFile(m_ObjectDetectionFilename, stringBuilder);
            stringBuilder.Append(",");

            // Read in the contents of the object detection categories
            AggregateFile(m_ObjectDetectionCategoryFilename, stringBuilder, true);

            var json = stringBuilder.ToString();

            if (m_PrettyPrint)
            {
                json = JToken.Parse(json).ToString(Formatting.Indented);
            }

            Debug.Log($"SS - COCO - writing to path: {m_DirectoryName}, file: coco_object_detection_annotations.json");

            // Write out the files
            var filename = Path.Combine(m_DirectoryName, "coco_object_detection_annotations.json");

            Debug.Log($"SS - COCO - file: {filename}");


            var cocoStream = File.CreateText(filename);
            await cocoStream.WriteAsync(json);
            cocoStream.Close();

            Manager.Instance.ConsumerFileProduced(filename);

            File.Delete(m_ObjectDetectionFilename);
            File.Delete(m_ObjectDetectionCategoryFilename);
        }

        async Task WriteKeypointFile()
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.Append("{");

            // Create the json header
            CreateHeaderInfo(stringBuilder);
            stringBuilder.Append(",");

            CreateLicenseInfo(stringBuilder);

            // Read in the contents of the captures
            stringBuilder.Append(",\"images\":");
            AggregateFile(m_RgbCaptureFilename, stringBuilder);

            // Read in the contents of the object detection
            stringBuilder.Append(",\"annotations\":");
            AggregateFile(m_KeypointFilename, stringBuilder);
            stringBuilder.Append(",");

            // Read in the contents of the object detection categories
            AggregateFile(m_KeypointCategoryFilename, stringBuilder, true);

            var json = stringBuilder.ToString();

            if (m_PrettyPrint)
            {
                json = JToken.Parse(json).ToString(Formatting.Indented);
            }

            // Write out the files
            var filename = Path.Combine(m_DirectoryName, "coco_keypoint_annotations.json");
            var cocoStream = File.CreateText(filename);
            await cocoStream.WriteAsync(json);
            cocoStream.Dispose();

            Manager.Instance.ConsumerFileProduced(filename);

            File.Delete(m_KeypointFilename);
            File.Delete(m_KeypointCategoryFilename);
        }

        bool m_DataCaptured;

        void WriteOutCategories()
        {
            // 3 cases
            //   1) Just have object detection
            //   2) Just have keypoint detection
            //   3) Have both, which includes writing out object detection in object detection coco output
            //      and merging the object detection & keypoints and writing out that data in the keypoint
            //      coco output data
            if (m_ReportingObjectDetection)
            {
                m_ObjectDetectionCategoryStream = File.CreateText(m_ObjectDetectionCategoryFilename);
                var json = JsonUtility.ToJson(m_ObjectDetectionCategories);
                m_ObjectDetectionCategoryWritingTask = m_ObjectDetectionCategoryStream.WriteAsync(json);
            }

            if (m_ReportingKeypoints)
            {
                // TODO - revisit this, but right now we are going to add the keypoint info to all of the categories,
                // we can get away with this because we only support one set of keypoint definitions per simulation
                // currently.

                if (m_KeypointCategories.categories.Length < 1) return;

                m_KeypointCategoryStream = File.CreateText(m_KeypointCategoryFilename);

                var merged = m_KeypointCategories;

                if (m_ReportingObjectDetection)
                {
                    merged = new CocoTypes.KeypointCategories
                    {
                        categories = new CocoTypes.KeypointCategory[m_ObjectDetectionCategories.categories.Count]
                    };

                    var i = 0;
                    foreach (var odc in m_ObjectDetectionCategories.categories)
                    {
                        merged.categories[i++] = MergeCategories(odc, m_KeypointCategories.categories[0]);
                    }
                }

#if false
                var json = JsonUtility.ToJson(merged);
#else
                var builder = new StringBuilder();
                var sw = new StringWriter(builder);

                var serializer = new JsonSerializer();

                using (var writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, merged);
                }

                var json = builder.ToString();
#endif
                m_KeypointCategoryWritingTask = m_KeypointCategoryStream.WriteAsync(json);
            }
        }

        public void OnAnnotationRegistered<TSpec>(Guid annotationId, TSpec[] values)
        {
            if (annotationId.ToString() == BoundingBox2DLabeler.annotationIdForCocoExport)
            {
                m_ReportingObjectDetection = true;
                m_ObjectDetectionCategories = new CocoTypes.ObjectDetectionCategories
                {
                    categories = new List<CocoTypes.ObjectDetectionCategory>()
                };

                foreach (var value in values)
                {
                    if (value is IdLabelConfig.LabelEntrySpec spec)
                    {
                        var rec = new CocoTypes.ObjectDetectionCategory
                        {
                            id = spec.label_id,
                            name = spec.label_name,
                            supercategory = spec.label_name
                        };

                        m_ObjectDetectionCategories.categories.Add(rec);
                    }
                }
            }

            if (annotationId.ToString() == KeypointLabeler.annotationIdForCocoExport)
            {
                m_ReportingKeypoints = true;
                var categories = new CocoTypes.KeypointCategory[values.Length];
                var i = 0;

                foreach (var value in values)
                {
                    if (value is KeypointLabeler.KeypointJson keypointJson)
                    {
                        categories[i++] = AnnotationHandler.ToKeypointCategory(keypointJson);
                    }
                }

                m_KeypointCategories = new CocoTypes.KeypointCategories
                {
                    categories = categories
                };
            }
        }


        static string versionEntry = "0.0.1";
        static string descriptionEntry = "Description of dataset";
        static string contributorEntry = "Anonymous";
        static string urlEntry = "Not Set";

        static void CreateHeaderInfo(StringBuilder stringBuilder)
        {
            stringBuilder.Append("\"info\":");

            var dateTime = DateTime.Today;
            var info = new CocoTypes.Info
            {
                year = int.Parse(dateTime.ToString("yyyy")),
                version = versionEntry,
                description = descriptionEntry,
                contributor = contributorEntry,
                url = urlEntry,
                date_created = DateTime.Today.ToString("D")
            };
            stringBuilder.Append(JsonUtility.ToJson(info));
        }

        static void CreateLicenseInfo(StringBuilder stringBuilder)
        {
            var licenses = new CocoTypes.Licenses
            {
                licenses = new[]
                {
                    new CocoTypes.License
                    {
                        id = 0,
                        name = "No License",
                        url = "Not Set"
                    }
                }
            };

            var tmpJson = JsonUtility.ToJson(licenses);

            // Remove the start and end '{' from the licenses json
            stringBuilder.Append(tmpJson.Substring(1, tmpJson.Length - 2));
        }

        bool m_FirstBoxAnnotation = true;
        bool m_FirstKeypointAnnotation = true;

        bool m_FirstCapture = true;
        Task m_RgbCaptureWritingTask;

        static CocoTypes.KeypointCategory MergeCategories(CocoTypes.ObjectDetectionCategory od, CocoTypes.KeypointCategory kp)
        {
            return new CocoTypes.KeypointCategory
            {
                id = od.id,
                name = od.name,
                supercategory = od.supercategory,
                keypoints = kp.keypoints,
                skeleton = kp.skeleton
            };
        }

        public async Task ProcessPendingCaptures(List<SimulationState.PendingCapture> pendingCaptures, SimulationState simState, int captureFileIndex)
        {
            var boxJson = string.Empty;
            var keypointJson = string.Empty;

            try
            {
                foreach (var cap in pendingCaptures)
                {
                    var boxes = new Dictionary<int, CocoTypes.ObjectDetectionAnnotation>();

                    foreach (var annotation in cap.Annotations)
                    {
                        if (annotation.Item2.RawValues == null)
                            continue;

                        var tmp = ProcessBoundingBoxAnnotations(annotation.Item2.RawValues);

                        foreach (var box in tmp.Values)
                        {
                            boxes[box.id] = box;

                            if (m_FirstBoxAnnotation)
                            {
                                boxJson = "[";
                                m_FirstBoxAnnotation = false;
                            }
                            else
                                boxJson += ",";

                            boxJson += JsonUtility.ToJson(box);
                        }

                    }

                    foreach (var annotation in cap.Annotations)
                    {
                        if (annotation.Item2.RawValues == null)
                            continue;

                        var keypoints = ProcessKeypointAnnotations(annotation.Item2.RawValues, boxes);

                        foreach (var kp in keypoints.Values)
                        {
                            if (m_FirstKeypointAnnotation)
                            {
                                keypointJson = "[";
                                m_FirstKeypointAnnotation = false;
                            }
                            else
                                keypointJson += ",";

                            var builder = new StringBuilder();
                            var stringWriter = new StringWriter(builder);

                            var serializer = new JsonSerializer();

                            using (var writer = new JsonTextWriter(stringWriter))
                            {
                                serializer.Serialize(writer, kp);
                            }

                            keypointJson += builder.ToString();

                        }
                    }
                }

                if (m_ObjectDetectionWritingTask != null)
                    await m_ObjectDetectionWritingTask;

                if (boxJson != string.Empty)
                {
                    InitializeCaptureFiles();
                    m_ObjectDetectionWritingTask = m_ObjectDetectionStream.WriteAsync(boxJson);
                }

                if (m_KeypointDetectionWritingTask != null)
                    await m_KeypointDetectionWritingTask;

                if (keypointJson != string.Empty)
                {
                    InitializeCaptureFiles();
                    m_KeypointDetectionWritingTask = m_KeypointDetectionStream.WriteAsync(keypointJson);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static Dictionary<int, CocoTypes.ObjectDetectionAnnotation> ProcessBoundingBoxAnnotations(IEnumerable<object> annotations)
        {
            var map = new Dictionary<int, CocoTypes.ObjectDetectionAnnotation>();
            foreach (var annotation in annotations)
            {
                if (annotation is BoundingBox2DLabeler.BoundingBoxValue bbox)
                {
                    var coco = CocoTypes.ObjectDetectionAnnotation.FromBoundingBoxValue(bbox);
                    map[coco.id] = coco;
                }
            }

            return map;
        }

        static Dictionary<int, CocoTypes.KeypointAnnotation> ProcessKeypointAnnotations(IEnumerable<object> annotations, Dictionary<int, CocoTypes.ObjectDetectionAnnotation> boundingBoxes)
        {
            var map = new Dictionary<int, CocoTypes.KeypointAnnotation>();

            foreach (var annotation in annotations)
            {
                if (annotation is KeypointLabeler.KeypointEntry keypoint)
                {
                    var coco = CocoTypes.KeypointAnnotation.FromKeypointValue(keypoint);
                    if (boundingBoxes.ContainsKey(coco.id))
                    {
                        coco.CopyObjectDetectionData(boundingBoxes[coco.id]);
                    }

                    map[coco.id] = coco;
                }
            }

            return map;
        }

        public async Task OnCaptureReported(int frame, int width, int height, string filename)
        {
            InitializeCaptureFiles();

            var json = string.Empty;

            var converted = AnnotationHandler.HandleCameraCapture(frame, width, height, filename);
            if (m_FirstCapture)
            {
                json = "[";
                m_FirstCapture = false;
            }
            else
                json += ",";

            json += JsonUtility.ToJson(converted);

            if (m_RgbCaptureWritingTask != null)
                await m_RgbCaptureWritingTask;

            if (json != string.Empty)
                m_RgbCaptureWritingTask = m_RgbCaptureStream.WriteAsync(json);

            m_DataCaptured = true;
        }
    }
}
