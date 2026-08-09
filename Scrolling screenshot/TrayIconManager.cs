using System;
using System.Windows.Forms;
using ContextMenu = System.Windows.Forms.ContextMenu;
using MenuItem = System.Windows.Forms.MenuItem;
using System.Drawing;

namespace Recture
{
    public class TrayIconManager : IDisposable
    {
        public event EventHandler ShowPreferences;
        public event EventHandler ExitApplication;

        private NotifyIcon _notifyIcon;
        private bool _isDisposed;

        public TrayIconManager()
        {
            InitializeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "Recture",
                Visible = true,
                Icon = LoadAppIcon() ?? Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath)
            };

            if (_notifyIcon.Icon == null)
            {
                _notifyIcon.Icon = new Icon(SystemIcons.Application, 40, 40);
            }

            var contextMenu = new ContextMenu();
            contextMenu.MenuItems.Add(new MenuItem("首选项", (s, e) => ShowPreferences?.Invoke(this, EventArgs.Empty)));
            contextMenu.MenuItems.Add(new MenuItem("-"));
            contextMenu.MenuItems.Add(new MenuItem("退出", (s, e) => ExitApplication?.Invoke(this, EventArgs.Empty)));

            _notifyIcon.ContextMenu = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowPreferences?.Invoke(this, EventArgs.Empty);
        }

        private Icon LoadAppIcon()
        {
            try
            {
                // Look for Recture.png in the application directory
                var exePath = System.Windows.Forms.Application.StartupPath;
                var pngPath = System.IO.Path.Combine(exePath, "Recture.png");
                if (System.IO.File.Exists(pngPath))
                {
                    using (var bmp = new Bitmap(pngPath))
                    {
                        return Icon.FromHandle(bmp.GetHicon());
                    }
                }
            }
            catch { }
            return null;
        }

        public void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            if (!_isDisposed && _notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(2000, title, message, icon);
            }
        }

        public void UpdateStatus(string status)
        {
            if (!_isDisposed && _notifyIcon != null)
            {
                _notifyIcon.Text = $"长截图工具 - {status}";
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
            }

            _isDisposed = true;
        }

        ~TrayIconManager()
        {
            Dispose(false);
        }
    }
}