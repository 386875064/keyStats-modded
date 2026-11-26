using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace KeyStatsUiTest;

/// <summary>
/// 对局记录窗口复现装置：挂真实 %LOCALAPPDATA%\KeyStats 数据，
/// 走真实 GameSessionService.Load() + GameSessionsWindow 渲染，
/// 抓各文本框实际值 + 截图 + 全程异常落盘。跑完即关。
/// </summary>
internal static class Program
{
    private static readonly string LogPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uitest-result.log");
    private static readonly string ShotPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uitest-shot.png");
    private static readonly string DetailShotPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uitest-shot-detail.png");
    private static readonly string PickerShotPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uitest-shot-picker.png");
    private static readonly string ReportShotPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uitest-shot-report.png");

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    private static void Log(string msg)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff ") + msg;
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
        Console.WriteLine(line);
    }

    [STAThread]
    private static void Main()
    {
        SetProcessDPIAware();
        try { if (File.Exists(LogPath)) File.Delete(LogPath); } catch { }
        Log("=== uitest start ===");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (s, e) =>
        {
            Log("DISPATCHER EXCEPTION: " + e.Exception);
            e.Handled = true; // 与真实 App 一致
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Log("UNHANDLED: " + e.ExceptionObject);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;

        // 报告窗用 DynamicResource 取 GhostButton 等样式，必须先初始化主题资源
        try
        {
            KeyStats.Helpers.ThemeManager.Instance.Initialize();
            Log("theme initialized");
        }
        catch (Exception ex)
        {
            Log("theme init fail: " + ex.Message);
        }

        // App.xaml 里的样式装置拿不到，补最小桩（只影响外观，不影响数据与布局逻辑）
        if (Application.Current != null)
        {
            Application.Current.Resources["GhostButton"] = new Style(typeof(System.Windows.Controls.Button));
            if (!Application.Current.Resources.Contains("CardBorder"))
            {
                Application.Current.Resources["CardBorder"] = new Style(typeof(System.Windows.Controls.Border));
            }
        }

        try
        {
            var svc = KeyStats.Services.GameSessionService.Instance;
            svc.Start();
            Log("service started");

            // LCU 端点发现单测：反射调用生产代码 LcuClient.Discover()
            try
            {
                var lcuType = typeof(KeyStats.Services.GameSessionService).Assembly
                    .GetType("KeyStats.Services.LcuClient");
                var discover = lcuType?.GetMethod("Discover",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var result = discover?.Invoke(null, null);
                if (result == null)
                {
                    Log("DISCOVER = null");
                }
                else
                {
                    var endpoint = (ValueTuple<string, string>)result;
                    Log("DISCOVER = " + endpoint.Item1 + " | tokenLen=" + endpoint.Item2.Length);
                }
            }
            catch (Exception ex)
            {
                Log("discover test fail: " + ex.Message);
            }

            var window = new KeyStats.Views.GameSessionsWindow();
            window.Show();
            Log("window shown");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
            timer.Tick += async (s, e) =>
            {
                timer.Stop();
                KeyStats.Views.GameSessionDetailWindow? detail = null;
                try
                {
                    var sessions = svc.GetSessions();
                    Log("service count=" + sessions.Count);
                    InspectText(window, "TotalSessionsText");
                    InspectText(window, "TotalDurationText");
                    InspectText(window, "TotalKeysText");
                    InspectText(window, "AverageApmText");
                    if (window.FindName("EmptyHintText") is System.Windows.UIElement hint)
                    {
                        Log("EmptyHintText visibility=" + hint.Visibility);
                    }
                    Shot(window, ShotPath);

                    if (sessions.Count > 0)
                    {
                        detail = new KeyStats.Views.GameSessionDetailWindow(sessions[0]);
                        detail.Show();
                        Log("detail shown (record[0])");
                    }
                }
                catch (Exception ex)
                {
                    Log("inspect fail: " + ex);
                }

                if (detail != null)
                {
                    try
                    {
                        await Task.Delay(1800);
                        InspectText(detail, "ClicksText");
                        InspectText(detail, "ClicksBreakdownText");
                        InspectText(detail, "ApmText");
                        if (detail.FindName("ChangeChampionButton") is System.Windows.Controls.Button button)
                        {
                            Log("ChangeChampionButton = \"" + button.Content + "\"");
                        }
                        Shot(detail, DetailShotPath);
                    }
                    catch (Exception ex)
                    {
                        Log("detail inspect fail: " + ex);
                    }
                    finally
                    {
                        detail.Close();
                    }
                }

                // 统计报告窗：三件套渲染检查
                try
                {
                    var report = new KeyStats.Views.ReportWindow();
                    report.Show();
                    await Task.Delay(2000);
                    Log("report shown");
                    InspectCount(report, "ChampionList", "英雄战绩行数");
                    InspectCount(report, "TrendList", "趋势柱数");
                    InspectCount(report, "FatigueList", "疲劳柱数");
                    InspectText(report, "ChampionHintText");
                    InspectText(report, "TrendHintText");
                    InspectText(report, "FatigueHintText");
                    Shot(report, ReportShotPath);
                    report.Close();
                }
                catch (Exception ex)
                {
                    Log("report inspect fail: " + ex);
                }

                // 英雄选择器：加载目录 + 搜索断言（本名/称号/英文/外号四路）
                try
                {
                    var picker = new KeyStats.Views.ChampionPickerWindow();
                    picker.Show();
                    Log("picker shown");
                    await Task.Delay(3500);

                    if (picker.FindName("SearchBox") is System.Windows.Controls.TextBox searchBox &&
                        picker.FindName("TilesPanel") is System.Windows.Controls.Panel tiles &&
                        picker.FindName("StatusText") is System.Windows.Controls.TextBlock status)
                    {
                        InspectText(picker, "StatusText");
                        foreach (var query in new[] { "亚索", "疾风剑豪", "女枪", "剑魔", "滑板鞋", "yasuo", "" })
                        {
                            searchBox.Text = query;
                            await Task.Delay(250);
                            Log("search \"" + query + "\" -> " + tiles.Children.Count + " tiles");
                        }
                    }

                    picker.Close();
                }
                catch (Exception ex)
                {
                    Log("picker inspect fail: " + ex);
                }

                window.Close();
                app.Shutdown();
            };
            timer.Start();
            app.Run();
            svc.Stop();
            Log("=== uitest done ===");
        }
        catch (Exception ex)
        {
            Log("FATAL: " + ex);
            try { app.Shutdown(); } catch { }
        }
    }

    private static void InspectText(Window window, string name)
    {
        if (window.FindName(name) is System.Windows.Controls.TextBlock tb)
        {
            Log(name + " = \"" + tb.Text + "\"");
        }
        else
        {
            Log(name + " = <not found>");
        }
    }

    private static void InspectCount(Window window, string name, string label)
    {
        if (window.FindName(name) is System.Windows.Controls.ItemsControl items)
        {
            var count = items.ItemsSource == null ? 0 : items.Items.Count;
            Log(label + "(" + name + ") = " + count);
        }
        else
        {
            Log(label + "(" + name + ") = <not found>");
        }
    }

    private static void Shot(Window window, string path)
    {
        try
        {
            double dpiX = 1.0, dpiY = 1.0;
            var source = PresentationSource.FromVisual(window);
            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }

            var left = window.Left;
            var top = window.Top;
            if (double.IsNaN(left) || double.IsNaN(top))
            {
                Log("shot skip: window not positioned (Left/Top NaN)");
                return;
            }

            int x = (int)(left * dpiX);
            int y = (int)(top * dpiY);
            int w = (int)(window.ActualWidth * dpiX);
            int h = (int)(window.ActualHeight * dpiY);
            using var bmp = new Bitmap(Math.Max(w, 1), Math.Max(h, 1));
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(Math.Max(w, 1), Math.Max(h, 1)));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Log("shot saved " + path + " " + w + "x" + h);
        }
        catch (Exception ex)
        {
            Log("shot fail: " + ex.Message);
        }
    }

    // ===== 与主程序 App.ResolveEmbeddedAssembly 相同的便携依赖解析 =====
    private static readonly Dictionary<string, Assembly> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object EmbeddedLock = new();

    private static Assembly? ResolveEmbedded(object? sender, ResolveEventArgs args)
    {
        try
        {
            var requested = new AssemblyName(args.Name);
            var simpleName = requested.Name;
            if (string.IsNullOrEmpty(simpleName))
            {
                return null;
            }

            var culture = requested.CultureName;
            var isSatellite = !string.IsNullOrEmpty(culture) &&
                              simpleName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase);
            var resourceName = isSatellite
                ? "Embedded.satellite." + culture + "." + simpleName + ".dll"
                : "Embedded." + simpleName + ".dll";

            lock (EmbeddedLock)
            {
                if (Cache.TryGetValue(resourceName, out var cached))
                {
                    return cached;
                }

                var host = typeof(KeyStats.Services.GameSessionService).Assembly;
                using var stream = host.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    return null;
                }

                var buffer = new byte[stream.Length];
                var offset = 0;
                while (offset < buffer.Length)
                {
                    var read = stream.Read(buffer, offset, buffer.Length - offset);
                    if (read <= 0)
                    {
                        break;
                    }
                    offset += read;
                }

                var loaded = Assembly.Load(buffer);
                Cache[resourceName] = loaded;
                Log("resolved " + resourceName + " (" + buffer.Length + " bytes)");
                return loaded;
            }
        }
        catch (Exception ex)
        {
            Log("resolve fail " + args.Name + ": " + ex.Message);
            return null;
        }
    }
}
