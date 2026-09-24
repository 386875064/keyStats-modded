using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading;
using KeyStats.Models;
using KeyStats.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeyStats.Sync.Tests;

[TestClass]
public sealed class StatsPersistenceTests
{
    [TestMethod]
    public void ContinuousActivity_SavesBeforeInputStops()
    {
        TestPlatform.RequireWindows();
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "daily_stats.json");
        var manager = (StatsManager)FormatterServices.GetUninitializedObject(typeof(StatsManager));
        var stateLock = new object();
        SetField(manager, "_lock", stateLock);
        SetField(manager, "_statsFilePath", path);
        SetField(manager, "_saveInterval", 200.0);
        SetField(manager, "<CurrentStats>k__BackingField", new DailyStats(DateTime.Today) { KeyPresses = 42 });
        SetField(manager, "<History>k__BackingField", new Dictionary<string, DailyStats>());
        var scheduleSave = typeof(StatsManager).GetMethod("ScheduleSave", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(scheduleSave);

        try
        {
            var elapsed = Stopwatch.StartNew();
            while (!File.Exists(path) && elapsed.Elapsed < TimeSpan.FromSeconds(5))
            {
                scheduleSave!.Invoke(manager, null);
                Thread.Sleep(10);
            }

            Assert.IsTrue(File.Exists(path), "Continuous events must not postpone the first save until idle.");
            lock (stateLock)
            {
                var saved = JsonSerializer.Deserialize<DailyStats>(File.ReadAllText(path));
                Assert.IsNotNull(saved);
                Assert.AreEqual(42, saved!.KeyPresses);
            }
        }
        finally
        {
            lock (stateLock)
            {
                SetField(manager, "_isDisposed", true);
                SetField(manager, "_pendingSave", false);
                var timer = typeof(StatsManager).GetField("_saveTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(manager) as System.Timers.Timer;
                timer?.Dispose();
            }
        }
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Missing test setup field: " + name);
        field!.SetValue(target, value);
    }
}
