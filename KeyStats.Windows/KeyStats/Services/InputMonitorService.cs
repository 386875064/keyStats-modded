using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using KeyStats.Helpers;

namespace KeyStats.Services;

public class InputMonitorService : IDisposable
{
    private static InputMonitorService? _instance;
    public static InputMonitorService Instance => _instance ??= new InputMonitorService();

    private IntPtr _keyboardHookId = IntPtr.Zero;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private NativeInterop.LowLevelKeyboardProc? _keyboardProc;
    private NativeInterop.LowLevelMouseProc? _mouseProc;

    private bool _isMonitoring;
    private readonly HashSet<int> _pressedKeys = new(); // Tracks currently held keys to avoid double-counting on key repeats.
    private readonly double _mouseSampleInterval = 1.0 / 30.0; // 30 FPS
    private DateTime _lastMouseSampleTime = DateTime.MinValue;
    private System.Drawing.Point? _lastMousePosition;
    private double _accumulatedDistance = 0.0;

    // Dedicated hook thread so UI-thread stalls cannot cause hook callback timeouts.
    private Thread? _hookThread;
    private uint _hookThreadId;

    // Hook health watchdog: detects whether Windows has silently removed our hooks.
    private Timer? _watchdogTimer;
    private int _isReinstallingHooks;
    private NativeInterop.POINT _lastCursorPos;
    private const int WatchdogIntervalMs = 3000;
    private const int HookDeadThresholdMs = 5000;
    private const int HookInstallReadyTimeoutMs = 5000;
    private const int HookThreadStopTimeoutMs = 2000;
    private const int HookReinstallRetryCount = 3;
    private const int HookReinstallRetryDelayMs = 750;

    // ===== Raw Input 通道 =====
    // 游戏反作弊（LOL 的 TP/ACE、Vanguard 等）会让低级钩子静默失效，
    // Raw Input 由系统直接投递到注册窗口，是可靠的第二通道。
    //
    // 判定依据不是"钩子静默多久"（那样人离开电脑也会误判），而是"这一次输入钩子有没有收到"：
    // Raw Input 事件到达时，若钩子在过去 RawCoincidenceWindowMs 内也收到过事件，
    // 说明同一次输入走的是钩子，Raw 丢弃（不重复计数）；否则说明钩子漏了这次输入 → Raw 处理。
    // 接管后钩子持续有事件多久算恢复（交给 watchdog 评估，避免来回抖动）
    private const int HookRecoveryConfirmMs = 5000;
    private IntPtr _rawInputWindow = IntPtr.Zero;
    private IntPtr _rawInputBuffer = IntPtr.Zero;
    private int _rawInputBufferSize;
    private NativeInterop.RawInputWndProc? _rawInputWndProc;
    private string? _rawInputClassName;
    private readonly HashSet<int> _rawPressedKeys = new();
    private volatile bool _rawInputReady;
    private volatile bool _rawTakeoverActive;
    private int _lastHookCallbackTick;
    private int _wndProcLogBudget = 8;
    private int _rawEventLogBudget = 5;

    // ===== 诊断状态（输入诊断窗口用）=====
    public bool IsMonitoring => _isMonitoring;
    public bool IsRawInputReady => _rawInputReady;
    public bool IsRawTakeover => _rawInputReady;
    public string CaptureChannel => _rawInputReady ? "Raw Input" : "Hook";

    private long _lastInputTicks;
    private string? _lastInputSummary;

    public DateTime? LastInputUtc
    {
        get
        {
            var ticks = Volatile.Read(ref _lastInputTicks);
            return ticks > 0 ? new DateTime(ticks, DateTimeKind.Local) : (DateTime?)null;
        }
    }

    public string? LastInputSummary => Volatile.Read(ref _lastInputSummary);

    private void TouchInput(string? summary)
    {
        Volatile.Write(ref _lastInputTicks, DateTime.Now.Ticks);
        if (summary != null)
        {
            Volatile.Write(ref _lastInputSummary, summary);
        }
    }

    public event Action<string, string, string>? KeyPressed;    public event Action<string, string>? LeftMouseClicked;
    public event Action<string, string>? RightMouseClicked;
    public event Action<string, string>? MiddleMouseClicked;
    public event Action<string, string>? SideBackMouseClicked;
    public event Action<string, string>? SideForwardMouseClicked;
    public event Action<double>? MouseMoved;
    public event Action<double, string, string>? MouseScrolled;

    private InputMonitorService() { }

    public void StartMonitoring()
    {
        if (_isMonitoring) return;

        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
        ResetTransientState();

        _lastHookCallbackTick = Environment.TickCount;
        StartHookThread();

        _isMonitoring = true;

        // Start the watchdog timer that periodically checks whether the hooks are still alive.
        _watchdogTimer = new Timer(WatchdogCallback, null, WatchdogIntervalMs, WatchdogIntervalMs);

        Debug.WriteLine("Input monitoring started successfully (dedicated hook thread)");
    }

    private void InstallHooks()
    {
        var moduleHandle = NativeInterop.GetModuleHandle(null);

        _keyboardHookId = NativeInterop.SetWindowsHookEx(
            NativeInterop.WH_KEYBOARD_LL,
            _keyboardProc!,
            moduleHandle,
            0);

        if (_keyboardHookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Debug.WriteLine($"Failed to install keyboard hook. Error code: {error}");
            throw new System.ComponentModel.Win32Exception(error, "Failed to install keyboard hook");
        }

        _mouseHookId = NativeInterop.SetWindowsHookEx(
            NativeInterop.WH_MOUSE_LL,
            _mouseProc!,
            moduleHandle,
            0);

        if (_mouseHookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Debug.WriteLine($"Failed to install mouse hook. Error code: {error}");
            if (_keyboardHookId != IntPtr.Zero)
            {
                NativeInterop.UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
            }
            throw new System.ComponentModel.Win32Exception(error, "Failed to install mouse hook");
        }
    }

    // ===== Raw Input 通道实现 =====

    private enum MouseButtonKind
    {
        Left,
        Right,
        Middle,
        SideBack,
        SideForward
    }

    private void StartRawInput()
    {
        try
        {
            WriteChannelLog("=== 开始初始化 Raw Input ===", reset: true);
            _rawInputWndProc = RawInputWndProc;
            var moduleHandle = NativeInterop.GetModuleHandle(null);
            _rawInputClassName = "KeyStatsRawInputSink_" + Guid.NewGuid().ToString("N");

            var windowClass = new NativeInterop.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeInterop.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_rawInputWndProc),
                hInstance = moduleHandle,
                lpszClassName = _rawInputClassName
            };
            var classAtom = NativeInterop.RegisterClassEx(ref windowClass);
            if (classAtom == 0)
            {
                WriteChannelLog($"RegisterClassEx 失败 err={Marshal.GetLastWin32Error()} cbSize={windowClass.cbSize} structSize={Marshal.SizeOf<NativeInterop.WNDCLASSEX>()}");
                return;
            }

            // 必须是普通窗口（不能用 HWND_MESSAGE），否则收不到 WM_INPUT；零尺寸且不显示。
            _rawInputWindow = NativeInterop.CreateWindowEx(
                0, _rawInputClassName, string.Empty, 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, moduleHandle, IntPtr.Zero);
            if (_rawInputWindow == IntPtr.Zero)
            {
                WriteChannelLog($"CreateWindowEx 失败 err={Marshal.GetLastWin32Error()} class={_rawInputClassName}");
                return;
            }

            var devices = new[]
            {
                new NativeInterop.RAWINPUTDEVICE
                {
                    usUsagePage = 0x01,
                    usUsage = 0x06, // 键盘
                    dwFlags = NativeInterop.RIDEV_INPUTSINK,
                    hwndTarget = _rawInputWindow
                },
                new NativeInterop.RAWINPUTDEVICE
                {
                    usUsagePage = 0x01,
                    usUsage = 0x02, // 鼠标
                    dwFlags = NativeInterop.RIDEV_INPUTSINK,
                    hwndTarget = _rawInputWindow
                }
            };

            if (!NativeInterop.RegisterRawInputDevices(
                    devices,
                    (uint)devices.Length,
                    (uint)Marshal.SizeOf<NativeInterop.RAWINPUTDEVICE>()))
            {
                WriteChannelLog($"RegisterRawInputDevices 失败 err={Marshal.GetLastWin32Error()} hwnd=0x{_rawInputWindow.ToInt64():X} size={Marshal.SizeOf<NativeInterop.RAWINPUTDEVICE>()}");
                return;
            }

            _rawInputReady = true;
            WriteChannelLog($"Raw Input 就绪 hwnd=0x{_rawInputWindow.ToInt64():X}");
            Debug.WriteLine("Raw Input channel ready (game fallback)");
        }
        catch (Exception ex)
        {
            WriteChannelLog("Raw Input 初始化异常: " + ex);
            Debug.WriteLine($"Raw Input initialization failed: {ex}");
        }
    }

    /// <summary>把采集通道的初始化结果写到临时目录，便于排查"为什么还在用钩子"。</summary>
    private static void WriteChannelLog(string message, bool reset = false)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "keystats_channel.log");
            var line = $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}";
            if (reset)
            {
                System.IO.File.WriteAllText(path, line);
            }
            else
            {
                System.IO.File.AppendAllText(path, line);
            }
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }

    private IntPtr RawInputWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_wndProcLogBudget > 0)
        {
            _wndProcLogBudget--;
            WriteChannelLog($"wndproc msg=0x{msg:X4} hwnd=0x{hWnd.ToInt64():X}");
        }

        if (msg == NativeInterop.WM_INPUT)
        {
            try
            {
                ProcessRawInput(lParam);
            }
            catch (Exception ex)
            {
                WriteChannelLog("Raw Input 处理失败: " + ex.GetType().Name + " " + ex.Message);
            }
            return IntPtr.Zero;
        }

        if (msg == NativeInterop.WM_CLOSE)
        {
            NativeInterop.DestroyWindow(hWnd);
            if (_rawInputWindow == hWnd)
            {
                _rawInputWindow = IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        return NativeInterop.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ProcessRawInput(IntPtr hRawInput)
    {
        if (_rawEventLogBudget > 0)
        {
            _rawEventLogBudget--;
            WriteChannelLog("WM_INPUT 到达");
        }

        // 钩子活着时直接返回：桌面状态下 Raw 通道只花一次 TickCount 比较，
        // 不做 GetRawInputData、不分配内存。
        if (!ShouldRawInputHandleEvent())
        {
            return;
        }

        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<NativeInterop.RAWINPUTHEADER>();
        if (NativeInterop.GetRawInputData(hRawInput, NativeInterop.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
        {
            return;
        }

        if ((int)size > _rawInputBufferSize)
        {
            if (_rawInputBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_rawInputBuffer);
            }
            _rawInputBuffer = Marshal.AllocHGlobal((int)size);
            _rawInputBufferSize = (int)size;
        }

        if (NativeInterop.GetRawInputData(hRawInput, NativeInterop.RID_INPUT, _rawInputBuffer, ref size, headerSize) != size)
        {
            return;
        }

        var header = Marshal.PtrToStructure<NativeInterop.RAWINPUTHEADER>(_rawInputBuffer);
        var dataPointer = IntPtr.Add(_rawInputBuffer, Marshal.SizeOf<NativeInterop.RAWINPUTHEADER>());

        if (header.dwType == NativeInterop.RIM_TYPEKEYBOARD)
        {
            HandleRawKeyboard(Marshal.PtrToStructure<NativeInterop.RAWKEYBOARD>(dataPointer));
        }
        else if (header.dwType == NativeInterop.RIM_TYPEMOUSE)
        {
            HandleRawMouse(Marshal.PtrToStructure<NativeInterop.RAWMOUSE>(dataPointer));
        }
    }

    /// <summary>
    /// Raw Input 作为主通道：只要注册成功就由它记账，钩子退化为兜底（Raw 注册失败时才会用到）。
    /// 之前按"钩子是否收到这次输入"仲裁，游戏里鼠标钩子还活着会让键盘永远无法接管，已废弃该判定。
    /// </summary>
    private bool ShouldRawInputHandleEvent()
    {
        if (!_rawInputReady)
        {
            return false;
        }

        if (!_rawTakeoverActive)
        {
            _rawTakeoverActive = true;
            lock (_rawPressedKeys)
            {
                _rawPressedKeys.Clear();
            }
            Debug.WriteLine("Raw Input is the primary capture channel");
        }

        return true;
    }

    private void HandleRawKeyboard(NativeInterop.RAWKEYBOARD keyboard)
    {
        var vkCode = (int)keyboard.VKey;
        if (vkCode == 0xFF)
        {
            // Raw Input 偶尔给不出虚拟键码，用扫描码反查
            vkCode = (int)NativeInterop.MapVirtualKey(keyboard.MakeCode, NativeInterop.MAPVK_VSC_TO_VK_EX);
        }
        if (vkCode <= 0)
        {
            return;
        }

        var isBreak = (keyboard.Flags & NativeInterop.RI_KEY_BREAK) != 0;
        if (isBreak)
        {
            lock (_rawPressedKeys)
            {
                _rawPressedKeys.Remove(vkCode);
            }
            return;
        }

        bool isNewKey;
        lock (_rawPressedKeys)
        {
            isNewKey = _rawPressedKeys.Add(vkCode);
        }
        if (!isNewKey)
        {
            return;
        }

        DispatchKeyPressed(KeyNameMapper.GetKeyName(vkCode, keyboard.MakeCode, ToHookFlags(keyboard.Flags)));
    }

    /// <summary>
    /// Raw Input 的 Flags 位含义（RI_KEY_BREAK/E0/E1）与低级钩子的 LLKHF_* 不同，
    /// 键名映射只认 LLKHF_EXTENDED(0x01)，这里做一次转换，否则小键盘 Enter 等扩展键会认错。
    /// </summary>
    private static uint ToHookFlags(ushort rawFlags)
    {
        return (rawFlags & NativeInterop.RI_KEY_E0) != 0 ? 0x01u : 0u;
    }

    private void HandleRawMouse(NativeInterop.RAWMOUSE mouse)
    {
        var buttonFlags = mouse.usButtonFlags;

        if ((buttonFlags & NativeInterop.RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
        {
            DispatchMouseClick(MouseButtonKind.Left);
        }
        if ((buttonFlags & NativeInterop.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
        {
            DispatchMouseClick(MouseButtonKind.Right);
        }
        if ((buttonFlags & NativeInterop.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
        {
            DispatchMouseClick(MouseButtonKind.Middle);
        }
        if ((buttonFlags & NativeInterop.RI_MOUSE_BUTTON_4_DOWN) != 0)
        {
            DispatchMouseClick(MouseButtonKind.SideBack);
        }
        if ((buttonFlags & NativeInterop.RI_MOUSE_BUTTON_5_DOWN) != 0)
        {
            DispatchMouseClick(MouseButtonKind.SideForward);
        }

        if ((buttonFlags & NativeInterop.RI_MOUSE_WHEEL) != 0)
        {
            var delta = unchecked((short)mouse.usButtonData);
            if (delta != 0)
            {
                DispatchScroll(Math.Abs(delta) / 120.0);
            }
        }

        if ((buttonFlags & NativeInterop.RI_MOUSE_HWHEEL) != 0)
        {
            var delta = unchecked((short)mouse.usButtonData);
            if (delta != 0)
            {
                DispatchScroll(Math.Abs(delta) / 120.0);
            }
        }

        // 绝对定位设备（触控板 / 远程桌面）给的是 0~65535 坐标而不是位移，累计会失真
        if ((mouse.usFlags & NativeInterop.MOUSE_MOVE_ABSOLUTE) != 0)
        {
            return;
        }

        if (mouse.lLastX != 0 || mouse.lLastY != 0)
        {
            AccumulateMouseDistance(Math.Sqrt(
                mouse.lLastX * (double)mouse.lLastX + mouse.lLastY * (double)mouse.lLastY));
        }
    }

    private void DispatchKeyPressed(string keyName)
    {
        TouchInput($"按键 {keyName}");
        var hWnd = NativeInterop.GetForegroundWindow();
        NativeInterop.GetWindowThreadProcessId(hWnd, out uint pid);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            InvokeSubscriber("KeyPressed", () =>
            {
                var activeApp = ActiveWindowManager.ResolveAppInfo(hWnd, pid);
                KeyPressed?.Invoke(keyName, activeApp.AppName, activeApp.DisplayName);
            });
        });
    }

    private void DispatchMouseClick(MouseButtonKind kind)
    {
        TouchInput(kind switch
        {
            MouseButtonKind.Left => "鼠标左键",
            MouseButtonKind.Right => "鼠标右键",
            MouseButtonKind.Middle => "鼠标中键",
            MouseButtonKind.SideBack => "鼠标侧键（后退）",
            _ => "鼠标侧键（前进）"
        });
        var hWnd = NativeInterop.GetForegroundWindow();
        NativeInterop.GetWindowThreadProcessId(hWnd, out uint pid);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            InvokeSubscriber("MouseClicked", () =>
            {
                var activeApp = ActiveWindowManager.ResolveAppInfo(hWnd, pid);
                var appName = activeApp.AppName;
                var displayName = activeApp.DisplayName;
                switch (kind)
                {
                    case MouseButtonKind.Left:
                        LeftMouseClicked?.Invoke(appName, displayName);
                        break;
                    case MouseButtonKind.Right:
                        RightMouseClicked?.Invoke(appName, displayName);
                        break;
                    case MouseButtonKind.Middle:
                        MiddleMouseClicked?.Invoke(appName, displayName);
                        break;
                    case MouseButtonKind.SideForward:
                        SideForwardMouseClicked?.Invoke(appName, displayName);
                        break;
                    default:
                        SideBackMouseClicked?.Invoke(appName, displayName);
                        break;
                }
            });
        });
    }

    private void DispatchScroll(double scrollDistance)
    {
        TouchInput(null);
        var hWnd = NativeInterop.GetForegroundWindow();
        NativeInterop.GetWindowThreadProcessId(hWnd, out uint pid);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            InvokeSubscriber("MouseScrolled", () =>
            {
                var activeApp = ActiveWindowManager.ResolveAppInfo(hWnd, pid);
                MouseScrolled?.Invoke(scrollDistance, activeApp.AppName, activeApp.DisplayName);
            });
        });
    }

    private void ReinstallHooks()
    {
        Debug.WriteLine("Watchdog: reinstalling hooks...");

        if (!TryStopHookThread())
        {
            throw new InvalidOperationException("Existing hook thread did not stop within the timeout.");
        }

        if (_keyboardHookId != IntPtr.Zero)
        {
            NativeInterop.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
        if (_mouseHookId != IntPtr.Zero)
        {
            NativeInterop.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }

        _lastHookCallbackTick = Environment.TickCount;
        StartHookThread();
        ResetTransientState();
        Debug.WriteLine("Watchdog: hooks reinstalled");
    }

    private void StartHookThread()
    {
        var hookStartup = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _hookThread = new Thread(() =>
        {
            try
            {
                _hookThreadId = NativeInterop.GetCurrentThreadId();
                InstallHooks();
                StartRawInput();
                hookStartup.TrySetResult(null);

                while (NativeInterop.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                    NativeInterop.TranslateMessage(ref msg);
                    NativeInterop.DispatchMessage(ref msg);
                }
            }
            catch (Exception ex)
            {
                hookStartup.TrySetResult(ex);
            }
        });
        _hookThread.IsBackground = true;
        _hookThread.Name = "InputHookThread";
        _hookThread.Start();

        if (!hookStartup.Task.Wait(HookInstallReadyTimeoutMs))
        {
            if (!TryStopHookThread())
            {
                throw new TimeoutException($"Timed out waiting {HookInstallReadyTimeoutMs}ms for hook installation, and the hook thread did not stop within {HookThreadStopTimeoutMs}ms.");
            }

            throw new TimeoutException($"Timed out waiting {HookInstallReadyTimeoutMs}ms for hook installation.");
        }

        var hookError = hookStartup.Task.Result;
        if (hookError != null)
        {
            if (!TryStopHookThread())
            {
                throw new InvalidOperationException($"Hook installation failed and the hook thread did not stop within {HookThreadStopTimeoutMs}ms.", hookError);
            }

            throw hookError;
        }
    }

    private void WatchdogCallback(object? state)
    {
        if (!_isMonitoring) return;

        if (_rawTakeoverActive)
        {
            // 钩子重新开始收事件（退出游戏）→ 交回钩子；仍无事件则继续保持接管，
            // 反作弊环境下反复重装钩子没有意义，还会造成计数空隙。
            var sinceHook = unchecked((uint)(Environment.TickCount - Volatile.Read(ref _lastHookCallbackTick)));
            if (sinceHook >= HookRecoveryConfirmMs)
            {
                return;
            }

            _rawTakeoverActive = false;
            Debug.WriteLine("Hooks recovered, Raw Input standing by");
        }

        NativeInterop.GetCursorPos(out var currentPos);
        var cursorMoved = currentPos.x != _lastCursorPos.x || currentPos.y != _lastCursorPos.y;
        _lastCursorPos = currentPos;
        var keyboardActivity = HasRecentKeyboardActivity();

        if (!cursorMoved && !keyboardActivity) return;

        // The cursor is moving but no hook callback has fired in a while → Windows likely removed the hooks silently.
        var elapsed = unchecked((uint)(Environment.TickCount - Volatile.Read(ref _lastHookCallbackTick)));
        if (elapsed > HookDeadThresholdMs)
        {
            if (Interlocked.CompareExchange(ref _isReinstallingHooks, 1, 0) != 0)
            {
                return;
            }

            Debug.WriteLine($"Watchdog: hook appears dead (no callback for {elapsed}ms), reinstalling...");
            try
            {
                RetryHookRecovery("watchdog");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Watchdog: recovery failed after retries. {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _isReinstallingHooks, 0);
            }
        }
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring) return;

        _watchdogTimer?.Dispose();
        _watchdogTimer = null;

        // 让 Raw Input 窗口在其所属线程上销毁
        if (_rawInputWindow != IntPtr.Zero)
        {
            NativeInterop.PostMessage(_rawInputWindow, NativeInterop.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
        _rawInputReady = false;
        _rawTakeoverActive = false;

        // Terminate the hook thread's message loop.
        if (!TryStopHookThread())
        {
            Debug.WriteLine("Failed to stop the hook thread within the timeout. Continuing shutdown cleanup.");
        }

        // 线程已退出，可以安全释放 Raw Input 复用缓冲
        if (_rawInputBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_rawInputBuffer);
            _rawInputBuffer = IntPtr.Zero;
            _rawInputBufferSize = 0;
        }

        if (_keyboardHookId != IntPtr.Zero)
        {
            NativeInterop.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }

        if (_mouseHookId != IntPtr.Zero)
        {
            NativeInterop.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }

        ResetTransientState();

        _isMonitoring = false;
        Debug.WriteLine("Input monitoring stopped");
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Raw Input 已就绪时它是唯一记账通道，钩子直接放行，避免重复计数
        if (_rawInputReady)
        {
            return NativeInterop.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        if (nCode >= 0)
        {
            Interlocked.Exchange(ref _lastHookCallbackTick, Environment.TickCount);

            var message = (int)wParam;
            var hookStruct = Marshal.PtrToStructure<NativeInterop.KBDLLHOOKSTRUCT>(lParam);
            var vkCode = (int)hookStruct.vkCode;

            if (message == NativeInterop.WM_KEYDOWN || message == NativeInterop.WM_SYSKEYDOWN)
            {
                bool isNewKey;
                lock (_pressedKeys)
                {
                    isNewKey = _pressedKeys.Add(vkCode);
                }

                if (isNewKey)
                {
                    var keyName = KeyNameMapper.GetKeyName(vkCode, hookStruct.scanCode, hookStruct.flags);
                    DispatchKeyPressed(keyName);
                }
            }
            else if (message == NativeInterop.WM_KEYUP || message == NativeInterop.WM_SYSKEYUP)
            {
                lock (_pressedKeys)
                {
                    _pressedKeys.Remove(vkCode);
                }
            }
        }

        return NativeInterop.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // 同上：Raw 就绪时钩子不参与记账
        if (_rawInputReady)
        {
            return NativeInterop.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        if (nCode >= 0)
        {
            Interlocked.Exchange(ref _lastHookCallbackTick, Environment.TickCount);

            var message = (int)wParam;
            var hookStruct = Marshal.PtrToStructure<NativeInterop.MSLLHOOKSTRUCT>(lParam);

            switch (message)
            {
                case NativeInterop.WM_LBUTTONDOWN:
                    DispatchMouseClick(MouseButtonKind.Left);
                    break;

                case NativeInterop.WM_RBUTTONDOWN:
                    DispatchMouseClick(MouseButtonKind.Right);
                    break;

                case NativeInterop.WM_MBUTTONDOWN:
                    DispatchMouseClick(MouseButtonKind.Middle);
                    break;

                case NativeInterop.WM_XBUTTONDOWN:
                    DispatchMouseClick(
                        NativeInterop.HiWord((int)hookStruct.mouseData) == NativeInterop.XBUTTON2
                            ? MouseButtonKind.SideForward
                            : MouseButtonKind.SideBack);
                    break;

                case NativeInterop.WM_MOUSEMOVE:
                    HandleMouseMove(hookStruct.pt);
                    break;

                case NativeInterop.WM_MOUSEWHEEL:
                case NativeInterop.WM_MOUSEHWHEEL:
                    DispatchScroll(Math.Abs(NativeInterop.HiWord((int)hookStruct.mouseData)) / 120.0);
                    break;
            }
        }

        return NativeInterop.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private void HandleMouseMove(NativeInterop.POINT pt)
    {
        var currentPosition = new System.Drawing.Point(pt.x, pt.y);

        if (!_lastMousePosition.HasValue)
        {
            _lastMousePosition = currentPosition;
            _lastMouseSampleTime = DateTime.MinValue;
            return;
        }

        var dx = currentPosition.X - _lastMousePosition.Value.X;
        var dy = currentPosition.Y - _lastMousePosition.Value.Y;
        _lastMousePosition = currentPosition;

        var segmentDistance = Math.Sqrt(dx * dx + dy * dy);

        const double maxSegmentDistance = 250.0;
        if (segmentDistance > maxSegmentDistance)
        {
            // 绝对坐标大跳变（分辨率/全屏切换）不是真实移动
            _accumulatedDistance = 0.0;
            _lastMouseSampleTime = DateTime.Now;
            return;
        }

        AccumulateMouseDistance(segmentDistance);
    }

    /// <summary>
    /// 累计移动距离并按 30FPS 结算上报（钩子绝对坐标与 Raw Input 相对位移共用）。
    /// </summary>
    private void AccumulateMouseDistance(double segmentDistance)
    {
        TouchInput(null);
        _accumulatedDistance += segmentDistance;

        var elapsed = (DateTime.Now - _lastMouseSampleTime).TotalSeconds;
        if (elapsed < _mouseSampleInterval)
        {
            return;
        }

        var reportedDistance = _accumulatedDistance;
        _accumulatedDistance = 0.0;
        _lastMouseSampleTime = DateTime.Now;

        if (reportedDistance <= 0)
        {
            return;
        }

        var distance = reportedDistance;
        ThreadPool.QueueUserWorkItem(_ => InvokeSubscriber("MouseMoved", () => MouseMoved?.Invoke(distance)));
    }

    private static void InvokeSubscriber(string eventName, Action invoke)
    {
        try
        {
            invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{eventName} subscriber failed: {ex}");
        }
    }

    public void ResetLastMousePosition()
    {
        _lastMousePosition = null;
        _accumulatedDistance = 0.0;
    }

    public void HandleSystemResume()
    {
        if (!_isMonitoring)
        {
            StartMonitoring();
            return;
        }

        if (Interlocked.CompareExchange(ref _isReinstallingHooks, 1, 0) != 0)
        {
            return;
        }

        try
        {
            Debug.WriteLine("Handling system resume: refreshing hooks and transient state.");
            RetryHookRecovery("resume");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Resume recovery failed after retries. {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _isReinstallingHooks, 0);
        }
    }

    private void RestartMonitoring()
    {
        StopMonitoring();
        StartMonitoring();
    }

    private void RetryHookRecovery(string reason)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= HookReinstallRetryCount; attempt++)
        {
            try
            {
                Debug.WriteLine($"Hook recovery attempt {attempt}/{HookReinstallRetryCount} ({reason}).");
                ReinstallHooks();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Debug.WriteLine($"Hook recovery attempt {attempt} failed: {ex.Message}");
                if (attempt < HookReinstallRetryCount)
                {
                    Thread.Sleep(HookReinstallRetryDelayMs);
                }
            }
        }

        Debug.WriteLine("Hook reinstall retries exhausted, restarting monitoring.");
        RestartMonitoring();

        if (!_isMonitoring)
        {
            throw lastError ?? new InvalidOperationException("Hook recovery failed and monitoring did not restart.");
        }
    }

    private bool HasRecentKeyboardActivity()
    {
        for (var vk = 0x08; vk <= 0xFE; vk++)
        {
            var state = NativeInterop.GetAsyncKeyState(vk);
            if ((state & 0x0001) != 0 || (state & 0x8000) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryStopHookThread()
    {
        var thread = _hookThread;
        var threadId = _hookThreadId;
        if (thread == null && threadId == 0)
        {
            return true;
        }

        if (threadId != 0)
        {
            NativeInterop.PostThreadMessage(threadId, NativeInterop.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        if (thread == null)
        {
            Debug.WriteLine("Hook thread reference is missing while thread id is still set; keeping thread state for a later retry.");
            return false;
        }

        if (thread != null && !thread.Join(HookThreadStopTimeoutMs))
        {
            Debug.WriteLine($"Hook thread did not exit within {HookThreadStopTimeoutMs}ms.");
            return false;
        }

        _hookThread = null;
        _hookThreadId = 0;
        return true;
    }

    private void ResetTransientState()
    {
        lock (_pressedKeys)
        {
            _pressedKeys.Clear();
        }

        lock (_rawPressedKeys)
        {
            _rawPressedKeys.Clear();
        }

        _lastMousePosition = null;
        _accumulatedDistance = 0.0;
        _lastMouseSampleTime = DateTime.MinValue;
    }

    public void Dispose()
    {
        StopMonitoring();
        _instance = null;
    }
}
