using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine.Serialization;
using Unity.Simulation;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.Perception.GroundTruth.Exporters.Solo;
using UnityEngine.Rendering;

namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    /// Produces 2d bounding box annotations for all visible objects each frame.
    /// </summary>
    [Serializable]
    public sealed class BoundingBox2DLabeler : CameraLabeler
    {
        public class BoundingBoxAnnotationDefinition : AnnotationDefinition
        {
            static readonly string k_Id = "bounding box";
            static readonly string k_Description = "Bounding box for each labeled object visible to the sensor";
            static readonly string k_AnnotationType = "bounding box";

            public BoundingBoxAnnotationDefinition() : base(k_Id, k_Description, k_AnnotationType) { }

            public BoundingBoxAnnotationDefinition(IEnumerable<DefinitionEntry> spec)
                : base(k_Id, k_Description, k_AnnotationType)
            {
                this.spec = spec;
            }

            [Serializable]
            public struct DefinitionEntry : IMessageProducer
            {
                public DefinitionEntry(int id, string name)
                {
                    labelId = id;
                    labelName = name;
                }

                public int labelId;
                public string labelName;
                public void ToMessage(IMessageBuilder builder)
                {
                    builder.AddInt("label_id", labelId);
                    builder.AddString("label_name", labelName);
                }
            }

            public IEnumerable<DefinitionEntry> spec;

            public override void ToMessage(IMessageBuilder builder)
            {
                base.ToMessage(builder);
                foreach (var e in spec)
                {
                    var nested = builder.AddNestedMessageToVector("spec");
                    e.ToMessage(nested);
                }
            }
        }

        BoundingBoxAnnotationDefinition m_AnnotationDefinition;

        /// <summary>
        /// Bounding boxes for all of the labeled objects in a capture
        /// </summary>
        [Serializable]
        public class BoundingBoxAnnotation : Annotation
        {
            public struct Entry
            {
                // The instance ID of the object
                public int instanceId;

                public int labelId;

                // The type of the object
                public string labelName;

                /// <summary>
                /// (xy) pixel location of the object's bounding box
                /// </summary>
                public Vector2 origin;
                /// <summary>
                /// (width/height) dimensions of the bounding box
                /// </summary>
                public Vector2 dimension;

                public void ToMessage(IMessageBuilder builder)
                {
                    builder.AddInt("instance_id", instanceId);
                    builder.AddInt("label_id", labelId);
                    builder.AddString("label_name", labelName);
                    builder.AddFloatVector("origin", new[] { origin.x, origin.y });
                    builder.AddFloatVector("dimension", new[] { dimension.x, dimension.y });
                }
            }

            /// <summary>
            /// The bounding boxes recorded by the annotator
            /// </summary>
            public List<Entry> boxes;

            public override void ToMessage(IMessageBuilder builder)
            {
                base.ToMessage(builder);
                foreach (var e in boxes)
                {
                    var nested = builder.AddNestedMessageToVector("values");
                    e.ToMessage(nested);
                }
            }
        }

        ///<inheritdoc/>
        public override string description
        {
            get => "Produces 2D bounding box annotations for all visible objects that bear a label defined in this labeler's associated label configuration.";
            protected set {}
        }

        static ProfilerMarker s_BoundingBoxCallback = new ProfilerMarker("OnBoundingBoxesReceived");

        /// <summary>
        /// The GUID id to associate with the annotations produced by this labeler.
        /// </summary>
        public static string annotationId = "bounding box";

        /// <summary>
        /// The <see cref="IdLabelConfig"/> which associates objects with labels.
        /// </summary>
        [FormerlySerializedAs("labelingConfiguration")]
        public IdLabelConfig idLabelConfig;

        Dictionary<int, (AsyncAnnotationFuture annotation, LabelEntryMatchCache labelEntryMatchCache)> m_AsyncData;
        List<BoundingBoxAnnotation.Entry> m_BoundingBoxValues;

        Vector2 m_OriginalScreenSize = Vector2.zero;

        Texture m_BoundingBoxTexture;
        Texture m_LabelTexture;
        GUIStyle m_Style;

        /// <summary>
        /// Creates a new BoundingBox2DLabeler. Be sure to assign <see cref="idLabelConfig"/> before adding to a <see cref="PerceptionCamera"/>.
        /// </summary>
        public BoundingBox2DLabeler()
        {
        }

        /// <summary>
        /// Creates a new BoundingBox2DLabeler with the given <see cref="IdLabelConfig"/>.
        /// </summary>
        /// <param name="labelConfig">The label config for resolving the label for each object.</param>
        public BoundingBox2DLabeler(IdLabelConfig labelConfig)
        {
            this.idLabelConfig = labelConfig;
        }

        /// <inheritdoc/>
        protected override bool supportsVisualization => true;

        /// <summary>
        /// Event information for <see cref="BoundingBox2DLabeler.BoundingBoxesCalculated"/>
        /// </summary>
        internal struct BoundingBoxesCalculatedEventArgs
        {
            /// <summary>
            /// The <see cref="Time.frameCount"/> on which the data was derived. This may be multiple frames in the past.
            /// </summary>
            public int frameCount;
            /// <summary>
            /// Bounding boxes.
            /// </summary>
            public IEnumerable<BoundingBoxAnnotation.Entry> data;
        }

        /// <summary>
        /// Event which is called each frame a semantic segmentation image is read back from the GPU.
        /// </summary>
        internal event Action<BoundingBoxesCalculatedEventArgs> boundingBoxesCalculated;

        /// <inheritdoc/>
        protected override void Setup()
        {
            if (idLabelConfig == null)
                throw new InvalidOperationException("BoundingBox2DLabeler's idLabelConfig field must be assigned");

            m_AsyncData = new Dictionary<int, (AsyncAnnotationFuture annotation, LabelEntryMatchCache labelEntryMatchCache)>();
            m_BoundingBoxValues = new List<BoundingBoxAnnotation.Entry>();

            var spec = idLabelConfig.GetAnnotationSpecification().Select(i => new BoundingBoxAnnotationDefinition.DefinitionEntry { labelId = i.label_id, labelName = i.label_name });
            m_AnnotationDefinition = new BoundingBoxAnnotationDefinition(spec);

            DatasetCapture.RegisterAnnotationDefinition(m_AnnotationDefinition);
#if false
            m_BoundingBoxAnnotationDefinition = DatasetCapture.RegisterAnnotationDefinition("bounding box", idLabelConfig.GetAnnotationSpecification(),
                "Bounding box for each labeled object visible to the sensor", id: new Guid(annotationId));
#endif
            perceptionCamera.RenderedObjectInfosCalculated += OnRenderedObjectInfosCalculated;

            visualizationEnabled = supportsVisualization;

            // Record the original screen size. The screen size can change during play, and the visual bounding
            // boxes need to be scaled appropriately
            m_OriginalScreenSize = new Vector2(Screen.width, Screen.height);

            m_BoundingBoxTexture = Resources.Load<Texture>("outline_box");
            m_LabelTexture = Resources.Load<Texture>("solid_white");

            m_Style = new GUIStyle();
            m_Style.normal.textColor = Color.black;
            m_Style.fontSize = 16;
            m_Style.padding = new RectOffset(4, 4, 4, 4);
            m_Style.contentOffset = new Vector2(4, 0);
            m_Style.alignment = TextAnchor.MiddleLeft;
        }

        /// <inheritdoc/>
        protected override void OnBeginRendering(ScriptableRenderContext scriptableRenderContext)
        {
            m_AsyncData[Time.frameCount] =
                (perceptionCamera.SensorHandle.ReportAnnotationAsync(m_AnnotationDefinition),
                 idLabelConfig.CreateLabelEntryMatchCache(Allocator.TempJob));
        }

        void OnRenderedObjectInfosCalculated(int frameCount, NativeArray<RenderedObjectInfo> renderedObjectInfos)
        {
            if (!m_AsyncData.TryGetValue(frameCount, out var asyncData))
                return;

            m_AsyncData.Remove(frameCount);
            using (s_BoundingBoxCallback.Auto())
            {
                m_BoundingBoxValues.Clear();
                for (var i = 0; i < renderedObjectInfos.Length; i++)
                {
                    var objectInfo = renderedObjectInfos[i];
                    if (!asyncData.labelEntryMatchCache.TryGetLabelEntryFromInstanceId(objectInfo.instanceId, out var labelEntry, out _))
                        continue;

                    m_BoundingBoxValues.Add(new BoundingBoxAnnotation.Entry
                        {
                            labelId = labelEntry.id,
                            labelName = labelEntry.label,
                            instanceId = (int)objectInfo.instanceId,
                            origin = new Vector2(objectInfo.boundingBox.x, objectInfo.boundingBox.y),
                            dimension = new Vector2(objectInfo.boundingBox.width, objectInfo.boundingBox.height)
                        }
                    );
                }

                if (!CaptureOptions.useAsyncReadbackIfSupported && frameCount != Time.frameCount)
                    Debug.LogWarning("Not on current frame: " + frameCount + "(" + Time.frameCount + ")");

                boundingBoxesCalculated?.Invoke(new BoundingBoxesCalculatedEventArgs()
                {
                    data = m_BoundingBoxValues,
                    frameCount = frameCount
                });
#if true
                var toReport = new BoundingBoxAnnotation
                {
                    sensorId = perceptionCamera.ID,
                    Id = m_AnnotationDefinition.id,
                    annotationType = m_AnnotationDefinition.annotationType,
                    description = m_AnnotationDefinition.description,
                    boxes = m_BoundingBoxValues
                };

                asyncData.annotation.Report(toReport);
                asyncData.labelEntryMatchCache.Dispose();
#endif
            }
        }

        /// <inheritdoc/>
        protected override void OnVisualize()
        {
            if (m_BoundingBoxValues == null) return;

            GUI.depth = 5;

            // The player screen can be dynamically resized during play, need to
            // scale the bounding boxes appropriately from the original screen size
            var screenRatioWidth = Screen.width / m_OriginalScreenSize.x;
            var screenRatioHeight = Screen.height / m_OriginalScreenSize.y;

            foreach (var box in m_BoundingBoxValues)
            {
                var x = box.origin.x * screenRatioWidth;
                var y = box.origin.y * screenRatioHeight;

                var boxRect = new Rect(x, y, box.dimension.x * screenRatioWidth, box.dimension.y * screenRatioHeight);
                var labelWidth = Math.Min(120, box.dimension.x * screenRatioWidth);
                var labelRect = new Rect(x, y - 17, labelWidth, 17);
                GUI.DrawTexture(boxRect, m_BoundingBoxTexture, ScaleMode.StretchToFill, true, 0, Color.yellow, 3, 0.25f);
                GUI.DrawTexture(labelRect, m_LabelTexture, ScaleMode.StretchToFill, true, 0, Color.yellow, 0, 0);
                GUI.Label(labelRect, box.labelName + "_" + box.instanceId, m_Style);
            }
        }
    }
}
