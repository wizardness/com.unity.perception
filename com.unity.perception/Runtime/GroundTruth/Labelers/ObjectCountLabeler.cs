using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.Rendering;

namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    /// Labeler which produces object counts for each label in the associated <see cref="IdLabelConfig"/> each frame.
    /// </summary>
    [Serializable]
    public sealed class ObjectCountLabeler : CameraLabeler
    {
        /// <summary>
        /// The object count metric records how many of a particular object are
        /// present in a capture.
        /// </summary>
        [Serializable]
        public class ObjectCountMetric : Metric
        {
            public struct Entry
            {
                /// <summary>
                /// The label of the category
                /// </summary>
                public string labelName;
                /// <summary>
                /// The number of instances for a particular category.
                /// </summary>
                public int count;
            }

            /// <summary>
            ///  The object counts
            /// </summary>
            public IEnumerable<Entry> objectCounts;
        }

        static readonly string k_Id = "ObjectCount";
        static readonly string k_Description = "Produces object counts for each label defined in this labeler's associated label configuration.";

        ///<inheritdoc/>
        public override string description
        {
            get => k_Description;
            protected set {}
        }

        /// <summary>
        /// The <see cref="IdLabelConfig"/> which associates objects with labels.
        /// </summary>
        public IdLabelConfig labelConfig
        {
            get => m_LabelConfig;
            set => m_LabelConfig = value;
        }

        /// <summary>
        /// Fired when the object counts are computed for a frame.
        /// </summary>
        public event Action<int, NativeSlice<uint>,IReadOnlyList<IdLabelEntry>> ObjectCountsComputed;

        [SerializeField]
        IdLabelConfig m_LabelConfig;

        static ProfilerMarker s_ClassCountCallback = new ProfilerMarker("OnClassLabelsReceived");

        ObjectCountMetric.Entry[] m_ClassCountValues;

        Dictionary<int, AsyncMetricFuture> m_AsyncMetrics;
        MetricDefinition m_Definition = new MetricDefinition(k_Id, k_Description);

        /// <summary>
        /// Creates a new ObjectCountLabeler. This constructor should only be used by serialization. For creation from
        /// user code, use <see cref="ObjectCountLabeler(IdLabelConfig)"/>.
        /// </summary>
        public ObjectCountLabeler()
        {
        }

        /// <summary>
        /// Creates a new ObjectCountLabeler with the given <see cref="IdLabelConfig"/>.
        /// </summary>
        /// <param name="labelConfig">The label config for resolving the label for each object.</param>
        public ObjectCountLabeler(IdLabelConfig labelConfig)
        {
            if (labelConfig == null)
                throw new ArgumentNullException(nameof(labelConfig));

            m_LabelConfig = labelConfig;
        }

        /// <inheritdoc/>
        protected override bool supportsVisualization => true;

        /// <inheritdoc/>
        protected override void Setup()
        {
            if (labelConfig == null)
                throw new InvalidOperationException("The ObjectCountLabeler idLabelConfig field must be assigned");

            m_AsyncMetrics =  new Dictionary<int, AsyncMetricFuture>();

            perceptionCamera.RenderedObjectInfosCalculated += (frameCount, objectInfo) =>
            {
                var objectCounts = ComputeObjectCounts(objectInfo);
                ObjectCountsComputed?.Invoke(frameCount, objectCounts, labelConfig.labelEntries);
                ProduceObjectCountMetric(objectCounts, m_LabelConfig.labelEntries, frameCount);
            };

            m_Definition = new MetricDefinition
            {
                id = k_Id,
                description = k_Description
            };

            DatasetCapture.Instance.RegisterMetric(m_Definition);
            visualizationEnabled = supportsVisualization;
        }

        /// <inheritdoc/>
        protected override void OnBeginRendering(ScriptableRenderContext scriptableRenderContext)
        {
#if true
            m_AsyncMetrics[Time.frameCount] = perceptionCamera.SensorHandle.ReportMetricAsync(m_Definition);
#endif
        }

        NativeArray<uint> ComputeObjectCounts(NativeArray<RenderedObjectInfo> objectInfo)
        {
            var objectCounts = new NativeArray<uint>(m_LabelConfig.labelEntries.Count, Allocator.Temp);
            foreach (var info in objectInfo)
            {
                if (!m_LabelConfig.TryGetLabelEntryFromInstanceId(info.instanceId, out _, out var labelIndex))
                    continue;

                objectCounts[labelIndex]++;
            }

            return objectCounts;
        }

        void ProduceObjectCountMetric(NativeSlice<uint> counts, IReadOnlyList<IdLabelEntry> entries, int frameCount)
        {
            using (s_ClassCountCallback.Auto())
            {
                if (!m_AsyncMetrics.TryGetValue(frameCount, out var classCountAsyncMetric))
                    return;

                m_AsyncMetrics.Remove(frameCount);

                if (m_ClassCountValues == null || m_ClassCountValues.Length != entries.Count)
                    m_ClassCountValues = new ObjectCountMetric.Entry[entries.Count]; //ClassCountValue[entries.Count];

                var visualize = visualizationEnabled;

                if (visualize)
                {
                    // Clear out all of the old entries...
                    hudPanel.RemoveEntries(this);
                }

                for (var i = 0; i < entries.Count; i++)
                {
                    m_ClassCountValues[i] = new ObjectCountMetric.Entry
                    {
                        labelName = entries[i].label,
                        count = (int)counts[i]
                    };

                    // Only display entries with a count greater than 0
                    if (visualize && counts[i] > 0)
                    {
                        var label = entries[i].label + " Counts";
                        hudPanel.UpdateEntry(this, label, counts[i].ToString());
                    }
                }

                var payload = new ObjectCountMetric
                {
                    sensorId = "",
                    annotationId = default,
                    description = m_Definition.description,
                    metadata = new Dictionary<string, object>(),
                    objectCounts = m_ClassCountValues
                };
                classCountAsyncMetric.Report(payload);
            }
        }

        /// <inheritdoc/>
        protected override void OnVisualizerEnabledChanged(bool enabled)
        {
            if (enabled) return;
            hudPanel.RemoveEntries(this);
        }
    }
}
