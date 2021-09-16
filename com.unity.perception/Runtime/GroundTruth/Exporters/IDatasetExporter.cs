using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnityEngine.Perception.GroundTruth.Exporters
{
    public interface IDatasetExporter
    {
        string GetRgbCaptureFilename(params(string, object)[] additionalSensorValues);

        void OnSimulationBegin(string directoryName);
        void OnSimulationEnd();

        void OnAnnotationRegistered<TSpec>(Guid annotationId, TSpec[] values);

        Task ProcessPendingCaptures(List<SimulationState.PendingCapture> pendingCaptures, SimulationState simState, int captureFileIndex);

        Task OnCaptureReported(int frame, int width, int height, string filename);
    }
}
