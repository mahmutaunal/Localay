using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace AlpWareStudio.Localay
{
    public static class KeyboardShortcuts
    {
        #region Events

        public static event EventHandler? StartStopRequested;
        public static event EventHandler? MonitorToggleRequested;
        public static event EventHandler? ChatBoxToggleRequested;
        public static event EventHandler? SettingsToggleRequested;
        public static event EventHandler? LogToggleRequested;
        public static event EventHandler? ClearSelectedAreaRequested;
        public static event EventHandler? ShowAreaRequested;
        // public static event EventHandler? MainWindowVisibilityToggleRequested;
        public static event EventHandler? SelectTranslationRegion;
        public static event EventHandler? ClearAreasRequested;
        public static event EventHandler? RetryTranslationRequested;
        public static event EventHandler? ToggleExcludeRegionsRequested;

        #endregion

        #region Registered Global Hotkeys

        // For registering hotkeys
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Constants for RegisterHotKey
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;

        // Every hotkey-bound function: its config name (as used by ConfigManager.GetHotKey) and the event it raises.
        // The RegisterHotKey id of each entry is its index + 1.
        private static readonly (string Name, Action Raise)[] _hotkeyFunctions =
        {
            ("Start/Stop", () => StartStopRequested?.Invoke(null, EventArgs.Empty)),
            ("Overlay", () => MonitorToggleRequested?.Invoke(null, EventArgs.Empty)),
            ("Setting", () => SettingsToggleRequested?.Invoke(null, EventArgs.Empty)),
            ("Log", () => LogToggleRequested?.Invoke(null, EventArgs.Empty)),
            ("Swap Languages", () => SwapLanguagesRequested?.Invoke(null, EventArgs.Empty)),
            ("Retry Translation", () => RetryTranslationRequested?.Invoke(null, EventArgs.Empty)),
            ("Select Exclude Region", () => ToggleExcludeRegionsRequested?.Invoke(null, EventArgs.Empty)),
        };

        private static readonly Dictionary<string, int> _keyCodeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> _keyNameMap = new Dictionary<int, string>();

        // Set while the Settings window is recording a new hotkey, so the keys reach it instead of firing actions
        private static volatile bool _suspended;
        // Refreshed on the UI thread when settings change.
        private static volatile Dictionary<string, (uint modifiers, int vkCode)> _parsedHotkeys = new Dictionary<string, (uint, int)>();

        private static IntPtr _mainWindowHandle = IntPtr.Zero;
        private static readonly HashSet<int> _registeredHotkeys = new();

        private static readonly object _hotkeyDispatchLock = new object();
        private static readonly Dictionary<string, DateTime> _lastHotkeyDispatchTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan _hotkeyDispatchThreshold = TimeSpan.FromMilliseconds(250);

        // Event for Multi selection area
        public static event EventHandler? SelectArea1Requested;
        public static event EventHandler? SelectArea2Requested;
        public static event EventHandler? SelectArea3Requested;
        public static event EventHandler? SelectArea4Requested;
        public static event EventHandler? SelectArea5Requested;
        public static event EventHandler? SwapLanguagesRequested;

        static KeyboardShortcuts()
        {
            // Initialize key code map
            for (int i = 0; i < 26; i++) // A-Z
            {
                char key = (char)('A' + i);
                _keyCodeMap[key.ToString()] = 0x41 + i;
            }

            for (int i = 0; i <= 9; i++) // 0-9
            {
                _keyCodeMap[i.ToString()] = 0x30 + i;
            }

            // Function keys
            for (int i = 1; i <= 12; i++)
            {
                _keyCodeMap[$"F{i}"] = 0x70 + i - 1;
            }

            // Other common keys
            _keyCodeMap["SPACE"] = 0x20;
            _keyCodeMap["TAB"] = 0x09;
            _keyCodeMap["ENTER"] = 0x0D;
            _keyCodeMap["ESCAPE"] = 0x1B;
            _keyCodeMap["BACKSPACE"] = 0x08;
            _keyCodeMap["INSERT"] = 0x2D;
            _keyCodeMap["DELETE"] = 0x2E;
            _keyCodeMap["HOME"] = 0x24;
            _keyCodeMap["END"] = 0x23;
            _keyCodeMap["PAGEUP"] = 0x21;
            _keyCodeMap["PAGEDOWN"] = 0x22;
            _keyCodeMap["LEFT"] = 0x25;
            _keyCodeMap["UP"] = 0x26;
            _keyCodeMap["RIGHT"] = 0x27;
            _keyCodeMap["DOWN"] = 0x28;

            foreach (var kvp in _keyCodeMap)
            {
                _keyNameMap[kvp.Value] = kvp.Key;
            }
        }

        public static IEnumerable<string> FunctionNames => _hotkeyFunctions.Select(f => f.Name);

        // Parse a config string such as "CTRL+SHIFT+G" or "F9". A modifier is required except for F1-F12.
        public static bool TryParseHotkey(string? hotkeyString, out uint modifiers, out int vkCode)
        {
            modifiers = 0;
            vkCode = 0;
            if (string.IsNullOrWhiteSpace(hotkeyString))
                return false;

            string[] parts = hotkeyString.Split('+');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToUpperInvariant())
                {
                    case "ALT":
                        modifiers |= MOD_ALT;
                        break;
                    case "CTRL":
                        modifiers |= MOD_CONTROL;
                        break;
                    case "SHIFT":
                        modifiers |= MOD_SHIFT;
                        break;
                    default:
                        return false;
                }
            }

            if (!_keyCodeMap.TryGetValue(parts[parts.Length - 1].Trim(), out vkCode))
                return false;

            return modifiers != 0 || IsFunctionKey(vkCode);
        }

        private static bool IsFunctionKey(int vkCode) => vkCode >= 0x70 && vkCode <= 0x7B; // F1-F12

        public static bool AreSameHotkey(string? a, string? b)
        {
            return TryParseHotkey(a, out uint modsA, out int vkA) &&
                   TryParseHotkey(b, out uint modsB, out int vkB) &&
                   modsA == modsB && vkA == vkB;
        }

        public enum HotkeyCaptureResult
        {
            Ok,
            ModifierOnly,   // only Ctrl/Alt/Shift held so far; keep waiting for the main key
            NeedsModifier,  // a plain key that isn't F1-F12
            Unsupported,    // a key we can't map, or the Windows key is held
        }

        // Turn a key press captured in WPF into a config string such as "CTRL+ALT+G"
        public static HotkeyCaptureResult TryBuildHotkey(ModifierKeys modifiers, Key key, out string hotkey)
        {
            hotkey = "";

            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
                return HotkeyCaptureResult.ModifierOnly;

            if ((modifiers & ModifierKeys.Windows) != 0 || key is Key.LWin or Key.RWin)
                return HotkeyCaptureResult.Unsupported;

            int vkCode = KeyInterop.VirtualKeyFromKey(key);
            if (!_keyNameMap.TryGetValue(vkCode, out string? keyName))
                return HotkeyCaptureResult.Unsupported;

            if (modifiers == ModifierKeys.None && !IsFunctionKey(vkCode))
                return HotkeyCaptureResult.NeedsModifier;

            hotkey = FormatModifiers(modifiers, "+") + keyName;
            return HotkeyCaptureResult.Ok;
        }

        // Modifier prefix in a fixed order, e.g. "CTRL+ALT+" (or "Ctrl + Alt + " for display)
        public static string FormatModifiers(ModifierKeys modifiers, string separator, bool forDisplay = false)
        {
            var sb = new System.Text.StringBuilder();
            if ((modifiers & ModifierKeys.Control) != 0)
                sb.Append(forDisplay ? "Ctrl" : "CTRL").Append(separator);
            if ((modifiers & ModifierKeys.Alt) != 0)
                sb.Append(forDisplay ? "Alt" : "ALT").Append(separator);
            if ((modifiers & ModifierKeys.Shift) != 0)
                sb.Append(forDisplay ? "Shift" : "SHIFT").Append(separator);
            return sb.ToString();
        }

        // Human-friendly form of a config string: "CTRL+ALT+G" -> "Ctrl + Alt + G"
        public static string FormatForDisplay(string? hotkeyString)
        {
            if (!TryParseHotkey(hotkeyString, out uint mods, out int vkCode))
                return hotkeyString ?? "";

            ModifierKeys modifierKeys = ModifierKeys.None;
            if ((mods & MOD_CONTROL) != 0) modifierKeys |= ModifierKeys.Control;
            if ((mods & MOD_ALT) != 0) modifierKeys |= ModifierKeys.Alt;
            if ((mods & MOD_SHIFT) != 0) modifierKeys |= ModifierKeys.Shift;

            string keyName = _keyNameMap[vkCode];
            if (keyName.Length > 1 && !IsFunctionKey(vkCode))
                keyName = char.ToUpperInvariant(keyName[0]) + keyName.Substring(1).ToLowerInvariant();

            return FormatModifiers(modifierKeys, " + ", forDisplay: true) + keyName;
        }

        // Stop handling hotkeys (and release the RegisterHotKey combos) while a new hotkey is being recorded
        public static void SuspendHotkeys()
        {
            if (_suspended)
                return;
            _suspended = true;
            if (_mainWindowHandle != IntPtr.Zero)
            {
                UnregisterAllHotkeys();
            }
        }

        public static void ResumeHotkeys()
        {
            if (!_suspended)
                return;
            _suspended = false;
            RegisterConfiguredHotkeys();
        }

        // Re-read hotkeys from config and re-register them
        public static void ReloadHotkeys()
        {
            RefreshHotkeys();
            RegisterConfiguredHotkeys();
        }

        // Re-read all hotkeys from config
        public static void RefreshHotkeys()
        {
            var hotkeys = new Dictionary<string, (uint, int)>();

            foreach (var (name, _) in _hotkeyFunctions)
            {
                ParseHotkey(hotkeys, name);
            }

            _parsedHotkeys = hotkeys;
        }

        private static void ParseHotkey(Dictionary<string, (uint, int)> hotkeys, string functionName)
        {
            string hotkeyString = ConfigManager.Instance.GetHotKey(functionName);
            if (string.IsNullOrEmpty(hotkeyString))
                return;

            if (TryParseHotkey(hotkeyString, out uint modifiers, out int vkCode))
            {
                hotkeys[functionName] = (modifiers, vkCode);
                Console.WriteLine($"Parsed hotkey for {functionName}: {hotkeyString} -> Modifiers: {modifiers}, VK: {vkCode}");
            }
            else
            {
                Console.WriteLine($"Could not parse hotkey '{hotkeyString}' for function {functionName}");
            }
        }

        public static void InitializeGlobalHotkeys() => ReloadHotkeys();

        // Set the main window handle for hotkey registration
        public static void SetMainWindowHandle(IntPtr mainWindowHandle)
        {
            _mainWindowHandle = mainWindowHandle;

            // Register hotkeys if we have a valid window handle
            if (_mainWindowHandle != IntPtr.Zero)
            {
                RegisterConfiguredHotkeys();
            }
        }

        private static void RegisterConfiguredHotkeys()
        {
            if (_mainWindowHandle == IntPtr.Zero)
                return;

            UnregisterAllHotkeys();
            if (_suspended || !ConfigManager.Instance.IsHotKeyEnabled()) return;

            for (int i = 0; i < _hotkeyFunctions.Length; i++)
            {
                RegisterFunctionHotkey(_hotkeyFunctions[i].Name, i + 1);
            }
        }

        private static void UnregisterAllHotkeys()
        {
            foreach (int id in _registeredHotkeys)
                UnregisterHotKey(_mainWindowHandle, id);
            _registeredHotkeys.Clear();
        }

        private static void RegisterFunctionHotkey(string functionName, int hotkeyId)
        {
            if (_parsedHotkeys.TryGetValue(functionName, out var hotkeyInfo))
            {
                uint modifiers = hotkeyInfo.modifiers | MOD_NOREPEAT;
                uint vkCode = (uint)hotkeyInfo.vkCode;

                if (RegisterHotKey(_mainWindowHandle, hotkeyId, modifiers, vkCode))
                {
                    _registeredHotkeys.Add(hotkeyId);
                    Console.WriteLine($"Registered {functionName} hotkey successfully");
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"Failed to register {functionName} hotkey. Error: {error}");
                }
            }
        }

        private static bool TryReserveHotkeyDispatch(string function)
        {
            DateTime now = DateTime.UtcNow;

            lock (_hotkeyDispatchLock)
            {
                if (_lastHotkeyDispatchTimes.TryGetValue(function, out DateTime lastDispatch) &&
                    (now - lastDispatch) < _hotkeyDispatchThreshold)
                {
                    Console.WriteLine($"Hotkey duplicate suppressed: {function}");
                    return false;
                }

                _lastHotkeyDispatchTimes[function] = now;
                return true;
            }
        }

        private static bool InvokeHotkeyAction(string function)
        {
            if (_suspended || !ConfigManager.Instance.IsHotKeyEnabled())
            {
                return false;
            }

            int index = Array.FindIndex(_hotkeyFunctions, f => f.Name == function);
            if (index < 0)
            {
                return false;
            }

            if (TryReserveHotkeyDispatch(function))
            {
                Console.WriteLine($"Hotkey detected: {function}");
                _hotkeyFunctions[index].Raise();
            }
            return true;
        }

        // Process WM_HOTKEY messages in the main window
        public static bool ProcessHotKey(IntPtr wParam)
        {
            int index = wParam.ToInt32() - 1;
            return _registeredHotkeys.Contains(index + 1) && index >= 0 && index < _hotkeyFunctions.Length && InvokeHotkeyAction(_hotkeyFunctions[index].Name);
        }

        // Run the action bound to a hotkey function name
        public static bool ProcessHandleHotKey(string function)
        {
            return InvokeHotkeyAction(function);
        }

        public static void CleanupGlobalHotkeys()
        {
            if (_mainWindowHandle != IntPtr.Zero) UnregisterAllHotkeys();
            _mainWindowHandle = IntPtr.Zero;
        }

        #endregion

        #region Standard Shortcut Handling

        // Handle shortcut keys - for regular window event handling
        public static bool HandleKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (!ConfigManager.Instance.IsHotKeyEnabled())
            {
                return false;
            }
            try
            {
                // Convert WPF key to virtual key code
                // With Alt held, WPF reports Key.System and puts the real key in SystemKey
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                int vkCode = KeyInterop.VirtualKeyFromKey(key);

                // Check modifiers
                uint modifiers = 0;
                if ((e.KeyboardDevice.Modifiers & ModifierKeys.Alt) != 0)
                    modifiers |= MOD_ALT;
                if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0)
                    modifiers |= MOD_CONTROL;
                if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0)
                    modifiers |= MOD_SHIFT;

                // Check if it matches any of our configured hotkeys
                foreach (var kvp in _parsedHotkeys)
                {
                    string function = kvp.Key;
                    var (configModifiers, configVkCode) = kvp.Value;

                    if (vkCode == configVkCode && modifiers == configModifiers)
                    {
                        // Invoke the associated event handler
                        if (ProcessHandleHotKey(function))
                        {
                            e.Handled = true;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling keyboard shortcut: {ex.Message}");
            }

            return false;
        }

        #endregion
    }
}