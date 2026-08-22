using System;
using DexManager.Models;

namespace DexManager.Platform
{
    public interface IKeyboardService : IDisposable
    {
        void Start();
        void Stop();
        event EventHandler CaptureHotkeyPressed;
        event EventHandler ExitHotkeyPressed;
        void ReloadConfiguration(KeyMappingSettings settings);
    }
}
