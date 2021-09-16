using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.Perception.GroundTruth.Exporters.Coco;
using UnityEngine.Perception.GroundTruth.Exporters.PerceptionFormat;

namespace UnityEngine.Perception.GroundTruth.Exporters.CocoHybrid
{
    public class CocoHybridExporter : IDatasetExporter
    {
        CocoExporter m_Coco = new CocoExporter();
        PerceptionExporter m_Perception = new PerceptionExporter();

        public string GetRgbCaptureFilename(params (string, object)[] additionalSensorValues)
        {
            return m_Coco.GetRgbCaptureFilename(additionalSensorValues) + m_Perception.GetRgbCaptureFilename(additionalSensorValues);
        }

        public void OnSimulationBegin(string directoryName)
        {
            m_Coco.OnSimulationBegin(directoryName + "_coco");
            m_Perception.OnSimulationBegin(directoryName);
        }

        public void OnSimulationEnd()
        {
            m_Coco.OnSimulationEnd();
            m_Perception.OnSimulationEnd();
        }

        public void OnAnnotationRegistered<TSpec>(Guid annotationId, TSpec[] values)
        {
            m_Coco.OnAnnotationRegistered(annotationId, values);
            m_Perception.OnAnnotationRegistered(annotationId, values);
        }

        public async Task ProcessPendingCaptures(List<SimulationState.PendingCapture> pendingCaptures, SimulationState simState, int captureFileIndex)
        {
            var cocoTask = m_Coco.ProcessPendingCaptures(pendingCaptures, simState, captureFileIndex);
            var perceptionTask = m_Perception.ProcessPendingCaptures(pendingCaptures, simState, captureFileIndex);
            await cocoTask;
            await perceptionTask;
        }

        public async Task OnCaptureReported(int frame, int width, int height, string filename)
        {
            var cocoTask = m_Coco.OnCaptureReported(frame, width, height, filename);
            var perceptionTask = m_Perception.OnCaptureReported(frame, width, height, filename);
            await cocoTask;
            await perceptionTask;
        }
    }
}
