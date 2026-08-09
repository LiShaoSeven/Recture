using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Recture
{
    public partial class PreferencesWindow : Window
    {
        public HotkeyInfo StartHotkey { get; private set; }
        public HotkeyInfo EndHotkey { get; private set; }

        private HotkeyInfo _tempStartHotkey;
        private HotkeyInfo _tempEndHotkey;
        private bool _isRecordingStart = false;
        private bool _isRecordingEnd = false;
        private HotkeyManager _hotkeyManager;

        public PreferencesWindow(HotkeyInfo startHotkey, HotkeyInfo endHotkey, HotkeyManager hotkeyManager)
        {
            InitializeComponent();

            StartHotkey = startHotkey;
            EndHotkey = endHotkey;
            _tempStartHotkey = new HotkeyInfo(startHotkey.Modifiers, startHotkey.Key);
            _tempEndHotkey = new HotkeyInfo(endHotkey.Modifiers, endHotkey.Key);
            _hotkeyManager = hotkeyManager;

            UpdateHotkeyTextBoxes();

            StartHotkeyButton.Click += StartHotkeyButton_Click;
            EndHotkeyButton.Click += EndHotkeyButton_Click;
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
        }

        private void UpdateHotkeyTextBoxes()
        {
            StartHotkeyTextBox.Text = _tempStartHotkey.ToString();
            EndHotkeyTextBox.Text = _tempEndHotkey.ToString();
            CheckConflicts();
        }

        private void CheckConflicts()
        {
            if (_tempStartHotkey.Equals(_tempEndHotkey))
            {
                ConflictMessage.Text = "错误：两个快捷键不能相同";
                OKButton.IsEnabled = false;
                return;
            }

            bool startConflict = _hotkeyManager.IsHotkeyConflict(_tempStartHotkey);
            bool endConflict = _hotkeyManager.IsHotkeyConflict(_tempEndHotkey);

            if (startConflict && endConflict)
            {
                ConflictMessage.Text = "错误：两个快捷键都与系统冲突";
                OKButton.IsEnabled = false;
            }
            else if (startConflict)
            {
                ConflictMessage.Text = "错误：开始截图快捷键与系统冲突";
                OKButton.IsEnabled = false;
            }
            else if (endConflict)
            {
                ConflictMessage.Text = "错误：结束滚动快捷键与系统冲突";
                OKButton.IsEnabled = false;
            }
            else
            {
                ConflictMessage.Text = "";
                OKButton.IsEnabled = true;
            }
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

            // Save capture mode
            Properties.Settings.Default.CaptureMode = (ManualRadioButton.IsChecked == true) ? "Manual" : "Auto";
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
            ManualRadioButton.IsChecked = true;
            UpdateHotkeyTextBoxes();
        }
    }
}