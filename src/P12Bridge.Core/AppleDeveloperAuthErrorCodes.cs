namespace P12Bridge.Core;

public static class AppleDeveloperAuthErrorCodes
{
    public const string MissingKeyId = "APPLE_AUTH_KEY_ID_MISSING";
    public const string MissingIssuerId = "APPLE_AUTH_ISSUER_ID_MISSING";
    public const string MissingPrivateKey = "APPLE_AUTH_PRIVATE_KEY_MISSING";
    public const string InvalidPrivateKey = "APPLE_AUTH_PRIVATE_KEY_INVALID";
    public const string TokenGenerationFailed = "APPLE_AUTH_TOKEN_GENERATION_FAILED";
    public const string AppleUnauthorized = "APPLE_AUTH_UNAUTHORIZED";
    public const string AppleForbidden = "APPLE_AUTH_FORBIDDEN";
    public const string AppleApiUnavailable = "APPLE_AUTH_API_UNAVAILABLE";
    public const string NetworkFailure = "APPLE_AUTH_NETWORK_FAILURE";
    public const string UnexpectedAppleResponse = "APPLE_AUTH_UNEXPECTED_RESPONSE";
}
