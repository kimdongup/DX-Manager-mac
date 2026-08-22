using System;

namespace DexManager.Platform
{
    public interface IAutoStartService
    {
        bool IsRegistered();
        void Apply(bool enabled);
        void Register();
        void Unregister();
    }
}
