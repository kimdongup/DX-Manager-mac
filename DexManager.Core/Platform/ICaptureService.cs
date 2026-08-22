using System;
using System.Threading.Tasks;
using DexManager.Models;

namespace DexManager.Platform
{
    public interface ICaptureService
    {
        CaptureResult CaptureWindow(IntPtr windowHandle, string serial);
        CaptureResult CaptureScreenRectangle(int x, int y, int width, int height, string prefix, string serial);
        Task<CaptureResult> CaptureWindowAsync(IntPtr windowHandle, string serial);
        Task<CaptureResult> CaptureScreenRectangleAsync(int x, int y, int width, int height, string prefix, string serial);
    }
}
