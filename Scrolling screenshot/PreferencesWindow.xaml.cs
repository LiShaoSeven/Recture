using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Recture
{
    public partial class PreferencesWindow : Window
    {
        public HotkeyInfo StartHotkey { get; private set; }
        public HotkeyInfo EndHotkey { get; private set; }
        public Key CaptureKey { get; private set; }
        public Key ScrollCaptureKey { get; private set; }

        private HotkeyInfo _tempStartHotkey;
        private HotkeyInfo _tempEndHotkey;
        private Key _tempCaptureKey;
        private Key _tempScrollCaptureKey;
        private bool _isRecordingStart = false;
        private bool _isRecordingEnd = false;
        private bool _isRecordingCaptureKey = false;
        private bool _isRecordingScrollCaptureKey = false;
        private HotkeyManager _hotkeyManager;

        public PreferencesWindow(HotkeyInfo startHotkey, HotkeyInfo endHotkey, HotkeyManager hotkeyManager)
        {
            InitializeComponent();

            StartHotkey = startHotkey;
            EndHotkey = endHotkey;
            _tempStartHotkey = new HotkeyInfo(startHotkey.Modifiers, startHotkey.Key);
            _tempEndHotkey = new HotkeyInfo(endHotkey.Modifiers, endHotkey.Key);

            try
            {
                var ck = Properties.Settings.Default.CaptureKey;
                _tempCaptureKey = string.IsNullOrEmpty(ck) ? Key.Space : (Key)Enum.Parse(typeof(Key), ck);
            }
            catch { _tempCaptureKey = Key.Space; }
            CaptureKey = _tempCaptureKey;

            try
            {
                var sk = Properties.Settings.Default.ScrollCaptureKey;
                _tempScrollCaptureKey = string.IsNullOrEmpty(sk) ? Key.X : (Key)Enum.Parse(typeof(Key), sk);
            }
            catch { _tempScrollCaptureKey = Key.X; }
            ScrollCaptureKey = _tempScrollCaptureKey;

            _hotkeyManager = hotkeyManager;

            UpdateHotkeyTextBoxes();

            StartHotkeyButton.Click += StartHotkeyButton_Click;
            EndHotkeyButton.Click += EndHotkeyButton_Click;
            CaptureKeyButton.Click += CaptureKeyButton_Click;
            ScrollCaptureKeyButton.Click += ScrollCaptureKeyButton_Click;
            OKButton.Click += OKButton_Click;
            CancelButton.Click += CancelButton_Click;
            ResetButton.Click += ResetButton_Click;

            // Initialize capture mode radio buttons from settings
            var mode = Properties.Settings.Default.CaptureMode;
            if (string.IsNullOrEmpty(mode) || mode == "Manual")
            {
                ManualRadioButton.IsChecked = true;
            }
            else
            {
                AutoRadioButton.IsChecked = true;
            }

            // 开机启动：以注册表当前实际状态为准（避免外部删除/修改导致状态不一致）
            AutoStartCheckBox.IsChecked = IsAutoStartEnabled();
        }

        private const string AutoStartRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartValueName = "Recture";

        private static string GetExecutablePath()
        {
            // Assembly.Location 在某些部署下为空字符串，回退到 Process MainModule
            try
            {
                var loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc) && System.IO.File.Exists(loc))
                    return loc;
            }
            catch { }
            try
            {
                return Process.GetCurrentProcess().MainModule.FileName;
            }
            catch { }
            return null;
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryPath, false))
                {
                    if (key == null) return false;
                    var v = key.GetValue(AutoStartValueName) as string;
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch { return false; }
        }

        private static void SetAutoStartEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(AutoStartRegistryPath, true))
                {
                    if (key == null) return;
                    if (enabled)
                    {
                        var exe = GetExecutablePath();
                        if (string.IsNullOrEmpty(exe)) return;
                        key.SetValue(AutoStartValueName, $"\"{exe}\"");
                    }
                    else
                    {
                        // 删除值不存在时不会抛异常
                        key.DeleteValue(AutoStartValueName, false);
                    }
                }
            }
            catch { }
        }

        private void UpdateHotkeyTextBoxes()
        {
            StartHotkeyTextBox.Text = _tempStartHotkey.ToString();
            EndHotkeyTextBox.Text = _tempEndHotkey.ToString();
            CaptureKeyTextBox.Text = KeyToString(_tempCaptureKey);
            ScrollCaptureKeyTextBox.Text = KeyToString(_tempScrollCaptureKey);
            CheckConflicts();
        }

        private static string KeyToString(Key key)
        {
            return key.ToString();
        }

        private void CheckConflicts()
        {
            // 自身逻辑冲突：开始/结束快捷键相同必须阻止
            if (_tempStartHotkey.Equals(_tempEndHotkey))
            {
                ConflictMessage.Text = "错误：开始截图与结束滚动快捷键不能相同";
                ConflictMessage.Foreground = Brushes.Red;
                OKButton.IsEnabled = false;
                return;
            }

            // 手动模式下两个按键不能相同
            if (_tempCaptureKey == _tempScrollCaptureKey)
            {
                ConflictMessage.Text = "错误：手动截图按键与滚动截图按键不能相同";
                ConflictMessage.Foreground = Brushes.Red;
                OKButton.IsEnabled = false;
                return;
            }

            // 与其他程序冲突：仅提醒，不阻止用户保存
            var sb = new StringBuilder();
            // 与原始值相同则跳过检测（当前已生效，不算冲突）
            if (!_tempStartHotkey.Equals(StartHotkey) && _hotkeyManager.IsHotkeyConflict(_tempStartHotkey))
                sb.AppendLine($"警告：开始截图快捷键 {_tempStartHotkey} 可能已被其他程序占用，保存后可能无法生效。");
            if (!_tempEndHotkey.Equals(EndHotkey) && _hotkeyManager.IsHotkeyConflict(_tempEndHotkey))
                sb.AppendLine($"警告：结束滚动快捷键 {_tempEndHotkey} 可能已被其他程序占用，保存后可能无法生效。");

            if (sb.Length > 0)
            {
                ConflictMessage.Text = sb.ToString().TrimEnd();
                ConflictMessage.Foreground = Brushes.OrangeRed;
            }
            else
            {
                ConflictMessage.Text = "";
            }
            OKButton.IsEnabled = true;
        }

        private void StartHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            StartHotkeyButton.IsEnabled = false;
            StartHotkeyTextBox.Text = "请按下快捷键...";
            _isRecordingStart = true;
            Keyboard.Focus(this);
        }

        private void EndHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            EndHotkeyButton.IsEnabled = false;
            EndHotkeyTextBox.Text = "请按下快捷键...";
            _isRecordingEnd = true;
            Keyboard.Focus(this);
        }

        private void CaptureKeyButton_Click(object sender, RoutedEventArgs e)
        {
            CaptureKeyButton.IsEnabled = false;
            CaptureKeyTextBox.Text = "请按下按键（Esc 取消）...";
            _isRecordingCaptureKey = true;
            Keyboard.Focus(this);
        }

        private void ScrollCaptureKeyButton_Click(object sender, RoutedEventArgs e)
        {
            ScrollCaptureKeyButton.IsEnabled = false;
            ScrollCaptureKeyTextBox.Text = "请按下按键（Esc 取消）...";
            _isRecordingScrollCaptureKey = true;
            Keyboard.Focus(this);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (_isRecordingStart)
            {
                RecordHotkey(ref _tempStartHotkey, e.Key);
                _isRecordingStart = false;
                StartHotkeyButton.IsEnabled = true;
                UpdateHotkeyTextBoxes();
                e.Handled = true;
            }
            else if (_isRecordingEnd)
            {
                RecordHotkey(ref _tempEndHotkey, e.Key);
                _isRecordingEnd = false;
                EndHotkeyButton.IsEnabled = true;
                UpdateHotkeyTextBoxes();
                e.Handled = true;
            }
            else if (_isRecordingCaptureKey)
            {
                // Esc 取消录制
                if (e.Key == Key.Escape)
                {
                    _isRecordingCaptureKey = false;
                    CaptureKeyButton.IsEnabled = true;
                    UpdateHotkeyTextBoxes();
                    e.Handled = true;
                    return;
                }

                // 单独的修饰键不作为截图按钮
                if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                    e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                    e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                    e.Key == Key.LWin || e.Key == Key.RWin)
                {
                    return;
                }

                _tempCaptureKey = e.Key;
                _isRecordingCaptureKey = false;
                CaptureKeyButton.IsEnabled = true;
                UpdateHotkeyTextBoxes();
                e.Handled = true;
            }
            else if (_isRecordingScrollCaptureKey)
            {
                // Esc 取消录制
                if (e.Key == Key.Escape)
                {
                    _isRecordingScrollCaptureKey = false;
                    ScrollCaptureKeyButton.IsEnabled = true;
                    UpdateHotkeyTextBoxes();
                    e.Handled = true;
                    return;
                }

                // 单独的修饰键不作为截图按钮
                if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                    e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                    e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                    e.Key == Key.LWin || e.Key == Key.RWin)
                {
                    return;
                }

                _tempScrollCaptureKey = e.Key;
                _isRecordingScrollCaptureKey = false;
                ScrollCaptureKeyButton.IsEnabled = true;
                UpdateHotkeyTextBoxes();
                e.Handled = true;
            }
        }

        private void RecordHotkey(ref HotkeyInfo hotkey, Key key)
        {
            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool alt = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
            bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);

            int modifiers = HotkeyManager.GetModifierFlags(ctrl, alt, shift, win);

            if (modifiers == 0)
            {
                return;
            }

            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            hotkey = new HotkeyInfo(modifiers, key);
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            StartHotkey = _tempStartHotkey;
            EndHotkey = _tempEndHotkey;
            CaptureKey = _tempCaptureKey;
            ScrollCaptureKey = _tempScrollCaptureKey;

            // Save capture mode and capture key
            Properties.Settings.Default.CaptureMode = (ManualRadioButton.IsChecked == true) ? "Manual" : "Auto";
            Properties.Settings.Default.CaptureKey = _tempCaptureKey.ToString();
            Properties.Settings.Default.ScrollCaptureKey = _tempScrollCaptureKey.ToString();

            // 开机启动：写入/删除注册表，同时把布尔状态记入设置便于其他地方读
            bool autoStart = AutoStartCheckBox.IsChecked == true;
            SetAutoStartEnabled(autoStart);
            Properties.Settings.Default.AutoStartEnabled = autoStart;

            Properties.Settings.Default.Save();

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _tempStartHotkey = new HotkeyInfo(HotkeyManager.GetModifierFlags(true, true, false, false), Key.F9);
            _tempEndHotkey = new HotkeyInfo(HotkeyManager.GetModifierFlags(true, true, false, false), Key.F10);
            _tempCaptureKey = Key.Space;
            _tempScrollCaptureKey = Key.X;
            ManualRadioButton.IsChecked = true;
            AutoStartCheckBox.IsChecked = false;
            UpdateHotkeyTextBoxes();
        }
    }
}