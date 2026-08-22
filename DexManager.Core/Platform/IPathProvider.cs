using System;

namespace DexManager.Platform
{
    public interface IPathProvider
    {
        string BaseDirectory { get; }
        string DefaultSettingsFilePath { get; }
        string DefaultScreenshotFolder { get; }
        string DefaultLogDirectory { get; }
        string DefaultProxyExecutablePath { get; }

        string ResolveDefaultAdbPath();
        string ResolveDefaultScrcpyPath();
        string ResolveWin7AdbPath();

        string[] GetCandidateAdbPaths();
        string[] GetCandidateScrcpyPaths();
    }
}
