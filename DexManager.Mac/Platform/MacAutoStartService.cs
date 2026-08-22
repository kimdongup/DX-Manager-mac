using DexManager.Platform;

namespace DexManager.Mac.Platform;

public sealed class MacAutoStartService : IAutoStartService
{
    private const string PlistFileName = "io.github.mazemei.dxmanager.plist";

    private static string LaunchAgentsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");

    private static string PlistPath =>
        Path.Combine(LaunchAgentsDir, PlistFileName);

    public bool IsRegistered()
    {
        try
        {
            return File.Exists(PlistPath);
        }
        catch
        {
            return false;
        }
    }

    public void Apply(bool enabled)
    {
        if (enabled) Register();
        else Unregister();
    }

    public void Register()
    {
        try
        {
            Directory.CreateDirectory(LaunchAgentsDir);
            var exePath = Environment.ProcessPath ?? AppDomain.CurrentDomain.BaseDirectory;
            var plist = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>io.github.mazemei.dxmanager</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{exePath}</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                </dict>
                </plist>
                """;
            File.WriteAllText(PlistPath, plist);
        }
        catch
        {
            // Suppress background registration errors
        }
    }

    public void Unregister()
    {
        try
        {
            if (File.Exists(PlistPath))
                File.Delete(PlistPath);
        }
        catch
        {
            // Suppress background unregistration errors
        }
    }
}
