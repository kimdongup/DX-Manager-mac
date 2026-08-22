using System;
using System.IO;
using System.Threading.Tasks;
using DexManager.Models;
using DexManager.Platform;
using DexManager.Utils;

namespace DexManager.Services
{
    public sealed class CaptureService : ICaptureService
    {
        private readonly AdbService _adbService;
        private readonly SettingsService _settingsService;
        private readonly AppSettings _settings;
        private readonly LogService _logService;
        private readonly ICaptureService _platformCapture;

        public CaptureService(
            AdbService adbService,
            SettingsService settingsService,
            AppSettings settings,
            LogService logService,
            ICaptureService platformCapture = null)
        {
            _adbService = adbService;
            _settingsService = settingsService;
            _settings = settings;
            _logService = logService;
            _platformCapture = platformCapture;
        }

        public CaptureResult CaptureWindow(
            IntPtr windowHandle,
            string serial)
        {
            if (_platformCapture != null)
            {
                var result = _platformCapture.CaptureWindow(windowHandle, serial);
                if (result != null && !string.IsNullOrWhiteSpace(result.LocalPath) && File.Exists(result.LocalPath))
                {
                    return ProcessCapturedFile(result.LocalPath, "DeX_Full", serial);
                }
                return result;
            }

            throw new PlatformNotSupportedException(
                "Platform capture service is not registered.");
        }

        public CaptureResult CaptureScreenRectangle(
            int x,
            int y,
            int width,
            int height,
            string prefix,
            string serial)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException(
                    LocalizationService.Get("Error.Capture.InvalidArea"));

            if (_platformCapture != null)
            {
                var result = _platformCapture.CaptureScreenRectangle(x, y, width, height, prefix, serial);
                if (result != null && !string.IsNullOrWhiteSpace(result.LocalPath) && File.Exists(result.LocalPath))
                {
                    return ProcessCapturedFile(result.LocalPath, prefix, serial);
                }
                return result;
            }

            throw new PlatformNotSupportedException(
                "Platform capture service is not registered.");
        }

        public Task<CaptureResult> CaptureWindowAsync(IntPtr windowHandle, string serial)
        {
            return Task.Run(() => CaptureWindow(windowHandle, serial));
        }

        public Task<CaptureResult> CaptureScreenRectangleAsync(int x, int y, int width, int height, string prefix, string serial)
        {
            return Task.Run(() => CaptureScreenRectangle(x, y, width, height, prefix, serial));
        }

        public CaptureResult ProcessCapturedFile(
            string localPath,
            string prefix,
            string serial)
        {
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                throw new FileNotFoundException("Captured image file not found.", localPath);

            var fileName = Path.GetFileName(localPath);
            _logService.Info(LocalizationService.Format(
                "Log.Capture.Saved",
                localPath));

            var transferred = false;
            var remotePath = string.Empty;

            if (_settings.Features.PushCaptureToDevice &&
                !string.IsNullOrWhiteSpace(serial))
            {
                remotePath = CombineDevicePath(
                    _settings.Paths.DeviceScreenshotFolder,
                    fileName);
                TransferToDevice(localPath, remotePath, serial);
                transferred = true;
            }
            else if (_settings.Features.PushCaptureToDevice)
            {
                _logService.Warning(LocalizationService.Get(
                    "Log.Capture.PushSkippedNoTarget"));
            }

            return new CaptureResult(localPath, remotePath, transferred);
        }

        private void TransferToDevice(
            string localPath,
            string remotePath,
            string serial)
        {
            var remoteFolder = _settings.Paths.DeviceScreenshotFolder.TrimEnd('/');
            var pushResult = Push(serial, localPath, remotePath);
            if (!pushResult.IsSuccess)
            {
                var mkdirResult = Shell(serial,
                    "mkdir -p " + ShellQuote(remoteFolder));
                if (!mkdirResult.IsSuccess)
                {
                    throw new InvalidOperationException(
                        LocalizationService.Format(
                            "Error.Capture.DeviceFolderFailed",
                            GetCommandError(mkdirResult)));
                }

                pushResult = Push(serial, localPath, remotePath);
                if (!pushResult.IsSuccess)
                {
                    throw new InvalidOperationException(
                        LocalizationService.Format(
                            "Error.Capture.PushFailed",
                            GetCommandError(pushResult)));
                }
            }

            var mediaUri = "file://" + remotePath;
            var scanResult = Shell(serial,
                "am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d " +
                ShellQuote(mediaUri));
            if (!scanResult.IsSuccess)
                _logService.Warning(LocalizationService.Get(
                    "Log.Capture.MediaScanFailed"));

            _logService.Info(LocalizationService.Format(
                "Log.Capture.Pushed",
                remotePath));
        }

        private ProcessResult Push(
            string serial,
            string localPath,
            string remotePath)
        {
            return _adbService.PushForSerial(
                serial,
                localPath,
                remotePath);
        }

        private ProcessResult Shell(string serial, string command)
        {
            return _adbService.ShellForSerial(serial, command, true);
        }

        private static string CombineDevicePath(string folder, string fileName)
        {
            return folder.TrimEnd('/') + "/" + fileName;
        }

        private static string ShellQuote(string value)
        {
            return "'" + value.Replace("'", "'\\''") + "'";
        }

        private static string GetCommandError(ProcessResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.StandardError))
                return result.StandardError;
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                return result.StandardOutput;
            return "ExitCode=" + result.ExitCode;
        }
    }
}
