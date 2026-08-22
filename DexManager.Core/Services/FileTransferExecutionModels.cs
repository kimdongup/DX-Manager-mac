using System.Text;

namespace DexManager.Services
{
    internal sealed class AdbExecutionResult
    {
        internal AdbExecutionResult(
            int exitCode,
            string outputTail,
            string errorTail)
        {
            ExitCode = exitCode;
            OutputTail = outputTail ?? string.Empty;
            ErrorTail = errorTail ?? string.Empty;
        }

        internal int ExitCode { get; private set; }
        internal string OutputTail { get; private set; }
        internal string ErrorTail { get; private set; }
    }

    internal sealed class BoundedTextBuffer
    {
        private const int MaximumCharacters = 65536;
        private readonly object _syncRoot = new object();
        private readonly StringBuilder _builder = new StringBuilder();

        internal void Append(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            lock (_syncRoot)
            {
                _builder.Append(value);
                if (_builder.Length > MaximumCharacters)
                    _builder.Remove(
                        0,
                        _builder.Length - MaximumCharacters);
            }
        }

        internal string Value
        {
            get
            {
                lock (_syncRoot) return _builder.ToString().Trim();
            }
        }
    }
}
