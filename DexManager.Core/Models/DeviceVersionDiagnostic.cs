namespace DexManager.Models
{
    public enum DeviceCompatibilityAssessment
    {
        Unknown = 0,
        RecommendedBaseline = 1,
        OlderUnverified = 2,
        NewerUnverified = 3
    }

    public sealed class DeviceVersionDiagnostic
    {
        public string Serial { get; set; }
        public string DisplayName { get; set; }
        public DeviceTransportKind TransportKind { get; set; }
        public string Model { get; set; }
        public string AndroidVersion { get; set; }
        public int AndroidSdk { get; set; }
        public string OneUiVersion { get; set; }
        public string SecurityPatch { get; set; }
        public bool QuerySucceeded { get; set; }
        public string ErrorDetail { get; set; }
        public DeviceCompatibilityAssessment Compatibility { get; set; }
    }
}
