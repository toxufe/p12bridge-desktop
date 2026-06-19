namespace P12Bridge.Core;

public sealed record AppStoreConnectProfile(
    string Id,
    string Name,
    string Platform,
    string Uuid,
    string ProfileState,
    string ProfileType,
    DateTimeOffset? CreatedDate,
    DateTimeOffset? ExpirationDate);
