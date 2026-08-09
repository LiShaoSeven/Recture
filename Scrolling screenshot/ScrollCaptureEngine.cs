using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;

namespace Recture
{
    public class ScrollCaptureEngine : ICaptureEngine
    {
        public event EventHandler<CaptureProgressEventArgs> ProgressUpdated;
        public event EventHandler<CaptureCompletedEventArgs> CaptureCompleted;

        private SelectionInfo _selection;
        private Bitmap _resultBitmap;
        private Bitmap _previousFrameBitmap;
        private byte[,] _previousGray;
        private bool _isRunning;
        private bool _isPaused;
        private Thread _captureThread;
        private int _captureInterval = 50; // ms
        private int _maxOverlap;
        private double _similarityThreshold = 0.95;
        private double _changeThreshold = 0.05; // 5% 变化检测阈值

        public bool IsRunning => _isRunning;

        public void StartCapture(SelectionInfo selection)
        {
            if (_isRunning) return;

            _selection = selection;
            _isRunning = true;
            _isPaused = false;

            // 初始结果图 = 红色选区
            _resultBitmap = CaptureScreen(selection.RedRect);

            // 计算最大可能的重叠行数 (蓝色选区高度的80%)
            _maxOverlap = (int)(selection.BlueRect.Height * 0.8);

            // 截取第一帧蓝色选区
            _previousFrameBitmap = CaptureScreen(selection.BlueRect);
            _previousGray = ToGrayscale(_previousFrameBitmap);

            _captureThread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "ScrollCaptureThread"
            };
            _captureThread.Start();
        }

        public void StopCapture()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _captureThread?.Join(2000);

            // 最终拼接：追加最后一帧的剩余内容
            FinalizeCapture();

            CaptureCompleted?.Invoke(this, new CaptureCompletedEventArgs(_resultBitmap));
        }

        public void Pause()
        {
            _isPaused = true;
        }

        public void Resume()
        {
            _isPaused = false;
        }

        private void CaptureLoop()
        {
            while (_isRunning)
            {
                if (_isPaused)
                {
                    Thread.Sleep(100);
                    continue;
                }

                try
                {
                    // 截取当前蓝色选区
                    Bitmap currentFrame = CaptureScreen(_selection.BlueRect);
                    byte[,] currentGray = ToGrayscale(currentFrame);

                    // 检测是否有变化
                    double changeRatio = EstimateChangeRatio(_previousGray, currentGray);

                    if (changeRatio > _changeThreshold)
                    {
                        // 找最佳重叠行数
                        int overlap = FindBestOverlap(_previousGray, currentGray, _maxOverlap);

                        if (overlap > 0)
                        {
                            // 拼接新内容（非重叠部分）
                            StitchFrame(currentGray, overlap);
                        }
                        else
                        {
                            // 无重叠，直接追加整个新帧
                            AppendEntireFrame(currentGray);
                        }

                        // 更新上一帧
                        _previousFrameBitmap?.Dispose();
                        _previousFrameBitmap = currentFrame;
                        _previousGray = currentGray;

                        // 通知进度
                        ProgressUpdated?.Invoke(this, new CaptureProgressEventArgs(_resultBitmap.Height));
                    }
                    else
                    {
                        currentFrame.Dispose();
                    }
                }
                catch (Exception)
                {
                    // 忽略截图异常，继续循环
                }

                Thread.Sleep(_captureInterval);
            }
        }

        private Bitmap CaptureScreen(Rectangle rect)
        {
            var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rect.Location, Point.Empty, rect.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        private byte[,] ToGrayscale(Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;
            var gray = new byte[width, height];

            var rect = new Rectangle(0, 0, width, height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bmpData.Scan0;
                    int stride = bmpData.Stride;

                    for (int y = 0; y < height; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            int idx = x * 3;
                            byte b = row[idx];       // Blue
                            byte g = row[idx + 1];   // Green
                            byte r = row[idx + 2];   // Red
                            gray[x, y] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            return gray;
        }

        private double EstimateChangeRatio(byte[,] gray1, byte[,] gray2)
        {
            int width = Math.Min(gray1.GetLength(0), gray2.GetLength(0));
            int height = Math.Min(gray1.GetLength(1), gray2.GetLength(1));

            int changedPixels = 0;
            int sampleStep = Math.Max(1, width / 100); // 采样以加速

            for (int y = 0; y < height; y += sampleStep)
            {
                for (int x = 0; x < width; x += sampleStep)
                {
                    if (Math.Abs(gray1[x, y] - gray2[x, y]) > 5)
                        changedPixels++;
                }
            }

            int totalSamples = (width / sampleStep) * (height / sampleStep);
            return (double)changedPixels / totalSamples;
        }

        private int FindBestOverlap(byte[,] prevGray, byte[,] currGray, int maxOverlap)
        {
            int width = Math.Min(prevGray.GetLength(0), currGray.GetLength(0));
            int prevHeight = prevGray.GetLength(1);
            int currHeight = currGray.GetLength(1);

            int effectiveMaxOverlap = Math.Min(maxOverlap, Math.Min(prevHeight, currHeight));

            // 从最大重叠开始向下搜索
            for (int overlap = effectiveMaxOverlap; overlap >= 5; overlap--)
            {
                double similarity = ComputeRowSimilarity(prevGray, prevHeight - overlap, currGray, 0, overlap, width);

                if (similarity >= _similarityThreshold)
                    return overlap;
            }

            return 0;
        }

        private double ComputeRowSimilarity(byte[,] gray1, int startY1, byte[,] gray2, int startY2, int rowCount, int width)
        {
            int totalDiff = 0;
            int totalPixels = rowCount * width;

            for (int y = 0; y < rowCount; y++)
            {
                int y1 = startY1 + y;
                int y2 = startY2 + y;

                if (y1 >= gray1.GetLength(1) || y2 >= gray2.GetLength(1))
                    break;

                for (int x = 0; x < width; x++)
                {
                    totalDiff += Math.Abs(gray1[x, y1] - gray2[x, y2]);
                }
            }

            double avgDiff = (double)totalDiff / totalPixels;
            return 1.0 - (avgDiff / 255.0);
        }

        private void StitchFrame(byte[,] currentGray, int overlap)
        {
            int width = currentGray.GetLength(0);
            int newRows = currentGray.GetLength(1) - overlap;

            if (newRows <= 0) return;

            // 扩展结果图
            int oldHeight = _resultBitmap.Height;
            int newHeight = oldHeight + newRows;

            var newResult = new Bitmap(width, newHeight, PixelFormat.Format24bppRgb);

            using (var g = Graphics.FromImage(newResult))
            {
                // 复制原有结果
                g.DrawImage(_resultBitmap, 0, 0, width, oldHeight);

                // 追加新内容（从overlap行开始）
                var frameData = new Bitmap(width, currentGray.GetLength(1), PixelFormat.Format24bppRgb);

                // 将灰度数据转为彩色位图
                FillBitmapFromGray(frameData, currentGray);

                var srcRect = new Rectangle(0, overlap, width, newRows);
                var dstRect = new Rectangle(0, oldHeight, width, newRows);
                g.DrawImage(frameData, dstRect, srcRect, GraphicsUnit.Pixel);

                frameData.Dispose();
            }

            _resultBitmap?.Dispose();
            _resultBitmap = newResult;
        }

        private void AppendEntireFrame(byte[,] currentGray)
        {
            int width = currentGray.GetLength(0);
            int frameHeight = currentGray.GetLength(1);

            int oldHeight = _resultBitmap.Height;
            int newHeight = oldHeight + frameHeight;

            var newResult = new Bitmap(width, newHeight, PixelFormat.Format24bppRgb);

            using (var g = Graphics.FromImage(newResult))
            {
                g.DrawImage(_resultBitmap, 0, 0, width, oldHeight);

                var frameBmp = new Bitmap(width, frameHeight, PixelFormat.Format24bppRgb);
                FillBitmapFromGray(frameBmp, currentGray);

                g.DrawImage(frameBmp, 0, oldHeight);
                frameBmp.Dispose();
            }

            _resultBitmap?.Dispose();
            _resultBitmap = newResult;
        }

        private void FillBitmapFromGray(Bitmap bmp, byte[,] gray)
        {
            int width = gray.GetLength(0);
            int height = gray.GetLength(1);

            var rect = new Rectangle(0, 0, width, height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bmpData.Scan0;
                    int stride = bmpData.Stride;

                    for (int y = 0; y < height; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            byte val = gray[x, y];
                            int idx = x * 3;
                            row[idx] = val;       // B
                            row[idx + 1] = val;   // G
                            row[idx + 2] = val;   // R
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }
        }

        private void FinalizeCapture()
        {
            if (_previousGray == null) return;

            // 尝试将最后一帧与结果图底部拼接
            int resultWidth = _resultBitmap.Width;
            int resultHeight = _resultBitmap.Height;
            int prevWidth = _previousGray.GetLength(0);
            int prevHeight = _previousGray.GetLength(1);

            int width = Math.Min(resultWidth, prevWidth);

            // 从结果图底部截取一段作为参考
            int refHeight = Math.Min(_maxOverlap, Math.Min(resultHeight, prevHeight));

            if (refHeight < 5)
            {
                // 直接追加整个最后一帧
                AppendLastFrameEntire();
                return;
            }

            // 提取结果图底部灰度
            var resultBottomGray = ExtractBottomGray(_resultBitmap, refHeight);

            // 找重叠
            int overlap = FindBestOverlap(resultBottomGray, _previousGray, refHeight);

            if (overlap > 0)
            {
                // 只追加非重叠部分
                int newRows = prevHeight - overlap;
                if (newRows > 0)
                {
                    AppendLastFramePartial(newRows);
                }
            }
            else
            {
                AppendLastFrameEntire();
            }
        }

        private byte[,] ExtractBottomGray(Bitmap bmp, int height)
        {
            int width = bmp.Width;
            int startY = bmp.Height - height;

            var bottomBmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bottomBmp))
            {
                g.DrawImage(bmp, new Rectangle(0, 0, width, height), new Rectangle(0, startY, width, height), GraphicsUnit.Pixel);
            }

            return ToGrayscale(bottomBmp);
        }

        private void AppendLastFramePartial(int newRows)
        {
            if (_previousFrameBitmap == null || newRows <= 0) return;

            int width = _previousFrameBitmap.Width;
            int oldHeight = _resultBitmap.Height;
            int newHeight = oldHeight + newRows;

            var newResult = new Bitmap(width, newHeight, PixelFormat.Format24bppRgb);

            using (var g = Graphics.FromImage(newResult))
            {
                g.DrawImage(_resultBitmap, 0, 0, width, oldHeight);

                // 追加最后一帧的底部newRows行
                var srcRect = new Rectangle(0, _previousFrameBitmap.Height - newRows, width, newRows);
                var dstRect = new Rectangle(0, oldHeight, width, newRows);
                g.DrawImage(_previousFrameBitmap, dstRect, srcRect, GraphicsUnit.Pixel);
            }

            _resultBitmap?.Dispose();
            _resultBitmap = newResult;
        }

        private void AppendLastFrameEntire()
        {
            if (_previousFrameBitmap == null) return;

            int width = _previousFrameBitmap.Width;
            int oldHeight = _resultBitmap.Height;
            int frameHeight = _previousFrameBitmap.Height;
            int newHeight = oldHeight + frameHeight;

            var newResult = new Bitmap(width, newHeight, PixelFormat.Format24bppRgb);

            using (var g = Graphics.FromImage(newResult))
            {
                g.DrawImage(_resultBitmap, 0, 0, width, oldHeight);
                g.DrawImage(_previousFrameBitmap, 0, oldHeight);
            }

            _resultBitmap?.Dispose();
            _resultBitmap = newResult;
        }

        public void Dispose()
        {
            _isRunning = false;
            _captureThread?.Join(2000);
            _resultBitmap?.Dispose();
            _previousFrameBitmap?.Dispose();
        }
    }

    // New manual capture engine for manual mode
    public class ManualCaptureEngine : ICaptureEngine
    {
        public event EventHandler<CaptureProgressEventArgs> ProgressUpdated;
        public event EventHandler<CaptureCompletedEventArgs> CaptureCompleted;

        private SelectionInfo _selection;
        private Bitmap _resultBitmap;
        private Bitmap _currentFrame;
        private bool _isRunning;
        private bool _isPaused;
        private Thread _previewThread;
        private object _frameLock = new object();

        public bool IsRunning => _isRunning;

        // The key used to capture a single frame in manual mode (default: Space)
        public Key CaptureKey { get; set; } = Key.Space;

        public void StartCapture(SelectionInfo selection)
        {
            if (_isRunning) return;
            _selection = selection;
            _isRunning = true;
            _isPaused = false;

            // 初始结果 = 红色区域
            _resultBitmap = CaptureScreen(selection.RedRect);

            // start continuous preview thread
            _previewThread = new Thread(PreviewLoop) { IsBackground = true, Name = "ManualPreviewThread" };
            _previewThread.Start();

            // Hook keyboard message sink to capture key presses
            KeyboardHook.Start(OnKeyDown);
        }

        public void StopCapture()
        {
            if (!_isRunning) return;
            _isRunning = false;
            KeyboardHook.Stop();
            _previewThread?.Join(1000);

            // 完成并触发
            CaptureCompleted?.Invoke(this, new CaptureCompletedEventArgs(_resultBitmap));
        }

        public void Pause()
        {
            _isPaused = true;
        }

        public void Resume()
        {
            _isPaused = false;
        }

        private void PreviewLoop()
        {
            while (_isRunning)
            {
                if (!_isPaused)
                {
                    try
                    {
                        var frame = CaptureScreen(_selection.BlueRect);
                        lock (_frameLock)
                        {
                            _currentFrame?.Dispose();
                            _currentFrame = frame;
                        }
                    }
                    catch { }
                }
                Thread.Sleep(200);
            }
        }

        private void OnKeyDown(System.Windows.Forms.Keys key)
        {
            if (!_isRunning || _isPaused) return;

            if (KeyInterop.VirtualKeyFromKey(CaptureKey) == (int)key)
            {
                // capture current selection area and append
                Bitmap frame;
                lock (_frameLock)
                {
                    if (_currentFrame == null) return;
                    frame = (Bitmap)_currentFrame.Clone();
                }

                AppendFrame(frame);
                frame.Dispose();

                // after append, update preview immediately
                lock (_frameLock)
                {
                    var newPreview = CaptureScreen(_selection.BlueRect);
                    _currentFrame?.Dispose();
                    _currentFrame = newPreview;
                }

                ProgressUpdated?.Invoke(this, new CaptureProgressEventArgs(_resultBitmap.Height));
            }
            else if (key == Keys.Escape)
            {
                StopCapture();
            }
        }

        private void AppendFrame(Bitmap frame)
        {
            int width = frame.Width;
            int fh = frame.Height;
            int oldH = _resultBitmap.Height;
            int newH = oldH + fh;
            var newResult = new Bitmap(width, newH, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(newResult))
            {
                g.DrawImage(_resultBitmap, 0, 0, width, oldH);
                g.DrawImage(frame, 0, oldH);
            }
            _resultBitmap?.Dispose();
            _resultBitmap = newResult;
        }

        private Bitmap CaptureScreen(Rectangle rect)
        {
            var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rect.Location, Point.Empty, rect.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        public Bitmap GetCurrentFrame()
        {
            lock (_frameLock)
            {
                return _currentFrame == null ? null : (Bitmap)_currentFrame.Clone();
            }
        }

        public void Dispose()
        {
            _isRunning = false;
            KeyboardHook.Stop();
            _previewThread?.Join(500);
            _resultBitmap?.Dispose();
            lock (_frameLock)
            {
                _currentFrame?.Dispose();
            }
        }
    }

    // Simple keyboard hook util using Win32 low-level keyboard hook via WinForms message loop helper
    internal static class KeyboardHook
    {
        private static MessageWindow _msgWindow;

        public static void Start(Action<System.Windows.Forms.Keys> onKeyDown)
        {
            if (_msgWindow != null) return;
            _msgWindow = new MessageWindow(onKeyDown);
        }

        public static void Stop()
        {
            if (_msgWindow == null) return;
            _msgWindow.Dispose();
            _msgWindow = null;
        }

        private class MessageWindow : System.Windows.Forms.NativeWindow, IDisposable
        {
            private Action<System.Windows.Forms.Keys> _onKeyDown;

            public MessageWindow(Action<System.Windows.Forms.Keys> onKeyDown)
            {
                _onKeyDown = onKeyDown;
                CreateHandle(new System.Windows.Forms.CreateParams());
            }

            protected override void WndProc(ref System.Windows.Forms.Message m)
            {
                const int WM_KEYDOWN = 0x0100;
                const int WM_SYSKEYDOWN = 0x0104;
                if (m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN)
                {
                    var key = (System.Windows.Forms.Keys)(int)m.WParam;
                    _onKeyDown?.Invoke(key);
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                DestroyHandle();
            }
        }
    }

    public class CaptureProgressEventArgs : EventArgs
    {
        public int CurrentHeight { get; }
        public CaptureProgressEventArgs(int currentHeight) { CurrentHeight = currentHeight; }
    }

    public class CaptureCompletedEventArgs : EventArgs
    {
        public Bitmap ResultBitmap { get; }
        public CaptureCompletedEventArgs(Bitmap resultBitmap) { ResultBitmap = resultBitmap; }
    }
}
