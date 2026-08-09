using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Runtime.InteropServices;

namespace Recture
{
    public partial class SimpleSelectionOverlay : Window
    {
        public event EventHandler<SelectionCompletedEventArgs> SelectionCompleted;

        private Bitmap _screenshot;
        private System.Windows.Point _start;
        private bool _isSelecting = false;

        public SimpleSelectionOverlay(Bitmap screenshot)
        {
            InitializeComponent();
            _screenshot = screenshot;
            Loaded += SimpleSelectionOverlay_Loaded;
        }

        public void ShowSelection()
        {
            // Ensure window is maximized and visible
            WindowState = WindowState.Maximized;
            Topmost = true;
            Show();
            Activate();
            Focus();
        }

        private void SimpleSelectionOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            // Set image source
            IntPtr hBitmap = _screenshot.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                PreviewImage.Source = src;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);
            }

            MouseLeftButtonDown += SimpleSelectionOverlay_MouseLeftButtonDown;
            MouseMove += SimpleSelectionOverlay_MouseMove;
            MouseLeftButtonUp += SimpleSelectionOverlay_MouseLeftButtonUp;
            KeyDown += SimpleSelectionOverlay_KeyDown;
        }

        private void SimpleSelectionOverlay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                SelectionCompleted?.Invoke(this, new SelectionCompletedEventArgs(null));
            }
            else if (e.Key == Key.Enter)
            {
                FinishSelection();
            }
        }

        private void SimpleSelectionOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _start = e.GetPosition(this);
            _isSelecting = true;
            SelectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRect, _start.X);
            Canvas.SetTop(SelectionRect, _start.Y);
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            CaptureMouse();
        }

        private void SimpleSelectionOverlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting) return;

            var pos = e.GetPosition(this);
            double x = Math.Min(_start.X, pos.X);
            double y = Math.Min(_start.Y, pos.Y);
            double w = Math.Abs(pos.X - _start.X);
            double h = Math.Abs(pos.Y - _start.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = w;
            SelectionRect.Height = h;
        }

        private void SimpleSelectionOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting) return;
            _isSelecting = false;
            ReleaseMouseCapture();
            FinishSelection();
        }

        private void FinishSelection()
        {
            // Convert selection rect to screen pixels based on window scaling and position
            var rect = new System.Windows.Rect(Canvas.GetLeft(SelectionRect), Canvas.GetTop(SelectionRect), SelectionRect.Width, SelectionRect.Height);

            if (rect.Width <= 0 || rect.Height <= 0)
            {
                Close();
                SelectionCompleted?.Invoke(this, new SelectionCompletedEventArgs(null));
                return;
            }

            // The preview image is stretched uniformly within the window. Calculate mapping.
            var imgSource = PreviewImage.Source as BitmapSource;
            if (imgSource == null)
            {
                Close();
                SelectionCompleted?.Invoke(this, new SelectionCompletedEventArgs(null));
                return;
            }

            double imgAspect = (double)imgSource.PixelWidth / imgSource.PixelHeight;
            double winAspect = ActualWidth / ActualHeight;

            double displayWidth, displayHeight, offsetX, offsetY;

            if (imgAspect > winAspect)
            {
                // image constrained by width
                displayWidth = ActualWidth;
                displayHeight = ActualWidth / imgAspect;
                offsetX = 0;
                offsetY = (ActualHeight - displayHeight) / 2.0;
            }
            else
            {
                // constrained by height
                displayHeight = ActualHeight;
                displayWidth = ActualHeight * imgAspect;
                offsetY = 0;
                offsetX = (ActualWidth - displayWidth) / 2.0;
            }

            double relX = (rect.X - offsetX) / displayWidth;
            double relY = (rect.Y - offsetY) / displayHeight;
            double relW = rect.Width / displayWidth;
            double relH = rect.Height / displayHeight;

            // Convert to pixel coordinates in the original screenshot
            int selX = (int)Math.Round(relX * imgSource.PixelWidth);
            int selY = (int)Math.Round(relY * imgSource.PixelHeight);
            int selW = (int)Math.Round(relW * imgSource.PixelWidth);
            int selH = (int)Math.Round(relH * imgSource.PixelHeight);

            // Clamp
            selX = Math.Max(0, Math.Min(selX, _screenshot.Width - 1));
            selY = Math.Max(0, Math.Min(selY, _screenshot.Height - 1));
            selW = Math.Max(0, Math.Min(selW, _screenshot.Width - selX));
            selH = Math.Max(0, Math.Min(selH, _screenshot.Height - selY));

            var selection = new SelectionInfo
            {
                RedRect = new System.Drawing.Rectangle(selX, selY, selW, selH),
                BlueRect = System.Drawing.Rectangle.Empty
            };

            Close();
            SelectionCompleted?.Invoke(this, new SelectionCompletedEventArgs(selection));
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                _screenshot?.Dispose();
            }
            catch { }
            PreviewImage.Source = null;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
