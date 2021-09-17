using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Simulation;
using UnityEditor;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.Perception.GroundTruth.Exporters.Solo;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

#if HDRP_PRESENT
using UnityEngine.Rendering.HighDefinition;
#endif

namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    /// Labeler which generates a semantic segmentation image each frame. Each object is rendered to the semantic segmentation
    /// image using the color associated with it based on the given <see cref="SemanticSegmentationLabelConfig"/>.
    /// Semantic segmentation images are saved to the dataset in PNG format.
    /// Only one SemanticSegmentationLabeler can render at once across all cameras.
    /// </summary>
    [Serializable]
    public sealed class SemanticSegmentationLabeler : CameraLabeler, IOverlayPanelProvider
    {
        [Serializable]
        public class SemanticSegmentationDefinition : AnnotationDefinition
        {
            static readonly string k_Id = "semantic segmentation";
            static readonly string k_Description = "Generates a semantic segmentation image for each captured frame. " +
                "Each object is rendered to the semantic segmentation image using the color associated with it based on " +
                "this labeler's associated semantic segmentation label configuration. Semantic segmentation images are saved " +
                "to the dataset in PNG format. Please note that only one SemanticSegmentationLabeler can render at once across all cameras.";
            static readonly string k_AnnotationType = "semantic segmentation";

            public IEnumerable<DefinitionEntry> spec;

            public SemanticSegmentationDefinition() : base(k_Id, k_Description, k_AnnotationType) { }

            public SemanticSegmentationDefinition(IEnumerable<DefinitionEntry> spec)
                : base(k_Id, k_Description, k_AnnotationType)
            {
                this.spec = spec;
            }

            public struct DefinitionEntry : IMessageProducer
            {
                public DefinitionEntry(string name, Color pixelValue)
                {
                    labelName = name;
                    this.pixelValue = pixelValue;
                }

                public string labelName;
                public Color pixelValue;
                public void ToMessage(IMessageBuilder builder)
                {
                    builder.AddString("label_name", labelName);
                    builder.AddIntVector("pixel_value", Utils.ToIntVector(pixelValue));
                }
            }
        }

        SemanticSegmentationDefinition m_AnnotationDefinition;

        [Serializable]
        public class SemanticSegmentation : Annotation
        {
            public IEnumerable<SemanticSegmentationDefinition.DefinitionEntry> instances;
            public string imageFormat;
            public Vector2 dimension;
            public byte[] buffer;

            public override void ToMessage(IMessageBuilder builder)
            {
                base.ToMessage(builder);
                builder.AddString("image_format", imageFormat);
                builder.AddFloatVector("dimension", new[] { dimension.x, dimension.y });
                builder.AddPngImage("semantic_segmentation", buffer);
                var nested = builder.AddNestedMessage("instances");
                foreach (var i in instances)
                {
                    i.ToMessage(nested);
                }
            }
        }

        public static string annotationId = "semantic segmentation";

        /// <summary>
        /// The SemanticSegmentationLabelConfig which maps labels to pixel values.
        /// </summary>
        public SemanticSegmentationLabelConfig labelConfig;

        /// <summary>
        /// Event information for <see cref="SemanticSegmentationLabeler.imageReadback"/>
        /// </summary>
        public struct ImageReadbackEventArgs
        {
            /// <summary>
            /// The <see cref="Time.frameCount"/> on which the image was rendered. This may be multiple frames in the past.
            /// </summary>
            public int frameCount;
            /// <summary>
            /// Color pixel data.
            /// </summary>
            public NativeArray<Color32> data;
            /// <summary>
            /// The source image texture.
            /// </summary>
            public RenderTexture sourceTexture;
        }

        public struct SegmentationValue
        {
            public int frame;
        }

        /// <summary>
        /// Event which is called each frame a semantic segmentation image is read back from the GPU.
        /// </summary>
        public event Action<ImageReadbackEventArgs> imageReadback;

        /// <summary>
        /// The RenderTexture on which semantic segmentation images are drawn. Will be resized on startup to match
        /// the camera resolution.
        /// </summary>
        public RenderTexture targetTexture => m_TargetTextureOverride;

        /// <inheritdoc cref="IOverlayPanelProvider"/>
        public Texture overlayImage=> targetTexture;

        /// <inheritdoc cref="IOverlayPanelProvider"/>
        public string label => "SemanticSegmentation";

        [Tooltip("(Optional) The RenderTexture on which semantic segmentation images will be drawn. Will be reformatted on startup.")]
        [SerializeField]
        RenderTexture m_TargetTextureOverride;

//        AnnotationDefinition m_SemanticSegmentationAnnotationDefinition;
        RenderTextureReader<Color32> m_SemanticSegmentationTextureReader;

#if HDRP_PRESENT
        SemanticSegmentationPass m_SemanticSegmentationPass;
        LensDistortionPass m_LensDistortionPass;
    #elif URP_PRESENT
        SemanticSegmentationUrpPass m_SemanticSegmentationPass;
        LensDistortionUrpPass m_LensDistortionPass;
    #endif

        Dictionary<int, AsyncAnnotationFuture> m_AsyncAnnotations;

        /// <summary>
        /// Creates a new SemanticSegmentationLabeler. Be sure to assign <see cref="labelConfig"/> before adding to a <see cref="PerceptionCamera"/>.
        /// </summary>
        public SemanticSegmentationLabeler() { }

        /// <summary>
        /// Creates a new SemanticSegmentationLabeler with the given <see cref="SemanticSegmentationLabelConfig"/>.
        /// </summary>
        /// <param name="labelConfig">The label config associating labels with colors.</param>
        /// <param name="targetTextureOverride">Override the target texture of the labeler. Will be reformatted on startup.</param>
        public SemanticSegmentationLabeler(SemanticSegmentationLabelConfig labelConfig, RenderTexture targetTextureOverride = null)
        {
            this.labelConfig = labelConfig;
            m_TargetTextureOverride = targetTextureOverride;
        }

        struct AsyncSemanticSegmentationWrite
        {
            public AsyncAnnotationFuture future;
            public NativeArray<Color32> data;
            public int width;
            public int height;
        }

        public override string description
        {
            get => string.Empty;
            protected set { }
        }

        /// <inheritdoc/>
        protected override bool supportsVisualization => true;

        /// <inheritdoc/>
        protected override void Setup()
        {
            var myCamera = perceptionCamera.GetComponent<Camera>();
            var camWidth = myCamera.pixelWidth;
            var camHeight = myCamera.pixelHeight;

            if (labelConfig == null)
            {
                throw new InvalidOperationException(
                    "SemanticSegmentationLabeler's LabelConfig must be assigned");
            }

            m_AsyncAnnotations = new Dictionary<int, AsyncAnnotationFuture>();

            if (targetTexture != null)
            {
                if (targetTexture.sRGB)
                {
                    Debug.LogError("targetTexture supplied to SemanticSegmentationLabeler must be in Linear mode. Disabling labeler.");
                    enabled = false;
                }
                var renderTextureDescriptor = new RenderTextureDescriptor(camWidth, camHeight, GraphicsFormat.R8G8B8A8_UNorm, 8);
                targetTexture.descriptor = renderTextureDescriptor;
            }
            else
                m_TargetTextureOverride = new RenderTexture(camWidth, camHeight, 8, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            targetTexture.Create();
            targetTexture.name = "Labeling";

#if HDRP_PRESENT
            var gameObject = perceptionCamera.gameObject;
            var customPassVolume = gameObject.GetComponent<CustomPassVolume>() ?? gameObject.AddComponent<CustomPassVolume>();
            customPassVolume.injectionPoint = CustomPassInjectionPoint.BeforeRendering;
            customPassVolume.isGlobal = true;
            m_SemanticSegmentationPass = new SemanticSegmentationPass(myCamera, targetTexture, labelConfig)
            {
                name = "Labeling Pass"
            };
            customPassVolume.customPasses.Add(m_SemanticSegmentationPass);

            m_LensDistortionPass = new LensDistortionPass(myCamera, targetTexture)
            {
                name = "Lens Distortion Pass"
            };
            customPassVolume.customPasses.Add(m_LensDistortionPass);
#elif URP_PRESENT
            // Semantic Segmentation
            m_SemanticSegmentationPass = new SemanticSegmentationUrpPass(myCamera, targetTexture, labelConfig);
            perceptionCamera.AddScriptableRenderPass(m_SemanticSegmentationPass);

            // Lens Distortion

            m_LensDistortionPass = new LensDistortionUrpPass(myCamera, targetTexture);
            perceptionCamera.AddScriptableRenderPass(m_LensDistortionPass);
#endif
            var specs = labelConfig.labelEntries.Select(l => new SemanticSegmentationDefinition.DefinitionEntry
            {
                labelName = l.label,
                pixelValue = l.color
            });

            if (labelConfig.skyColor != Color.black)
            {
                specs = specs.Append(new SemanticSegmentationDefinition.DefinitionEntry
                {
                    labelName = "sky",
                    pixelValue = labelConfig.skyColor
                });
            }

            m_AnnotationDefinition = new SemanticSegmentationDefinition(specs);
            DatasetCapture.RegisterAnnotationDefinition(m_AnnotationDefinition);

            m_SemanticSegmentationTextureReader = new RenderTextureReader<Color32>(targetTexture);
            visualizationEnabled = supportsVisualization;
        }

        void OnSemanticSegmentationImageRead(int frameCount, NativeArray<Color32> data)
        {
            if (!m_AsyncAnnotations.TryGetValue(frameCount, out var future))
                return;

            m_AsyncAnnotations.Remove(frameCount);

            var asyncRequest = Manager.Instance.CreateRequest<AsyncRequest<AsyncSemanticSegmentationWrite>>();

            imageReadback?.Invoke(new ImageReadbackEventArgs
            {
                data = data,
                frameCount = frameCount,
                sourceTexture = targetTexture
            });
            asyncRequest.data = new AsyncSemanticSegmentationWrite
            {
                future = future,
                data = new NativeArray<Color32>(data, Allocator.Persistent),
                width = targetTexture.width,
                height = targetTexture.height,
            };
            asyncRequest.Enqueue((r) =>
            {
                Profiler.BeginSample("Encode");
                var pngBytes = ImageConversion.EncodeArrayToPNG(r.data.data.ToArray(), GraphicsFormat.R8G8B8A8_UNorm, (uint)r.data.width, (uint)r.data.height);
                Profiler.EndSample();

                var toReport = new SemanticSegmentation
                {
                    instances = m_AnnotationDefinition.spec,
                    sensorId = perceptionCamera.ID,
                    Id = m_AnnotationDefinition.id,
                    annotationType = m_AnnotationDefinition.annotationType,
                    description = m_AnnotationDefinition.description,
                    imageFormat = "png",
                    dimension = new Vector2(r.data.width, r.data.height),
                    buffer = pngBytes
                };

                r.data.future.Report(toReport);

                r.data.data.Dispose();
                return AsyncRequest.Result.Completed;
            });
            asyncRequest.Execute();
        }

        /// <inheritdoc/>
        protected override void OnEndRendering(ScriptableRenderContext scriptableRenderContext)
        {
            m_AsyncAnnotations[Time.frameCount] = perceptionCamera.SensorHandle.ReportAnnotationAsync(m_AnnotationDefinition);
            m_SemanticSegmentationTextureReader.Capture(scriptableRenderContext,
                (frameCount, data, renderTexture) => OnSemanticSegmentationImageRead(frameCount, data));
        }

        /// <inheritdoc/>
        protected override void Cleanup()
        {
            m_SemanticSegmentationTextureReader?.WaitForAllImages();
            m_SemanticSegmentationTextureReader?.Dispose();
            m_SemanticSegmentationTextureReader = null;

            if (m_TargetTextureOverride != null)
                m_TargetTextureOverride.Release();

            m_TargetTextureOverride = null;
        }
    }
}
