namespace P12Bridge.Core;

public interface IProvisioningProfileParser
{
    ProvisioningProfileParseResult Parse(byte[] mobileProvisionBytes, DateTimeOffset? now = null);
}
