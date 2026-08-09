using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace Recture
{
    public partial class ManualPreviewWindow : Window
    {
        private BitmapSource _lastSource;

        public ManualPreviewWindow()
        {
            InitializeComponent();

            // 在构造函数中立即确定位置（DIPs），避免 Show 之后到 Loaded 之前窗口位置不确定
            WindowStartupLocation = WindowStartupLocation.Manual;
            PositionToTopRight();
        }

        private void PositionToTopRight()
        {
            // SystemParameters.WorkArea 是 DIPs，与 WPF Window.Left/Top 单位一致
            double waW = SystemParameters.WorkArea.Width;
            double waH = SystemParameters.WorkArea.Height;
            double w = Width;
            double h = Height;
            if (w > waW) w = Math.Max(MinWidth, waW - 20);
            if (h > waH) h = Math.Max(MinHeight, waH - 20);

            Left = Math.Max(0, waW - w - 20);
            Top = Math.Max(0, 20);
            Width = w;
            Height = h;
        }

        public void UpdatePreview(Bitmap bmp)
        {
            if (bmp == null) return;
            try
            {
                IntPtr h = bmp.GetHbitmap();
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    try { if (src.CanFreeze) src.Freeze(); } catch { }
                    PreviewImage.Source = src;
                    _lastSource = src;
                }
                finally
                {
                    DeleteObject(h);
                }
            }
            catch { }
        }

        public void UpdateStatus(int totalHeight, int frameWidth, int frameHeight)
        {
            StatusText.Text = $"已拼接: {totalHeight}px  (当前帧 {frameWidth}×{frameHeight})";
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
