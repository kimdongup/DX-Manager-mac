using DexManager.Models;
using DexManager.Platform;

namespace DexManager.Mac.Platform;

public sealed class MacKeyboardService : IKeyboardService
{
    private bool _disposed;

    public event EventHandler CaptureHotkeyPressed;
    public event EventHandler ExitHotkeyPressed;

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void ReloadConfiguration(KeyMappingSettings settings)
    {
    }

    public void TriggerCapture() => CaptureHotkeyPressed?.Invoke(this, EventArgs.Empty);

    public void TriggerExit() => ExitHotkeyPressed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
