using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using KeyStats.Helpers;

namespace KeyStats.Services;

public enum InputEventSource
{
    Hook,
    RawInput
}

public enum MouseButtonKind
{
    Left,
    Right,
    Middle,
    SideBack,
    SideForward
}

/// <summary>
/// Secondary input capture channel built on Win32 Raw Input with RIDEV_INPUTSINK.
///
/// Why this exists: many games (and their anti-cheat drivers) detach low-level
/// keyboard/mouse hooks while a match is running, which makes the hook-based
/// statistics silently drop every event. Raw Input is a standard, non-injecting
/// Windows API: the system delivers events directly to a registered window, so it
/// keeps working in cases where SetWindowsHookEx does not.
/// </summary>
public sealed class RawInputService : IDisposable
{
    private static RawInputService? _instance;
    public static RawInputService Instance => _instance ??= new RawInputService();

    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDEV_INPUTSINK = 0x00000100;

    private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    private const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
    private const ushort HID_USAGE_GENERIC_KEYBOARD = 0x06;

    private const uint RIM_TYPEMOUSE = 0;
    private const uint RIM_TYPEKEYBOARD = 1;

    // RAWKEYBOARD flags
    private const ushort RI_KEY_BREAK = 0x01;
    private const ushort RI_KEY_E0 = 0x02;

    // RAWMOUSE button flags
    private const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    private const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    private const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    private const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
    private const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;
    private const ushort RI_MOUSE_WHEEL = 0x0400;
    private const ushort RI_MOUSE_HWHEEL = 0x0800;

    private readonly object _lock = new();
    private readonly byte[] _buffer = new byte[512];

    private HwndSource? _source;
    private GCHandle _bufferHandle;
    private bool _isRegistered;

    public bool IsRegistered
    {
        get
        {
            lock (_lock)
            {
                return _isRegistered;
            }
        }
    }

    public event Action<string>? KeyPressed;
    public event Action<MouseButtonKind>? MouseButtonPressed;
    public event Action<double>? MouseMoved;
    public event Action<int>? MouseWheelScrolled;

    private RawInputService() { }

    /// <summary>
    /// Creates the sink window and registers keyboard + mouse raw input.
    /// Must be called on a thread that owns a window message loop (the WPF UI thread).
    /// </summary>
    public bool Start()
    {
        lock (_lock)
        {
            if (_isRegistered)
            {
                return true;
            }

            try
            {
                _bufferHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);

                var parameters = new HwndSourceParameters("KeyStatsRawInputSink")
                {
                    WindowStyle = 0,
                    Width = 0,
                    Height = 0
                };

                _source = new HwndSource(parameters);
                _source.AddHook(WndProc);

                var devices = new RAWINPUTDEVICE[2];
                devices[0] = new RAWINPUTDEVICE
                {
                    usUsagePage = HID_USAGE_PAGE_GENERIC,
                    usUsage = HID_USAGE_GENERIC_KEYBOARD,
                    dwFlags = RIDEV_INPUTSINK,
                    hwndTarget = _source.Handle
                };
                devices[1] = new RAWINPUTDEVICE
                {
                    usUsagePage = HID_USAGE_PAGE_GENERIC,
                    usUsage = HID_USAGE_GENERIC_MOUSE,
                    dwFlags = RIDEV_INPUTSINK,
                    hwndTarget = _source.Handle
                };

                _isRegistered = RegisterRawInputDevices(
                    devices,
                    (uint)devices.Length,
                    (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));

                if (!_isRegistered)
                {
                    Debug.WriteLine($"RawInput: RegisterRawInputDevices failed ({Marshal.GetLastWin32Error()})");
                    ReleaseSourceLocked();
                    return false;
                }

                Debug.WriteLine("RawInput: sink registered");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RawInput: start failed - {ex.Message}");
                ReleaseSourceLocked();
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            ReleaseSourceLocked();
        }
    }

    private void ReleaseSourceLocked()
    {
        _isRegistered = false;

        if (_source != null)
        {
            try
            {
                _source.RemoveHook(WndProc);
                _source.Dispose();
            }
            catch
            {
                // Ignore teardown failures.
            }

            _source = null;
        }

        if (_bufferHandle.IsAllocated)
        {
            _bufferHandle.Free();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_INPUT)
        {
            try
            {
                HandleRawInput(lParam);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RawInput: processing failed - {ex.Message}");
            }
        }

        return IntPtr.Zero;
    }

    private void HandleRawInput(IntPtr hRawInput)
    {
        var headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
        var size = (uint)_buffer.Length;

        var written = GetRawInputData(hRawInput, RID_INPUT, _bufferHandle.AddrOfPinnedObject(), ref size, headerSize);
        if (written == uint.MaxValue || size < headerSize)
        {
            return;
        }

        var header = (RAWINPUTHEADER)Marshal.PtrToStructure(_bufferHandle.AddrOfPinnedObject(), typeof(RAWINPUTHEADER))!;
        var dataPtr = IntPtr.Add(_bufferHandle.AddrOfPinnedObject(), (int)headerSize);

        if (header.dwType == RIM_TYPEMOUSE)
        {
            var mouse = (RAWMOUSE)Marshal.PtrToStructure(dataPtr, typeof(RAWMOUSE))!;
            HandleMouse(mouse);
        }
        else if (header.dwType == RIM_TYPEKEYBOARD)
        {
            var keyboard = (RAWKEYBOARD)Marshal.PtrToStructure(dataPtr, typeof(RAWKEYBOARD))!;
            HandleKeyboard(keyboard);
        }
    }

    private void HandleKeyboard(RAWKEYBOARD keyboard)
    {
        if ((keyboard.Flags & RI_KEY_BREAK) != 0)
        {
            return;
        }

        if (keyboard.VKey == 0 || keyboard.VKey == 0xFF)
        {
            return;
        }

        var extendedFlag = (keyboard.Flags & RI_KEY_E0) != 0 ? 1u : 0u;
        var keyName = KeyNameMapper.GetKeyName(keyboard.VKey, keyboard.MakeCode, extendedFlag);
        KeyPressed?.Invoke(keyName);
    }

    private void HandleMouse(RAWMOUSE mouse)
    {
        if (mouse.lLastX != 0 || mouse.lLastY != 0)
        {
            var distance = Math.Sqrt(((double)mouse.lLastX * mouse.lLastX) + ((double)mouse.lLastY * mouse.lLastY));
            if (distance > 0)
            {
                MouseMoved?.Invoke(distance);
            }
        }

        var flags = mouse.usButtonFlags;
        if (flags == 0)
        {
            return;
        }

        if ((flags & RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
        {
            MouseButtonPressed?.Invoke(MouseButtonKind.Left);
        }

        if ((flags & RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
        {
            MouseButtonPressed?.Invoke(MouseButtonKind.Right);
        }

        if ((flags & RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
        {
            MouseButtonPressed?.Invoke(MouseButtonKind.Middle);
        }

        if ((flags & RI_MOUSE_BUTTON_4_DOWN) != 0)
        {
            MouseButtonPressed?.Invoke(MouseButtonKind.SideBack);
        }

        if ((flags & RI_MOUSE_BUTTON_5_DOWN) != 0)
        {
            MouseButtonPressed?.Invoke(MouseButtonKind.SideForward);
        }

        if ((flags & (RI_MOUSE_WHEEL | RI_MOUSE_HWHEEL)) != 0)
        {
            MouseWheelScrolled?.Invoke((short)mouse.usButtonData);
        }
    }

    public void Dispose()
    {
        Stop();
        _instance = null;
    }

    #region Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags;
        public ushort usButtonFlags;
        public ushort usButtonData;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    #endregion
}
