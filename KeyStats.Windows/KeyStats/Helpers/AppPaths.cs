using System;
using System.IO;

namespace KeyStats.Helpers;

/// <summary>
/// Resolves where KeyStats keeps its data.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Optional override used for portable installs and automated testing.
    /// Set the KEYSTATS_DATA_DIR environment variable to relocate every data file.
    /// </summary>
    public const string DataDirectoryEnvironmentVariable = "KEYSTATS_DATA_DIR";

    public static string GetDataFolder()
    {
        try
        {
            var overrideFolder = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overrideFolder))
            {
                var resolved = Path.GetFullPath(overrideFolder.Trim());
                Directory.CreateDirectory(resolved);
                return resolved;
            }
        }
        catch
        {
            // Fall back to the default location below.
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyStats");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
