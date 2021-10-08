using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Unity.Collections;
using Unity.Profiling;
using Unity.Simulation;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.Perception.GroundTruth.Exporters.Solo;
using UnityEngine.Rendering;

namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    ///  Produces instance segmentation for each frame.
    /// </summary>
    [Serializable]
    public sealed class InstanceSegmentationLabeler : CameraLabeler, IOverlayPanelProvider
    {
        [Serializable]
        public class InstanceSegmentationDefinition : AnnotationDefinition
        {
            static readonly string k_Id = "instance segmentation";
            static readonly string k_Description = "You know the deal";
            static readonly string k_AnnotationType = "instance segmentation";

            public InstanceSegmentationDefinition(IdLabelConfig.LabelEntrySpec[] spec)
                : base(k_Id, k_Description, k_AnnotationType)
            {
                this.spec = spec;
            }

            public IdLabelConfig.LabelEntrySpec[] spec;

            public override void ToMessage(IMessageBuilder builder)
            {
                base.ToMessage(builder);
                foreach (var e in spec)
                {
                    var nested = builder.AddNestedMessageToVector("spec");
                    // TODO figure out how to generically write the spec to a message builder
                }
            }
        }

        /// <summary>
        /// The instance segmentation image recorded for a capture. This
        /// includes the data that associates a pixel color to an object.
        /// </summary>
        [Serializable]
        public class InstanceSegmentation : Annotation
        {
            public struct Entry
            {
                /// <summary>
                /// The instance ID associated with a pixel color
                /// </summary>
                public int instanceId;
                /// <summary>
                /// The color (rgba) value
                /// </summary>
                public Color32 rgba;

                internal void ToMessage(IMessageBuilder builder)
                {
                    builder.AddInt("instance_id", instanceId);
                    builder.AddIntVector("rgba", new[] { (int)rgba.r, (int)rgba.g, (int)rgba.b, (int)rgba.a });
                }
            }

            /// <summary>
            /// This instance to pixel map
            /// </summary>
            public List<Entry> instances;

            // The format of the image type
            public string imageFormat;

            // The dimensions (width, height) of the image
            public Vector2 dimension;

            // The raw bytes of the image file
            public byte[] buffer;

            public override void ToMessage(IMessageBuilder builder)
            {
                base.ToMessage(builder);
                builder.AddString("image_format", imageFormat);
                builder.AddFloatVector("dimension", new[] { dimension.x, dimension.y });
                builder.AddPngImage("instance_segmentation", buffer);

                foreach (var e in instances)
                {
                    var nested = builder.AddNestedMessageToVector("instances");
                    e.ToMessage(nested);
                }
            }
        }

        InstanceSegmentationDefinition m_Definition;

        ///<inheritdoc/>
        public override string description
        {
            get => "Produces an instance segmentation image for each frame. The image will render the pixels of each labeled object in a distinct color.";
            protected set { }
        }

        /// <inheritdoc/>
        protected override bool supportsVisualization => true;

        /// <summary>
        /// The GUID to associate with annotations produced by this labeler.
        /// </summary>
        [Tooltip("The id to associate with instance segmentation annotations in the dataset.")]
        public string annotationId = "instance segmentation";

        /// <summary>
        /// The <see cref="idLabelConfig"/> which associates objects with labels.
        /// </summary>
        public IdLabelConfig idLabelConfig;

        static ProfilerMarker s_OnObjectInfoReceivedCallback = new ProfilerMarker("OnInstanceSegmentationObjectInformationReceived");
        static ProfilerMarker s_OnImageReceivedCallback = new ProfilerMarker("OnInstanceSegmentationImagesReceived");

        Texture m_CurrentTexture;

        /// <inheritdoc cref="IOverlayPanelProvider"/>
        // ReSharper disable once ConvertToAutoPropertyWithPrivateSetter
        public Texture overlayImage => m_CurrentTexture;

        /// <inheritdoc cref="IOverlayPanelProvider"/>
        public string label => "InstanceSegmentation";

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [SuppressMessage("ReSharper", "NotAccessedField.Local")]
        public struct ColorValue
        {
            public uint instance_id;
            public Color32 color;
        }

        public struct InstanceData
        {
            public byte[] buffer;
            public List<ColorValue> colors;
        }

        string m_InstancePath;
        List<InstanceData> m_InstanceData;

        Dictionary<int, AsyncFuture<Annotation>> m_PendingFutures;
        ConcurrentDictionary<int, List<InstanceSegmentation.Entry>> m_PendingEntries;
        ConcurrentDictionary<int, byte[]> m_PendingEncodedImages;

        bool ReportFrameIfReady(int frame)
        {
            lock (m_PendingEntries)
            {
                var entriesReady = m_PendingEntries.ContainsKey(frame);
                var imgReady = m_PendingEncodedImages.ContainsKey(frame);

                if (!entriesReady || !imgReady) return false;

                if (!m_PendingEntries.TryRemove(frame, out var entries))
                {
                    throw new InvalidOperationException($"Could not remove entries for {frame} although it said it was ready");
                }

                if (!m_PendingEncodedImages.TryRemove(frame, out var img))
                {
                    throw new InvalidOperationException($"Could not remove encoded image for {frame} although it said it was ready");
                }

                var toReport = new InstanceSegmentation
                {
                    sensorId = perceptionCamera.ID,
                    Id = m_Definition.id,
                    annotationType = m_Definition.annotationType,
                    description = m_Definition.description,
                    imageFormat = "png",
                    instances = entries,
                    dimension = new Vector2(Screen.width, Screen.height), // TODO figure out how to get this from the camera
                    buffer = img
                };

                if (!m_PendingFutures.TryGetValue(frame, out var future))
                {
                    throw new InvalidOperationException($"Could not get future for {frame}");
                }

                future.Report(toReport);

                m_PendingFutures.Remove(frame);

                return true;
            }
        }
        struct AsyncWrite
        {
            public int frame;
            public NativeArray<Color32> data;
            public int width;
            public int height;
        }

        /// <summary>
        /// Creates a new InstanceSegmentationLabeler. Be sure to assign <see cref="idLabelConfig"/> before adding to a <see cref="PerceptionCamera"/>.
        /// </summary>
        public InstanceSegmentationLabeler() { }

        /// <summary>
        /// Creates a new InstanceSegmentationLabeler with the given <see cref="IdLabelConfig"/>.
        /// </summary>
        /// <param name="labelConfig">The label config for resolving the label for each object.</param>
        public InstanceSegmentationLabeler(IdLabelConfig labelConfig)
        {
            this.idLabelConfig = labelConfig;
        }

        void OnRenderedObjectInfosCalculated(int frame, NativeArray<RenderedObjectInfo> renderedObjectInfos)
        {
            using (s_OnObjectInfoReceivedCallback.Auto())
            {
                m_InstanceData.Clear();

                var instances = new List<InstanceSegmentation.Entry>();

                foreach (var objectInfo in renderedObjectInfos)
                {
                    if (!idLabelConfig.TryGetLabelEntryFromInstanceId(objectInfo.instanceId, out var labelEntry))
                        continue;

                    instances.Add(new InstanceSegmentation.Entry
                    {
                        instanceId = (int)objectInfo.instanceId,
                        rgba = objectInfo.instanceColor
                    });
                }

                if (!m_PendingEntries.TryAdd(frame, instances))
                {
                    throw new InvalidOperationException($"Could not add instances for {frame}");
                }

                ReportFrameIfReady(frame);
            }
        }

        void OnImageCaptured(int frameCount, NativeArray<Color32> data, RenderTexture renderTexture)
        {
            using (s_OnImageReceivedCallback.Auto())
            {
                m_CurrentTexture = renderTexture;

                var colors = new NativeArray<Color32>(data, Allocator.Persistent);

                var asyncRequest = Manager.Instance.CreateRequest<AsyncRequest<AsyncWrite>>();

                asyncRequest.data = new AsyncWrite
                {
                    frame = frameCount,
                    data = colors,
                    width = renderTexture.width,
                    height = renderTexture.height,
                };

                asyncRequest.Enqueue(r =>
                {
                    var buffer = ImageConversion.EncodeArrayToPNG(r.data.data.ToArray(), GraphicsFormat.R8G8B8A8_UNorm, (uint)r.data.width, (uint)r.data.height);
                    r.data.data.Dispose();

                    if (!m_PendingEncodedImages.TryAdd(r.data.frame, buffer))
                    {
                        throw new InvalidOperationException("Could not add encoded png to pending encoded images");
                    }

                    ReportFrameIfReady(r.data.frame);
                    return AsyncRequest.Result.Completed;
                });
                asyncRequest.Execute();
            }
        }

        /// <inheritdoc/>
        protected override void OnBeginRendering(ScriptableRenderContext scriptableRenderContext)
        {
            m_PendingFutures[Time.frameCount] = perceptionCamera.SensorHandle.ReportAnnotationAsync(m_Definition);
        }

        /// <inheritdoc/>
        protected override void Setup()
        {
            if (idLabelConfig == null)
                throw new InvalidOperationException("InstanceSegmentationLabeler's idLabelConfig field must be assigned");

            m_Definition = new InstanceSegmentationDefinition(idLabelConfig.GetAnnotationSpecification());
            DatasetCapture.Instance.RegisterAnnotationDefinition(m_Definition);

            m_InstanceData = new List<InstanceData>();

            perceptionCamera.InstanceSegmentationImageReadback += OnImageCaptured;
            perceptionCamera.RenderedObjectInfosCalculated += OnRenderedObjectInfosCalculated;

            m_PendingFutures = new Dictionary<int, AsyncFuture<Annotation>>();
            m_PendingEntries = new ConcurrentDictionary<int, List<InstanceSegmentation.Entry>>();
            m_PendingEncodedImages = new ConcurrentDictionary<int, byte[]>();

            visualizationEnabled = supportsVisualization;
        }
    }
}
