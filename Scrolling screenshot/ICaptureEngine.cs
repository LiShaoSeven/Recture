using System;
using System.Drawing;

namespace Recture
{
    public interface ICaptureEngine : IDisposable
    {
        event EventHandler<CaptureProgressEventArgs> ProgressUpdated;
        event EventHandler<CaptureCompletedEventArgs> CaptureCompleted;

        bool IsRunning { get; }
        void StartCapture(SelectionInfo selection);
        void StopCapture();
        void Pause();
        void Resume();
    }
}
