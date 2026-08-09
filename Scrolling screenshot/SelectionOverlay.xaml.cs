using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Recture
{
    public partial class SelectionOverlay : Window
    {
        public event EventHandler<SelectionCompletedEventArgs> SelectionCompleted;

        private bool _isDraggingHorizontal = false;
        private bool _isDraggingVertical = false;
        private Point _dragStartPoint; // in device-independent units (DIPs)

        // Selection rectangle measured in DIPs for UI; converted to pixels for capture
        private int _selectionHeight; // DIPs
        private int _selectionWidth; // DIPs
        private int _selectionX; // DIPs (window.Left)
        private int _selectionY; // DIPs (window.Top)
        private int _dividerX; // DIPs
        private int _dividerY; // DIPs
        private const int DividerThickness = 6; // DIPs

        // Transform matrices to convert between DIPs and device pixels
        private Matrix _transformToDevice = Matrix.Identity;
        private Matrix _transformFromDevice = Matrix.Identity;

        public SelectionOverlay()
        {
            InitializeComponent();
        }

        public void ShowSelection()
        {
            // Determine monitor to show on (primary for now)
            var screen = System.Windows.Forms.Screen.PrimaryScreen;

            // Show window first so PresentationSource and transforms are available
            Show();

            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                _transformToDevice = source.CompositionTarget.TransformToDevice;
                _transformFromDevice = source.CompositionTarget.TransformFromDevice;
            }

            double scaleX = _transformToDevice.M11;
            double scaleY = _transformToDevice.M22;

            // Compute selection size in device pixels first, then convert to DIPs for window sizing
            int selWidthPx = (int)(screen.Bounds.Width * 0.6);
            int selHeightPx = (int)(screen.Bounds.Height * 0.5);
            int selXpx = screen.Bounds.Left + (screen.Bounds.Width - selWidthPx) / 2;
            int selYpx = screen.Bounds.Top + (screen.Bounds.Height - selHeightPx) / 2;

            // Convert pixels to DIPs for WPF window coordinates
            _selectionWidth = (int)Math.Round(selWidthPx / scaleX);
            _selectionHeight = (int)Math.Round(selHeightPx / scaleY);
            _selectionX = (int)Math.Round(selXpx / scaleX);
            _selectionY = (int)Math.Round(selYpx / scaleY);

            _dividerY = _selectionHeight / 2;
            _dividerX = _selectionWidth / 2;

            Left = _selectionX;
            Top = _selectionY;
            Width = _selectionWidth;
            Height = _selectionHeight;

            UpdateAreas();
            Focus();
        }

        private void UpdateAreas()
        {
            // Set row/column sizes to position dividers
            if (this.Content is System.Windows.FrameworkElement)
            {
                var grid = this.Content as System.Windows.Controls.Grid;
                if (grid != null)
                {
                    // Row 0 height = dividerY, Row1 = DividerThickness, Row2 = remaining
                    grid.RowDefinitions[0].Height = new GridLength(_dividerY, GridUnitType.Pixel);
                    grid.RowDefinitions[1].Height = new GridLength(DividerThickness, GridUnitType.Pixel);
                    grid.RowDefinitions[2].Height = new GridLength(Math.Max(0, _selectionHeight - _dividerY - DividerThickness), GridUnitType.Pixel);

                    grid.ColumnDefinitions[0].Width = new GridLength(_dividerX, GridUnitType.Pixel);
                    grid.ColumnDefinitions[1].Width = new GridLength(DividerThickness, GridUnitType.Pixel);
                    grid.ColumnDefinitions[2].Width = new GridLength(Math.Max(0, _selectionWidth - _dividerX - DividerThickness), GridUnitType.Pixel);
                }
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Point clickPos = e.GetPosition(this);
            // Check for vertical divider hit
            if (clickPos.X >= _dividerX - 10 && clickPos.X <= _dividerX + 10)
            {
                _isDraggingVertical = true;
                _dragStartPoint = clickPos;
                Cursor = Cursors.SizeWE;
            }
            // Check for horizontal divider hit
            else if (clickPos.Y >= _dividerY - 10 && clickPos.Y <= _dividerY + 10)
            {
                _isDraggingHorizontal = true;
                _dragStartPoint = clickPos;
                Cursor = Cursors.SizeNS;
            }
            else
            {
                CompleteSelection();
            }
        }

        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDraggingVertical)
            {
                Point currentPos = e.GetPosition(this);
                int deltaX = (int)(currentPos.X - _dragStartPoint.X);
                _dividerX = Math.Max(50, Math.Min(_selectionWidth - 50 - DividerThickness, _dividerX + deltaX));
                _dragStartPoint = currentPos;
                UpdateAreas();
            }
            else if (_isDraggingHorizontal)
            {
                Point currentPos = e.GetPosition(this);
                int deltaY = (int)(currentPos.Y - _dragStartPoint.Y);
                _dividerY = Math.Max(50, Math.Min(_selectionHeight - 50 - DividerThickness, _dividerY + deltaY));
                _dragStartPoint = currentPos;
                UpdateAreas();
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_isDraggingVertical || _isDraggingHorizontal)
            {
                _isDraggingVertical = false;
                _isDraggingHorizontal = false;
                Cursor = Cursors.Cross;
            }
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                Close();
                SelectionCompleted?.Invoke(this, new SelectionCompletedEventArgs(null));
            }
            else if (e.Key == Key.Enter)
            {
                CompleteSelection();
            }
        }

        private void CompleteSelection()
        {
            // Convert selection (stored in DIPs) back to device pixels for screen capture
            double scaleX = _transformToDevice.M11;
            double scaleY = _transformToDevice.M22;

            int selXpx = (int)Math.Round(_selectionX * scaleX);
            int selYpx = (int)Math.Round(_selectionY * scaleY);
            int selWidthPx = Math.Max(0, (int)Math.Round(_selectionWidth * scaleX));
            int selHeightPx = Math.Max(0, (int)Math.Round(_selectionHeight * scaleY));

            int dividerYPx = (int)Math.Round(_dividerY * scaleY);
            int dividerThicknessPx = Math.Max(1, (int)Math.Round(DividerThickness * Math.Max(scaleX, scaleY)));

            // Ensure rectangles stay inside selection bounds
            dividerYPx = Math.Max(0, Math.Min(dividerYPx, selHeightPx - dividerThicknessPx));

            var selection = new SelectionInfo
            {
                RedRect = new System.Drawing.Rectangle(
                    selXpx, selYpx, selWidthPx, dividerYPx),
                BlueRect = new System.Drawing.Rectangle(
                    selXpx, selYpx + dividerYPx + dividerThicknessPx, selWidthPx, Math.Max(0, selHeightPx - dividerYPx - dividerThicknessPx))
            };

            Close();
            SelectionCompleted?.Invoke(this, new SelectionCompletedEventArgs(selection));
        }
    }

    public class SelectionInfo
    {
        public System.Drawing.Rectangle RedRect { get; set; }
        public System.Drawing.Rectangle BlueRect { get; set; }
    }

    public class SelectionCompletedEventArgs : EventArgs
    {
        public SelectionInfo Selection { get; }

        public SelectionCompletedEventArgs(SelectionInfo selection)
        {
            Selection = selection;
        }
    }
}