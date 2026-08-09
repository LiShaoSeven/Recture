using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Recture
{
    public class HotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int MOD_ALT = 0x0001;
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_WIN = 0x0008;

        private readonly Window _ownerWindow;
        private IntPtr _windowHandle;
        private HwndSource _hwndSource;
        private readonly Dictionary<int, Action> _hotkeyActions = new Dictionary<int, Action>();
        private readonly Dictionary<int, Tuple<IntPtr, int, int>> _hotkeyRegistrations = new Dictionary<int, Tuple<IntPtr, int, int>>();
        private int _currentId = 100;

        public HotkeyManager(Window ownerWindow)
        {
            _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
            _windowHandle = new WindowInteropHelper(ownerWindow).Handle;
            if (_windowHandle != IntPtr.Zero)
            {
                InitializeHook();
            }
            else
            {
                ownerWindow.SourceInitialized += OwnerWindow_SourceInitialized;
            }
        }

        public void Dispose()
        {
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(HwndHook);
                _hwndSource = null;
            }

            try
            {
                UnregisterAll();
            }
            catch
            {
            }

            if (_ownerWindow != null)
            {
                _ownerWindow.SourceInitialized -= OwnerWindow_SourceInitialized;
            }
        }

        private void OwnerWindow_SourceInitialized(object sender, EventArgs e)
        {
            var w = (Window)sender;
            w.SourceInitialized -= OwnerWindow_SourceInitialized;
            _windowHandle = new WindowInteropHelper(_ownerWindow).Handle;
            InitializeHook();
        }

        private void InitializeHook()
        {
            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            if (_hwndSource != null)
            {
                _hwndSource.AddHook(HwndHook);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_hotkeyActions.TryGetValue(id, out Action action))
                {
                    try
                    {
                        action.Invoke();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Hotkey action error: {ex.Message}");
                    }
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public bool RegisterHotkey(int modifiers, Key key, Action action)
        {
            int virtualKey = KeyInterop.VirtualKeyFromKey(key);

            // Prevent duplicate registration in this manager
            foreach (var reg in _hotkeyRegistrations.Values)
            {
                if (reg.Item2 == modifiers && reg.Item3 == virtualKey)
                {
                    return false;
                }
            }

            int id = _currentId++;

            IntPtr hWndToUse = _windowHandle == IntPtr.Zero ? IntPtr.Zero : _windowHandle;

            if (NativeMethods.RegisterHotKey(hWndToUse, id, modifiers, virtualKey))
            {
                _hotkeyActions[id] = action;
                _hotkeyRegistrations[id] = Tuple.Create(hWndToUse, modifiers, virtualKey);
                return true;
            }

            return false;
        }

        public bool RegisterHotkey(HotkeyInfo hotkeyInfo, Action action)
        {
            return RegisterHotkey(hotkeyInfo.Modifiers, hotkeyInfo.Key, action);
        }

        public void UnregisterAll()
        {
            var ids = new List<int>(_hotkeyRegistrations.Keys);
            foreach (int id in ids)
            {
                if (_hotkeyRegistrations.TryGetValue(id, out var reg))
                {
                    try
                    {
                        NativeMethods.UnregisterHotKey(reg.Item1, id);
                    }
                    catch
                    {
                    }
                }
            }
            _hotkeyActions.Clear();
            _hotkeyRegistrations.Clear();
        }

        public bool IsHotkeyConflict(int modifiers, Key key)
        {
            int virtualKey = KeyInterop.VirtualKeyFromKey(key);

            // If this manager already has the hotkey registered, consider it a conflict
            foreach (var reg in _hotkeyRegistrations.Values)
            {
                if (reg.Item2 == modifiers && reg.Item3 == virtualKey)
                    return true;
            }

            IntPtr hWndForTest = _windowHandle == IntPtr.Zero ? IntPtr.Zero : _windowHandle;
            int testId = _currentId + 1000;

            if (NativeMethods.RegisterHotKey(hWndForTest, testId, modifiers, virtualKey))
            {
                NativeMethods.UnregisterHotKey(hWndForTest, testId);
                return false;
            }
            return true;
        }

        public bool IsHotkeyConflict(HotkeyInfo hotkeyInfo)
        {
            return IsHotkeyConflict(hotkeyInfo.Modifiers, hotkeyInfo.Key);
        }

        public static int GetModifierFlags(bool ctrl, bool alt, bool shift, bool win)
        {
            int flags = 0;
            if (ctrl) flags |= MOD_CONTROL;
            if (alt) flags |= MOD_ALT;
            if (shift) flags |= MOD_SHIFT;
            if (win) flags |= MOD_WIN;
            return flags;
        }

        public static string HotkeyToString(int modifiers, Key key)
        {
            List<string> parts = new List<string>();
            if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((modifiers & MOD_WIN) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join(" + ", parts);
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        }
    }

    public class HotkeyInfo
    {
        public int Modifiers { get; set; }
        public Key Key { get; set; }

        public HotkeyInfo() { }

        public HotkeyInfo(int modifiers, Key key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        public override string ToString()
        {
            return HotkeyManager.HotkeyToString(Modifiers, Key);
        }

        public override bool Equals(object obj)
        {
            if (obj is HotkeyInfo other)
            {
                return Modifiers == other.Modifiers && Key == other.Key;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Modifiers.GetHashCode() ^ Key.GetHashCode();
        }
    }
}