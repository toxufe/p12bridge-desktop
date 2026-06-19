namespace P12Bridge.Core;

public sealed class AppleApiKeyCredential
{
    public AppleApiKeyCredential(
        string keyId,
        string issuerId,
        string privateKeyPem,
        string? displayName = null)
    {
        KeyId = keyId;
        IssuerId = issuerId;
        PrivateKeyPem = privateKeyPem;
        DisplayName = displayName;
    }

    public string KeyId { get; }

    public string IssuerId { get; }

    public string PrivateKeyPem { get; }

    public string? DisplayName { get; }

    public override string ToString() =>
        $"AppleApiKeyCredential {{ KeyId = {KeyId}, IssuerId = {IssuerId}, PrivateKeyPem = [REDACTED], DisplayName = {DisplayName} }}";
}
