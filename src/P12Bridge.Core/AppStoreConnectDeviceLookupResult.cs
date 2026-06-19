namespace P12Bridge.Core;

public sealed class AppStoreConnectDeviceLookupResult
{
    public AppStoreConnectDeviceLookupResult(
        string checkedEndpoint,
        IReadOnlyList<AppStoreConnectDevice> devices,
        IReadOnlyList<ValidationIssue> issues)
    {
        CheckedEndpoint = checkedEndpoint;
        Devices = devices;
        Issues = issues;
    }

    public string CheckedEndpoint { get; }

    public IReadOnlyList<AppStoreConnectDevice> Devices { get; }

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsSuccess => !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool HasDevices => Devices.Count > 0;

    public static AppStoreConnectDeviceLookupResult Success(
        string checkedEndpoint,
        IReadOnlyList<AppStoreConnectDevice> devices) =>
        new(checkedEndpoint, devices, Array.Empty<ValidationIssue>());

    public static AppStoreConnectDeviceLookupResult Failure(
        string checkedEndpoint,
        params ValidationIssue[] issues) =>
        new(checkedEndpoint, Array.Empty<AppStoreConnectDevice>(), issues);

    public static AppStoreConnectDeviceLookupResult Failure(
        string checkedEndpoint,
        IReadOnlyList<ValidationIssue> issues) =>
        new(checkedEndpoint, Array.Empty<AppStoreConnectDevice>(), issues);

    public override string ToString() =>
        $"AppStoreConnectDeviceLookupResult {{ CheckedEndpoint = {CheckedEndpoint}, IsSuccess = {IsSuccess}, DeviceCount = {Devices.Count}, Issues = {Issues.Count} }}";
}
