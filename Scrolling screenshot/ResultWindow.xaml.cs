using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Recture
{
    public partial class ResultWindow : Window
    {
        private readonly Bitmap _resultBitmap;
        private BitmapSource _fullBitmapSource;
        private double _zoom = 1.0;

        public ResultWindow(Bitmap resultBitmap)
        {
            InitializeComponent();
            _resultBitmap = resultBitmap;
            DisplayImage();

            // Initialize zoom controls
            ZoomSlider.Value = 1.0;
            ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;
            ZoomInButton.Click += (s, e) => ZoomSlider.Value = Math.Min(ZoomSlider.Value + 0.1, ZoomSlider.Maximum);
            ZoomOutButton.Click += (s, e) => ZoomSlider.Value = Math.Max(ZoomSlider.Value - 0.1, ZoomSlider.Minimum);
            FitButton.Click += FitButton_Click;
            ActualSizeButton.Click += ActualSizeButton_Click;

            CopyButton.Click += CopyButton_Click;
            SaveButton.Click += SaveButton_Click;
            CloseButton.Click += CloseButton_Click;
        }

        private void DisplayImage()
        {
            // Convert full GDI bitmap to WPF BitmapSource
            IntPtr hBitmap = _resultBitmap.GetHbitmap();
            try
            {
                _fullBitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                // Freeze to make it thread-safe and allow creating CroppedBitmap reliably
                try { if (_fullBitmapSource.CanFreeze) _fullBitmapSource.Freeze(); } catch { }
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                {
                    DeleteObject(hBitmap);
                }
            }

            // Clear any previous images
            var panel = GetImagePanel();
            if (panel == null) return;
            panel.Children.Clear();

            if (_fullBitmapSource == null) return;

            // WPF/GPU may have limits on maximum image height. Slice the large image into vertical tiles.
            int totalHeight = _fullBitmapSource.PixelHeight;
            int width = _fullBitmapSource.PixelWidth;

            const int maxTileHeight = 4096; // safe tile height; adjust if needed for very large screens/GPU

            for (int y = 0; y < totalHeight; y += maxTileHeight)
            {
                int tileH = Math.Min(maxTileHeight, totalHeight - y);
                var crop = new CroppedBitmap(_fullBitmapSource, new Int32Rect(0, y, width, tileH));
                try { if (crop.CanFreeze) crop.Freeze(); } catch { }

                var img = new System.Windows.Controls.Image
                {
                    Source = crop,
                    Stretch = System.Windows.Media.Stretch.None,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    SnapsToDevicePixels = true
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.HighQuality);
                // Enable cache to reduce re-rendering artifacts on very tall images
                img.CacheMode = new System.Windows.Media.BitmapCache();
                panel.Children.Add(img);
            }

            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (_fullBitmapSource == null) return;

            _zoom = ZoomSlider?.Value ?? 1.0;

            // Apply scale to the container so all tiles scale together and ScrollViewer measures correctly
            var panel = GetImagePanel();
            if (panel == null) return;
            panel.Width = double.NaN;
            panel.Height = double.NaN;
            panel.LayoutTransform = new ScaleTransform(_zoom, _zoom);
            panel.SnapsToDevicePixels = true;
            panel.UseLayoutRounding = true;
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyZoom();
        }

        private void FitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_fullBitmapSource == null) return;

            double availableWidth = ImageScrollViewer.ViewportWidth;
            double availableHeight = ImageScrollViewer.ViewportHeight;

            // If viewport not ready (e.g., first render), use ScrollViewer actual size minus margins
            if (double.IsNaN(availableWidth) || availableWidth == 0)
                availableWidth = ImageScrollViewer.ActualWidth;
            if (double.IsNaN(availableHeight) || availableHeight == 0)
                availableHeight = ImageScrollViewer.ActualHeight;

            if (availableWidth <= 0 || availableHeight <= 0) return;

            double scaleX = availableWidth / _fullBitmapSource.PixelWidth;
            double scaleY = availableHeight / _fullBitmapSource.PixelHeight;
            double scale = Math.Min(scaleX, scaleY);

            // Clamp scale to slider bounds
            scale = Math.Max(ZoomSlider.Minimum, Math.Min(ZoomSlider.Maximum, scale));
            ZoomSlider.Value = scale;
        }

        private void ActualSizeButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomSlider.Value = 1.0;
        }

        private void ImageScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Ctrl + MouseWheel to zoom
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                if (e.Delta > 0)
                    ZoomSlider.Value = Math.Min(ZoomSlider.Value + 0.1, ZoomSlider.Maximum);
                else
                    ZoomSlider.Value = Math.Max(ZoomSlider.Value - 0.1, ZoomSlider.Minimum);

                e.Handled = true;
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 将截图以多种格式放入剪贴板，确保各类应用（微信/QQ/浏览器/Office）都能粘贴
                // 仅放 DIB 的 WPF Clipboard.SetImage 在很多应用里粘贴不出，必须补 PNG + CF_BITMAP
                using (var pngMs = new MemoryStream())
                {
                    _resultBitmap.Save(pngMs, ImageFormat.Png);
                    var pngBytes = pngMs.ToArray();

                    var data = new System.Windows.DataObject();
                    // 1) PNG 流（CFSTR_PNG）—— 浏览器/Electron/现代 IM 识别
                    data.SetData("PNG", new MemoryStream(pngBytes, false));
                    // 2) DIBv5 —— 带透明通道，Office/绘图软件识别
                    data.SetData(DataFormats.Dib, CreateDibV5FromBitmap(_resultBitmap), true);
                    // 3) BitmapSource —— WPF 内部互操作
                    data.SetImage(BitmapToBitmapSource(_resultBitmap));

                    Clipboard.Clear();
                    Clipboard.SetDataObject(data, true);
                }
            }
            catch (Exception ex)
            {
                
            }
        }

        private static BitmapSource BitmapToBitmapSource(Bitmap bmp)
        {
            IntPtr h = bmp.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                try { if (src.CanFreeze) src.Freeze(); } catch { }
                return src;
            }
            finally
            {
                DeleteObject(h);
            }
        }

        // 生成 DIBv5 字节数组（带透明通道），用于 Clipboard DataFormats.Dib
        private static byte[] CreateDibV5FromBitmap(Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;
            // BITMAPV5HEADER 大小 = 124 字节
            const int headerSize = 124;
            int pixelSize = width * height * 4;
            byte[] data = new byte[headerSize + pixelSize];

            // BITMAPV5HEADER
            BitConverter.GetBytes(headerSize).CopyTo(data, 0);      // bV5Size
            BitConverter.GetBytes(width).CopyTo(data, 4);            // bV5Width
            BitConverter.GetBytes(height).CopyTo(data, 8);          // bV5Height（正数=自底向上，复制时需要翻转）
            BitConverter.GetBytes((short)1).CopyTo(data, 12);       // bV5Planes
            BitConverter.GetBytes((short)32).CopyTo(data, 14);     // bV5BitCount
            BitConverter.GetBytes(3).CopyTo(data, 16);              // bV5Compression = BI_BITFIELDS
            BitConverter.GetBytes(pixelSize).CopyTo(data, 20);      // bV5SizeImage
            // 28-35 bV5XPelsPerMeter, 36-43 bV5YPelsPerMeter 留 0
            BitConverter.GetBytes(0).CopyTo(data, 44);              // bV5ClrUsed
            BitConverter.GetBytes(0).CopyTo(data, 48);             // bV5ClrImportant
            // 52-55 bV5RedMask   = 0x00FF0000
            BitConverter.GetBytes(0x00FF0000).CopyTo(data, 52);
            // 56-59 bV5GreenMask = 0x0000FF00
            BitConverter.GetBytes(0x0000FF00).CopyTo(data, 56);
            // 60-63 bV5BlueMask  = 0x000000FF
            BitConverter.GetBytes(0x000000FF).CopyTo(data, 60);
            // 64-67 bV5AlphaMask = 0xFF000000
            BitConverter.GetBytes(0xFF000000).CopyTo(data, 64);
            // 68 bV5CSType = LCS_GRAPHICS_COLOR_SPACE (0x73524742 'sRGB')
            BitConverter.GetBytes(0x73524742).CopyTo(data, 68);
            // 72-107 bV5Endpoints (CIEXYZTRIPLE) 留 0
            // 108-115 bV5GammaRed/Green/Blue 留 0
            // 116-123 bV5Intent 等留 0

            // 拷贝像素：源是 ARGB 32bpp 顶向下；DIB 默认自底向上，需翻转 Y
            var rect = new Rectangle(0, 0, width, height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int strideSrc = bmpData.Stride;
                int strideDst = width * 4;
                unsafe
                {
                    byte* srcBase = (byte*)bmpData.Scan0;
                    fixed (byte* dstBase = data)
                    {
                        byte* dstPixels = dstBase + headerSize;
                        // DIB 自底向上：最后一行对应源图的第一行
                        for (int y = 0; y < height; y++)
                        {
                            byte* srcRow = srcBase + y * strideSrc;
                            byte* dstRow = dstPixels + (height - 1 - y) * strideDst;
                            Buffer.MemoryCopy(srcRow, dstRow, strideDst, strideDst);
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            return data;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveDialog = new SaveFileDialog
            {
                Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg)|*.jpg|Bitmap 图片 (*.bmp)|*.bmp",
                Title = "保存截图",
                FileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}",
                DefaultExt = ".png"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    ImageFormat format = ImageFormat.Png;
                    switch (Path.GetExtension(saveDialog.FileName).ToLower())
                    {
                        case ".jpg":
                        case ".jpeg":
                            format = ImageFormat.Jpeg;
                            break;
                        case ".bmp":
                            format = ImageFormat.Bmp;
                            break;
                    }

                    _resultBitmap.Save(saveDialog.FileName, format);
                    MessageBox.Show("图片保存成功", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _resultBitmap?.Dispose();
            // Clear image sources and panel to release references from WPF
            try
            {
                var panel = GetImagePanel();
                panel?.Children.Clear();
            }
            catch { }
            _fullBitmapSource = null;
        }

        private System.Windows.Controls.StackPanel GetImagePanel()
        {
            try
            {
                return this.FindName("ImagePanel") as System.Windows.Controls.StackPanel;
            }
            catch
            {
                return null;
            }
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}