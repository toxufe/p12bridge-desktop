namespace P12Bridge.Core;

public sealed record AppStoreConnectDevice(
    string Id,
    string Name,
    string Platform,
    string Udid,
    string DeviceClass,
    string Status,
    string Model,
    DateTimeOffset? AddedDate);
