using System;

namespace DexManager.Services
{
    public static class DeviceSerialScope
    {
        public static bool Matches(string requestedSerial, string candidateSerial)
        {
            if (string.IsNullOrWhiteSpace(requestedSerial) ||
                string.IsNullOrWhiteSpace(candidateSerial))
            {
                return false;
            }

            return string.Equals(
                requestedSerial.Trim(),
                candidateSerial.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
