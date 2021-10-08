using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Profiling;
using UnityEditor.Profiling.Memory.Experimental;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.Perception.GroundTruth.Exporters.Solo;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    /// Labeler which produces label id, instance id, and visible pixel count in a single metric each frame for
    /// each object which takes up one or more pixels in the camera's frame.
    /// </summary>
    [Serializable]
    public sealed class RenderedObjectInfoLabeler : CameraLabeler
    {
        [Serializable]
        public class MetricDefinition : DataModel.MetricDefinition
        {
            public IdLabelConfig.LabelEntrySpec[] spec;

            public MetricDefinition() { }

            public MetricDefinition(string id, string description, IdLabelConfig.LabelEntrySpec[] spec)
            {
                this.id = id;
                this.description = description;
                this.spec = spec;
            }

            public override bool IsValid()
            {
                return base.IsValid() && spec != null;
            }

            public override void ToMessage(IMessageBuilder builder)
            {
                // TODO stuff for spec
                base.ToMessage(builder);
            }
        }

        [Serializable]
        public class RenderedObjectInfoMetric : Metric
        {
            public override IEnumerable<object> Values => objectInfo;

            public class Entry
            {
                [UsedImplicitly]
                public int label_id;
                [UsedImplicitly]
                public uint instance_id;
                [UsedImplicitly]
                public Color32 instance_color;
                [UsedImplicitly]
                public int visible_pixels;
            }

            public IEnumerable<Entry> objectInfo;
        }

        static readonly string k_Id = "RenderedObjectInfo";
        static readonly string k_Description = "Produces label id, instance id, and visible pixel count in a single metric each frame for each object which takes up one or more pixels in the camera's frame, based on this labeler's associated label configuration.";

        ///<inheritdoc/>
        public override string description
        {
            get => k_Description;
            protected set {}
        }

        static ProfilerMarker s_ProduceRenderedObjectInfoMetric = new ProfilerMarker("ProduceRenderedObjectInfoMetric");

        /// <summary>
        /// The <see cref="IdLabelConfig"/> which associates objects with labels.
        /// </summary>
        [FormerlySerializedAs("labelingConfiguration")]
        public IdLabelConfig idLabelConfig;

        RenderedObjectInfoMetric.Entry[] m_VisiblePixelsValues;

        Dictionary<int, AsyncFuture<Metric>> m_ObjectInfoAsyncMetrics;
        MetricDefinition m_Definition;

        /// <summary>
        /// Creates a new RenderedObjectInfoLabeler. Be sure to assign <see cref="idLabelConfig"/> before adding to a <see cref="PerceptionCamera"/>.
        /// </summary>
        public RenderedObjectInfoLabeler()
        {
        }

        /// <summary>
        /// Creates a new RenderedObjectInfoLabeler with an <see cref="IdLabelConfig"/>.
        /// </summary>
        /// <param name="idLabelConfig">The <see cref="IdLabelConfig"/> which associates objects with labels. </param>
        public RenderedObjectInfoLabeler(IdLabelConfig idLabelConfig)
        {
            if (idLabelConfig == null)
                throw new ArgumentNullException(nameof(idLabelConfig));

            this.idLabelConfig = idLabelConfig;
        }

        /// <inheritdoc/>
        protected override bool supportsVisualization => true;

        /// <inheritdoc/>
        protected override void Setup()
        {
            if (idLabelConfig == null)
                throw new InvalidOperationException("RenderedObjectInfoLabeler's idLabelConfig field must be assigned");

            m_ObjectInfoAsyncMetrics = new Dictionary<int, AsyncFuture<Metric>>();

            perceptionCamera.RenderedObjectInfosCalculated += (frameCount, objectInfo) =>
            {
                ProduceRenderedObjectInfoMetric(objectInfo, frameCount);
            };

            m_Definition = new MetricDefinition(k_Id, k_Description, idLabelConfig.GetAnnotationSpecification());

            DatasetCapture.Instance.RegisterMetric(m_Definition);

            visualizationEnabled = supportsVisualization;
        }

        /// <inheritdoc/>
        protected override void OnBeginRendering(ScriptableRenderContext scriptableRenderContext)
        {
            m_ObjectInfoAsyncMetrics[Time.frameCount] = perceptionCamera.SensorHandle.ReportMetricAsync(m_Definition);
        }

        void ProduceRenderedObjectInfoMetric(NativeArray<RenderedObjectInfo> renderedObjectInfos, int frameCount)
        {
            using (s_ProduceRenderedObjectInfoMetric.Auto())
            {
                if (!m_ObjectInfoAsyncMetrics.TryGetValue(frameCount, out var metric))
                    return;

                m_ObjectInfoAsyncMetrics.Remove(frameCount);

                if (m_VisiblePixelsValues == null || m_VisiblePixelsValues.Length != renderedObjectInfos.Length)
                    m_VisiblePixelsValues = new RenderedObjectInfoMetric.Entry[renderedObjectInfos.Length];

                var visualize = visualizationEnabled;

                if (visualize)
                {
                    // Clear out all of the old entries...
                    hudPanel.RemoveEntries(this);
                }

                for (var i = 0; i < renderedObjectInfos.Length; i++)
                {
                    var objectInfo = renderedObjectInfos[i];
                    if (!TryGetLabelEntryFromInstanceId(objectInfo.instanceId, out var labelEntry))
                        continue;

                    m_VisiblePixelsValues[i] = new RenderedObjectInfoMetric.Entry
                    {
                        label_id = labelEntry.id,
                        instance_id = objectInfo.instanceId,
                        visible_pixels = objectInfo.pixelCount,
                        instance_color = objectInfo.instanceColor
                    };

                    if (visualize)
                    {
                        var label = labelEntry.label + "_" + objectInfo.instanceId;
                        hudPanel.UpdateEntry(this, label, objectInfo.pixelCount.ToString());
                    }
                }

                var (seq, step) = DatasetCapture.Instance.GetSequenceAndStepFromFrame(frameCount);

                var payload = new RenderedObjectInfoMetric
                {
                    Id = m_Definition.id,
                    sensorId = perceptionCamera.ID,
                    annotationId = default,
                    description = m_Definition.description,
                    objectInfo = m_VisiblePixelsValues,
                    sequenceId = seq,
                    step = step
                };

                metric.Report(payload);
            }
        }

        bool TryGetLabelEntryFromInstanceId(uint instanceId, out IdLabelEntry labelEntry)
        {
            return idLabelConfig.TryGetLabelEntryFromInstanceId(instanceId, out labelEntry);
        }

        /// <inheritdoc/>
        protected override void OnVisualizerEnabledChanged(bool enabled)
        {
            if (enabled) return;
            hudPanel.RemoveEntries(this);
        }
    }
}
