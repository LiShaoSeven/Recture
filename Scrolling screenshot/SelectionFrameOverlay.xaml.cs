using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Recture
{
    public partial class SelectionFrameOverlay : Window
    {
        public SelectionFrameOverlay()
        {
            InitializeComponent();
            // 让窗口本身与 Canvas / Rectangle 都不参与命中测试，鼠标点击直接穿透到下方窗口
            OverlayCanvas.IsHitTestVisible = false;
            RedRectBorder.IsHitTestVisible = false;
        }

        public void ShowForSelection(SelectionInfo selection)
        {
            if (selection == null) return;
            var red = selection.RedRect;
            if (red.Width <= 0 || red.Height <= 0) return;

            // SelectionInfo 里的 RedRect 是物理像素，需要转 DIPs 才能给 WPF Window 使用
            // 转换矩阵必须在窗口拥有 PresentationSource 之后才能取到，所以先 Show 再设位置
            Show();

            Matrix fromDevice = Matrix.Identity;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
                fromDevice = source.CompositionTarget.TransformFromDevice;

            double scaleX = fromDevice.M11;
            double scaleY = fromDevice.M22;

            // 红框相对截图区域向外扩展 3px，防止红框边线被拼接到截图中
            double pad = 3.0;
            double left = red.Left * scaleX - pad;
            double top = red.Top * scaleY - pad;
            double width = red.Width * scaleX + pad * 2;
            double height = red.Height * scaleY + pad * 2;

            Left = left;
            Top = top;
            Width = width;
            Height = height;

            // 红框占满整个窗口
            Canvas.SetLeft(RedRectBorder, 0);
            Canvas.SetTop(RedRectBorder, 0);
            RedRectBorder.Width = width;
            RedRectBorder.Height = height;
        }

        public void CloseOverlay()
        {
            try { Close(); } catch { }
        }
    }
}
