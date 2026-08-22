using System;
using System.Collections.Generic;

namespace DexManager.Services
{
    public static class AdbCommandBuilder
    {
        public static string ForDevice(string serial, string arguments)
        {
            if (string.IsNullOrWhiteSpace(serial))
                throw new ArgumentException(
                    "ADB device serial is empty.",
                    "serial");
            if (string.IsNullOrWhiteSpace(arguments))
                throw new ArgumentException(
                    "ADB device command is empty.",
                    "arguments");

            return "-s " + Quote(serial.Trim()) + " " + arguments;
        }

        public static string ForShellCommands(
            string serial,
            params string[] commands)
        {
            if (commands == null || commands.Length == 0)
                throw new ArgumentException(
                    "At least one ADB shell command is required.",
                    "commands");

            var normalized = new List<string>();
            foreach (var command in commands)
            {
                if (string.IsNullOrWhiteSpace(command)) continue;
                normalized.Add(command.Trim());
            }
            if (normalized.Count == 0)
                throw new ArgumentException(
                    "At least one non-empty ADB shell command is required.",
                    "commands");

            return ForDevice(
                serial,
                "shell " + Quote(string.Join("; ", normalized)));
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
