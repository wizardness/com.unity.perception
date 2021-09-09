using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Unity.Collections;
using Unity.Profiling;
using Unity.Simulation;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Perception.GroundTruth.SoloDesign;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    ///  Produces instance segmentation for each frame.
    /// </summary>
    [Serializable]
    public sealed class InstanceSegmentationLabeler : CameraLabeler, IOverlayPanelProvider
    {
        InstanceSegmentationDefinition m_Definition = new InstanceSegmentationDefinition();

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

        Dictionary<int, (AsyncAnnotation annotation, byte[] buffer)> m_AsyncData;
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
#if false
        struct AsyncWrite
        {
            public NativeArray<Color32> data;
            public int width;
            public int height;
            public string path;
        }
#endif
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
            if (!m_AsyncData.TryGetValue(frame, out var asyncData))
                return;

            m_AsyncData.Remove(frame);

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

                var toReport = new InstanceSegmentation
                {
                    sensorId = perceptionCamera.ID,
                    Id = m_Definition.id,
                    annotationType = m_Definition.annotationType,
                    description = m_Definition.description,
                    imageFormat = "png",
                    instances = instances,
                    dimension = new Vector2(Screen.width, Screen.height), // TODO figure out how to get this from the camera
                    buffer = asyncData.buffer
                };

                asyncData.annotation.Report(toReport);
            }
        }

        void OnImageCaptured(int frameCount, NativeArray<Color32> data, RenderTexture renderTexture)
        {
            if (!m_AsyncData.TryGetValue(frameCount, out var annotation))
                return;

            using (s_OnImageReceivedCallback.Auto())
            {
                m_CurrentTexture = renderTexture;

//                m_InstancePath = $"{k_Directory}/{k_FilePrefix}{frameCount}.png";
//                var localPath = $"{Manager.Instance.GetDirectoryFor(k_Directory)}/{k_FilePrefix}{frameCount}.png";

                var colors = new NativeArray<Color32>(data, Allocator.Persistent);
#if false
                var asyncRequest = Manager.Instance.CreateRequest<AsyncRequest<AsyncWrite>>();

                asyncRequest.data = new AsyncWrite
                {
                    data = colors,
                    width = renderTexture.width,
                    height = renderTexture.height,
                    path = localPath
                };

                asyncRequest.Enqueue(r =>
                {
                    Profiler.BeginSample("InstanceSegmentationEncode");
                    var pngEncoded = ImageConversion.EncodeArrayToPNG(r.data.data.ToArray(), GraphicsFormat.R8G8B8A8_UNorm, (uint)r.data.width, (uint)r.data.height);
                    Profiler.EndSample();
                    Profiler.BeginSample("InstanceSegmentationWritePng");
                    File.WriteAllBytes(r.data.path, pngEncoded);
                    Manager.Instance.ConsumerFileProduced(r.data.path);
                    Profiler.EndSample();
                    r.data.data.Dispose();
                    return AsyncRequest.Result.Completed;
                });
                asyncRequest.Execute();
#endif
                annotation.Item2 = ImageConversion.EncodeArrayToPNG(colors.ToArray(), GraphicsFormat.R8G8B8A8_UNorm, (uint)renderTexture.width, (uint)renderTexture.height);
//                Profiler.EndSample();
//                Profiler.BeginSample("InstanceSegmentationWritePng");
//                File.WriteAllBytes(localPath, annotation.Item2);
//                Manager.Instance.ConsumerFileProduced(localPath);
//                Profiler.EndSample();
                colors.Dispose();

                m_AsyncData[frameCount] = annotation;
            }
        }

        /// <inheritdoc/>
        protected override void OnBeginRendering(ScriptableRenderContext scriptableRenderContext)
        {
            m_AsyncData[Time.frameCount] = (perceptionCamera.SensorHandle.ReportAnnotationAsync(m_Definition), null);
        }

        /// <inheritdoc/>
        protected override void Setup()
        {
            if (idLabelConfig == null)
                throw new InvalidOperationException("InstanceSegmentationLabeler's idLabelConfig field must be assigned");

            m_InstanceData = new List<InstanceData>();

            perceptionCamera.InstanceSegmentationImageReadback += OnImageCaptured;
            perceptionCamera.RenderedObjectInfosCalculated += OnRenderedObjectInfosCalculated;

            m_AsyncData = new Dictionary<int, (AsyncAnnotation, byte[])>();

            visualizationEnabled = supportsVisualization;
        }
    }
}
