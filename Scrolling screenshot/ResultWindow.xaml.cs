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
            using (MemoryStream ms = new MemoryStream())
            {
                _resultBitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;

                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = ms;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();

                Clipboard.SetImage(bitmapImage);
            }
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