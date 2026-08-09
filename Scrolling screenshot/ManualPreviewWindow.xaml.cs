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
        private SelectionInfo _selection;
        public ManualPreviewWindow(SelectionInfo selection)
        {
            InitializeComponent();
            _selection = selection;
            PositionWindowNearSelection();
        }

        private void PositionWindowNearSelection()
        {
            try
            {
                // Place window to the right of selection if space, otherwise left
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                int screenW = screen.Bounds.Width;
                int selRight = _selection.RedRect.Right;
                int preferredLeft = selRight + 10;

                if (preferredLeft + Width > screenW)
                {
                    Left = Math.Max(0, _selection.RedRect.Left - Width - 10);
                }
                else
                {
                    Left = preferredLeft;
                }
                Top = Math.Max(0, _selection.RedRect.Top);
            }
            catch { }
        }

        public void UpdatePreview(Bitmap bmp)
        {
            if (bmp == null) return;
            try
            {
                IntPtr h = bmp.GetHbitmap();
                var src = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                PreviewImage.Source = src;
                try { if (src.CanFreeze) src.Freeze(); } catch { }
                DeleteObject(h);
            }
            catch { }
        }

        public void UpdateStatus(int height)
        {
            StatusText.Text = $"高度: {height}px";
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
