using System.Text;
using DexManager.Mac.Hosting;

namespace DexManager.Mac;

internal static class Program
{
    private const string Version = "2.0.0 (macOS Edition)";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length > 0)
        {
            var firstArg = args[0].ToLowerInvariant();
            if (firstArg is "-h" or "--help")
            {
                PrintHelp();
                return 0;
            }
            if (firstArg is "-v" or "--version")
            {
                Console.WriteLine($"DX Manager for macOS - Version {Version}");
                return 0;
            }
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        try
        {
            using var host = new InteractiveHost();

            if (args.Length > 0)
            {
                var cmd = args[0].ToLowerInvariant();
                switch (cmd)
                {
                    case "--diag" or "-d":
                        await host.RunDiagnosticsAsync();
                        return 0;

                    case "--dex" or "-x":
                        await host.StartDexAsync();
                        AnsiConsole.Info("DeX launched. Press Ctrl+C to stop...");
                        try
                        {
                            while (!cts.Token.IsCancellationRequested)
                            {
                                await Task.Delay(500, cts.Token);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected on Ctrl+C
                        }
                        await host.StopDexAsync();
                        return 0;

                    case "--stop-dex":
                        await host.StopDexAsync();
                        return 0;

                    default:
                        AnsiConsole.Warning($"Unknown option: '{args[0]}'. Showing help:");
                        PrintHelp();
                        return 1;
                }
            }

            await host.RunAsync(cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.Error($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        AnsiConsole.Header($"DX MANAGER for macOS (.NET 8) - v{Version}");
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project DexManager.Mac               # Start Interactive Dashboard");
        Console.WriteLine("  dotnet run --project DexManager.Mac -- --dex       # Launch DeX immediately (Ctrl+C to stop)");
        Console.WriteLine("  dotnet run --project DexManager.Mac -- --stop-dex  # Stop active DeX session");
        Console.WriteLine("  dotnet run --project DexManager.Mac -- --diag      # Run system diagnostics");
        Console.WriteLine("  dotnet run --project DexManager.Mac -- --version   # Display version info");
        Console.WriteLine("  dotnet run --project DexManager.Mac -- --help      # Show this help message");
        Console.WriteLine();
    }
}
