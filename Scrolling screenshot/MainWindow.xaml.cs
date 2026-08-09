﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Windows;
using System.Windows.Input;
using System.Drawing;
using System.Threading.Tasks;

namespace Recture
{
    public partial class MainWindow : Window
    {
        private HotkeyManager _hotkeyManager;
        private TrayIconManager _trayIconManager;
        private ICaptureEngine _captureEngine;
        private ManualPreviewWindow _manualPreviewWindow;
        private SelectionFrameOverlay _selectionFrameOverlay;

        private HotkeyInfo _startHotkey = new HotkeyInfo(HotkeyManager.GetModifierFlags(true, true, false, false), Key.F9);
        private HotkeyInfo _endHotkey = new HotkeyInfo(HotkeyManager.GetModifierFlags(true, true, false, false), Key.F10);

        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadHotkeysFromSettings();
            InitializeHotkeyManager();
            InitializeTrayIcon();
        }

        private void InitializeHotkeyManager()
        {
            _hotkeyManager = new HotkeyManager(this);
            RegisterHotkeys();
        }

        private void InitializeTrayIcon()
        {
            _trayIconManager = new TrayIconManager();
            //_trayIconManager.ShowBalloonTip("长截图工具", $"已启动\n开始截图: {_startHotkey}\n结束滚动: {_endHotkey}");
            _trayIconManager.ShowPreferences += TrayIconManager_ShowPreferences;
            _trayIconManager.ExitApplication += TrayIconManager_ExitApplication;
        }

        private void RegisterHotkeys()
        {
            _hotkeyManager.UnregisterAll();

            bool startSuccess = _hotkeyManager.RegisterHotkey(_startHotkey, StartScreenshot);
            bool endSuccess = _hotkeyManager.RegisterHotkey(_endHotkey, EndScrolling);

            if (!startSuccess)
            {
                _trayIconManager.ShowBalloonTip("警告", $"开始截图快捷键 {_startHotkey} 注册失败，可能与系统冲突", System.Windows.Forms.ToolTipIcon.Warning);
            }
            if (!endSuccess)
            {
                _trayIconManager.ShowBalloonTip("警告", $"结束滚动快捷键 {_endHotkey} 注册失败，可能与系统冲突", System.Windows.Forms.ToolTipIcon.Warning);
            }
        }

        private void StartScreenshot()
        {
            if (_captureEngine != null && _captureEngine.IsRunning)
                return;

            _trayIconManager.UpdateStatus("选择区域中...");

            // Capture the entire primary screen first and show a full-screen preview
            System.Drawing.Bitmap fullScreen = CaptureFullScreen();

            var preview = new SimpleSelectionOverlay(fullScreen);
            preview.SelectionCompleted += SelectionOverlay_SelectionCompleted;
            preview.ShowSelection();
        }

        private System.Drawing.Bitmap CaptureFullScreen()
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            var bounds = screen.Bounds;
            var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, System.Drawing.CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        private void SelectionOverlay_SelectionCompleted(object sender, SelectionCompletedEventArgs e)
        {
            if (e.Selection != null)
            {
                var sel = e.Selection;
                if (sel.BlueRect.IsEmpty || sel.BlueRect.Width == 0 || sel.BlueRect.Height == 0)
                {
                    int redH = sel.RedRect.Height;
                    int blueH = Math.Max(16, Math.Min(200, redH / 4));
                    sel.BlueRect = new System.Drawing.Rectangle(sel.RedRect.X, sel.RedRect.Y + Math.Max(0, redH - blueH), sel.RedRect.Width, blueH);
                }

                _trayIconManager.UpdateStatus("等待捕获... 按结束快捷键停止");

                StartCaptureEngine(sel);
            }
            else
            {
                _trayIconManager.UpdateStatus("已取消");
            }
        }

        private void StartCaptureEngine(SelectionInfo selection)
        {
            // 无论何种模式都先把选区框（无标题/无焦点）显示在屏幕上，方便用户观察
            _selectionFrameOverlay = new SelectionFrameOverlay();
            _selectionFrameOverlay.ShowForSelection(selection);

            // Determine mode from settings
            string mode = Properties.Settings.Default.CaptureMode;
            if (string.IsNullOrEmpty(mode)) mode = "Manual";

            System.Windows.Threading.DispatcherTimer uiTimer = null;

            if (mode == "Auto")
            {
                _captureEngine = new ScrollCaptureEngine();
            }
            else
            {
                var manual = new ManualCaptureEngine();
                // 从首选项读取手动截图按钮
                try
                {
                    var ck = Properties.Settings.Default.CaptureKey;
                    if (!string.IsNullOrEmpty(ck) && Enum.TryParse<Key>(ck, out var parsedKey))
                        manual.CaptureKey = parsedKey;
                }
                catch { }
                // 从首选项读取滚动截图按键
                try
                {
                    var sk = Properties.Settings.Default.ScrollCaptureKey;
                    if (!string.IsNullOrEmpty(sk) && Enum.TryParse<Key>(sk, out var parsedKey))
                        manual.ScrollCaptureKey = parsedKey;
                }
                catch { }
                _captureEngine = manual;

                // 截图进度预览（展示已拼接结果，而非当前选区实时图像）
                _manualPreviewWindow = new ManualPreviewWindow();
                uiTimer = new System.Windows.Threading.DispatcherTimer();
                uiTimer.Interval = TimeSpan.FromMilliseconds(300);
                uiTimer.Tick += (s, e) =>
                {
                    if (_captureEngine is ManualCaptureEngine me && _manualPreviewWindow != null)
                    {
                        int rw = me.ResultWidth;
                        int rh = me.ResultHeight;
                        // 高度>0 时才取快照，结果图可能很大，Clone 是必要的
                        if (rh > 0)
                        {
                            using (var bmp = me.GetCurrentResult())
                            {
                                if (bmp != null)
                                    _manualPreviewWindow.UpdatePreview(bmp);
                            }
                            _manualPreviewWindow.UpdateStatus(rh, rw, rh);
                        }
                    }
                    else
                    {
                        uiTimer?.Stop();
                    }
                };
                uiTimer.Start();
                _manualPreviewWindow.Show();
            }

            _captureEngine.CaptureCompleted += (s, e) =>
            {
                uiTimer?.Stop();
                CaptureEngine_CaptureCompleted(s, e);
            };
            _captureEngine.ProgressUpdated += CaptureEngine_ProgressUpdated;
            _captureEngine.StartCapture(selection);
        }

        private void CaptureEngine_ProgressUpdated(object sender, CaptureProgressEventArgs e)
        {
            _trayIconManager.UpdateStatus($"截图中: {e.CurrentHeight}px");
            if (_manualPreviewWindow != null && _captureEngine is ManualCaptureEngine me)
            {
                _manualPreviewWindow.UpdateStatus(e.CurrentHeight, me.ResultWidth, e.CurrentHeight);
            }
        }

        private void CaptureEngine_CaptureCompleted(object sender, CaptureCompletedEventArgs e)
        {
            // CaptureCompleted may be raised from a background thread.
            // Marshal UI work back to the Dispatcher (UI) thread to avoid STA exceptions.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _trayIconManager.UpdateStatus("完成");
                ShowResult(e.ResultBitmap);
                _captureEngine = null;
                if (_manualPreviewWindow != null)
                {
                    _manualPreviewWindow.Close();
                    _manualPreviewWindow = null;
                }
                if (_selectionFrameOverlay != null)
                {
                    _selectionFrameOverlay.CloseOverlay();
                    _selectionFrameOverlay = null;
                }
            }));
        }

        private void EndScrolling()
        {
            if (_captureEngine != null && _captureEngine.IsRunning)
            {
                _captureEngine.StopCapture();
            }
        }

        private void ShowResult(System.Drawing.Bitmap resultBitmap)
        {
            ResultWindow resultWindow = new ResultWindow(resultBitmap);
            resultWindow.Show();
        }

        private void TrayIconManager_ShowPreferences(object sender, EventArgs e)
        {
            PreferencesWindow preferences = new PreferencesWindow(_startHotkey, _endHotkey, _hotkeyManager);
            if (preferences.ShowDialog() == true)
            {
                _startHotkey = preferences.StartHotkey;
                _endHotkey = preferences.EndHotkey;
                SaveHotkeysToSettings();
                RegisterHotkeys();
                _trayIconManager.ShowBalloonTip("设置已更新", $"开始截图: {_startHotkey}\n结束滚动: {_endHotkey}");
            }
        }

        private void TrayIconManager_ExitApplication(object sender, EventArgs e)
        {
            _captureEngine?.StopCapture();
            _trayIconManager.Dispose();
            _hotkeyManager?.Dispose();
            Application.Current.Shutdown();
        }

        private void LoadHotkeysFromSettings()
        {
            try
            {
                if (!string.IsNullOrEmpty(Properties.Settings.Default.StartHotkey))
                {
                    string[] parts = Properties.Settings.Default.StartHotkey.Split('|');
                    _startHotkey = new HotkeyInfo(int.Parse(parts[0]), (Key)Enum.Parse(typeof(Key), parts[1]));
                }
                if (!string.IsNullOrEmpty(Properties.Settings.Default.EndHotkey))
                {
                    string[] parts = Properties.Settings.Default.EndHotkey.Split('|');
                    _endHotkey = new HotkeyInfo(int.Parse(parts[0]), (Key)Enum.Parse(typeof(Key), parts[1]));
                }
            }
            catch
            {
            }
        }

        private void SaveHotkeysToSettings()
        {
            Properties.Settings.Default.StartHotkey = $"{_startHotkey.Modifiers}|{_startHotkey.Key}";
            Properties.Settings.Default.EndHotkey = $"{_endHotkey.Modifiers}|{_endHotkey.Key}";
            Properties.Settings.Default.Save();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _hotkeyManager?.Dispose();
            _trayIconManager?.Dispose();
        }
    }
}