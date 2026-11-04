namespace IndustrialStationHandover;

public sealed record MotionParameters(string CardFamily, int? CardNumber, int? AxisNumber, double? MillimetresPerRevolution,
    double? FeedbackToleranceMillimetres, double? MaximumVelocityMillimetresPerSecond);
public sealed record CameraParameters(string? DeviceIdentity, string? PixelFormat, string? TriggerAssociationRevision);
public sealed record SiteParameters(int Schema, bool TemplateOnly, string ReleaseId, string? StationIdentity,
    string? PlcEndpoint, string? AddressMapRevision, MotionParameters Motion, CameraParameters Camera,
    string? DataRoot, string? AcceptanceCriteriaRevision)
{
    public static SiteParameters Template(string release) => new(1, true, release, null, null, null,
        new("E3032", null, null, null, null, null), new(null, null, null), null, null);
    // Completeness/shape review only, never opens hardware or produces runtime-ready settings.
    public IReadOnlyList<string> Review()
    {
        List<string> issues = new();
        if (Schema != 1 || !TemplateOnly || string.IsNullOrWhiteSpace(ReleaseId) || ReleaseId.Length > 128) issues.Add("UnsupportedTemplateHeader");
        issues.Add("TemplateOnly-NoRuntimePermission");
        foreach ((string name, string? value) in new[] { ("StationIdentity", StationIdentity), ("PlcEndpoint", PlcEndpoint), ("AddressMapRevision", AddressMapRevision),
            ("Camera.DeviceIdentity", Camera?.DeviceIdentity), ("Camera.PixelFormat", Camera?.PixelFormat), ("Camera.TriggerAssociationRevision", Camera?.TriggerAssociationRevision),
            ("DataRoot", DataRoot), ("AcceptanceCriteriaRevision", AcceptanceCriteriaRevision) })
            if (string.IsNullOrWhiteSpace(value)) issues.Add("Missing:" + name);
        if (Motion is null || Motion.CardFamily != "E3032") issues.Add("MotionFamilyNeedsSeparateAdapter");
        if (Motion?.CardNumber is null or < 0) issues.Add("MissingOrInvalid:Motion.CardNumber");
        if (Motion?.AxisNumber is null or < 0) issues.Add("MissingOrInvalid:Motion.AxisNumber");
        foreach ((string name, double? value) in new[] { ("Motion.MillimetresPerRevolution", Motion?.MillimetresPerRevolution),
            ("Motion.FeedbackToleranceMillimetres", Motion?.FeedbackToleranceMillimetres), ("Motion.MaximumVelocityMillimetresPerSecond", Motion?.MaximumVelocityMillimetresPerSecond) })
            if (value is null || !double.IsFinite(value.Value) || value <= 0) issues.Add("MissingOrInvalid:" + name);
        return issues.AsReadOnly();
    }
}
public enum MaintenanceKind { StartupReview, Incident, ParameterChange, RestoreReview }
public sealed record MaintenanceRecord(int Schema, bool ExampleOnly, Guid Id, MaintenanceKind Kind, DateTimeOffset ObservedUtc,
    string Operator, string ReleaseId, string BeforeConfigurationSha256, string AfterConfigurationSha256, string EvidenceReference, string Summary)
{
    public void Validate()
    {
        if (Schema != 1 || Id == Guid.Empty || !Enum.IsDefined(Kind) || ObservedUtc == default || ObservedUtc.Offset != TimeSpan.Zero ||
            !Text(Operator) || !Text(ReleaseId) || !Text(EvidenceReference) || !Text(Summary) ||
            !Hash(BeforeConfigurationSha256) || !Hash(AfterConfigurationSha256) ||
            Kind == MaintenanceKind.ParameterChange && BeforeConfigurationSha256 == AfterConfigurationSha256)
            throw new ArgumentException("Invalid maintenance declaration");
    }
    private static bool Text(string? s) => !string.IsNullOrWhiteSpace(s) && s.Length <= 1024 && s == s.Trim() && !s.Any(char.IsControl);
    private static bool Hash(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
