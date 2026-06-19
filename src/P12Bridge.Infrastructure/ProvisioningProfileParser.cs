using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class ProvisioningProfileParser : IProvisioningProfileParser
{
    private static readonly XName KeyName = "key";
    private static readonly XName DictName = "dict";
    private static readonly XName StringName = "string";
    private static readonly XName DateName = "date";
    private static readonly XName DataName = "data";
    private static readonly XName ArrayName = "array";
    private static readonly XName TrueName = "true";

    public ProvisioningProfileParseResult Parse(byte[] mobileProvisionBytes, DateTimeOffset? now = null)
    {
        if (mobileProvisionBytes is null || mobileProvisionBytes.Length == 0)
        {
            return ProvisioningProfileParseResult.Failure(new ValidationIssue(
                ProvisioningProfileErrorCodes.EmptyPayload,
                ValidationSeverity.Error,
                "Provisioning profile data is empty.",
                "Choose a non-empty .mobileprovision file."));
        }

        var plistBytes = ExtractPlistBytes(mobileProvisionBytes);
        if (plistBytes.Length == 0)
        {
            return ProvisioningProfileParseResult.Failure(new ValidationIssue(
                ProvisioningProfileErrorCodes.PlistNotFound,
                ValidationSeverity.Error,
                "Could not find an embedded plist payload in the provisioning profile.",
                "Verify the file is a valid .mobileprovision profile."));
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(plistBytes), LoadOptions.None);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException)
        {
            return ProvisioningProfileParseResult.Failure(new ValidationIssue(
                ProvisioningProfileErrorCodes.MalformedPlist,
                ValidationSeverity.Error,
                "Provisioning profile plist payload is malformed.",
                "Export the profile again from Apple Developer and retry."));
        }

        var rootDictionary = document.Root?.Element(DictName);
        if (rootDictionary is null)
        {
            return ProvisioningProfileParseResult.Failure(new ValidationIssue(
                ProvisioningProfileErrorCodes.MalformedPlist,
                ValidationSeverity.Error,
                "Provisioning profile plist does not contain a root dictionary.",
                "Export the profile again from Apple Developer and retry."));
        }

        return MapProfile(rootDictionary, now ?? DateTimeOffset.UtcNow);
    }

    private static ProvisioningProfileParseResult MapProfile(XElement rootDictionary, DateTimeOffset now)
    {
        var issues = new List<ValidationIssue>();

        var uuid = ReadRequiredString(rootDictionary, "UUID", issues);
        var name = ReadRequiredString(rootDictionary, "Name", issues);
        var creationDate = ReadRequiredDate(rootDictionary, "CreationDate", issues);
        var expirationDate = ReadRequiredDate(rootDictionary, "ExpirationDate", issues);
        var teamId = ReadFirstStringFromArray(rootDictionary, "TeamIdentifier") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(teamId))
        {
            issues.Add(MissingKeyIssue("TeamIdentifier"));
        }

        var entitlements = ReadDictionary(rootDictionary, "Entitlements");
        var applicationIdentifier = ReadString(entitlements, "application-identifier") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(applicationIdentifier))
        {
            issues.Add(MissingKeyIssue("Entitlements.application-identifier"));
        }

        var bundleIdentifier = DeriveBundleIdentifier(applicationIdentifier, teamId);
        var provisionedDeviceCount = ReadArray(rootDictionary, "ProvisionedDevices")?.Elements(StringName).Count() ?? 0;
        var provisionsAllDevices = ReadBoolean(rootDictionary, "ProvisionsAllDevices");
        var getTaskAllow = ReadBoolean(entitlements, "get-task-allow");
        var type = ClassifyProfile(provisionedDeviceCount, provisionsAllDevices, getTaskAllow);
        if (type == ProvisioningProfileType.Unknown)
        {
            issues.Add(new ValidationIssue(
                ProvisioningProfileErrorCodes.UnknownProfileType,
                ValidationSeverity.Warning,
                "Provisioning profile type could not be determined.",
                "Review profile metadata manually before using it for signing or upload checks."));
        }

        var status = expirationDate <= now ? ProvisioningProfileStatus.Expired : ProvisioningProfileStatus.Active;
        if (status == ProvisioningProfileStatus.Expired)
        {
            issues.Add(new ValidationIssue(
                ProvisioningProfileErrorCodes.ExpiredProfile,
                ValidationSeverity.Error,
                "Provisioning profile has expired.",
                "Regenerate and download a new provisioning profile from Apple Developer."));
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error && issue.Code == ProvisioningProfileErrorCodes.MissingRequiredKey))
        {
            return ProvisioningProfileParseResult.Failure(issues.ToArray());
        }

        var profile = new ProvisioningProfile(
            uuid,
            name,
            teamId,
            applicationIdentifier,
            bundleIdentifier,
            creationDate,
            expirationDate,
            type,
            status,
            provisionedDeviceCount,
            ReadDeveloperCertificateFingerprints(rootDictionary));

        return ProvisioningProfileParseResult.Success(profile, issues);
    }

    private static byte[] ExtractPlistBytes(byte[] bytes)
    {
        var payload = System.Text.Encoding.UTF8.GetString(bytes);
        const string startToken = "<?xml";
        const string endToken = "</plist>";

        var start = payload.IndexOf(startToken, StringComparison.Ordinal);
        if (start < 0)
        {
            return Array.Empty<byte>();
        }

        var end = payload.IndexOf(endToken, start, StringComparison.Ordinal);
        if (end < 0)
        {
            return Array.Empty<byte>();
        }

        end += endToken.Length;
        return System.Text.Encoding.UTF8.GetBytes(payload[start..end]);
    }

    private static XElement? ReadDictionary(XElement? dictionary, string key) =>
        ReadValue(dictionary, key)?.Name == DictName ? ReadValue(dictionary, key) : null;

    private static XElement? ReadArray(XElement? dictionary, string key) =>
        ReadValue(dictionary, key)?.Name == ArrayName ? ReadValue(dictionary, key) : null;

    private static string? ReadString(XElement? dictionary, string key)
    {
        var value = ReadValue(dictionary, key);
        return value?.Name == StringName ? value.Value : null;
    }

    private static DateTimeOffset? ReadDate(XElement dictionary, string key)
    {
        var value = ReadValue(dictionary, key);
        if (value?.Name != DateName)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value.Value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static bool ReadBoolean(XElement? dictionary, string key)
    {
        var value = ReadValue(dictionary, key);
        return value?.Name == TrueName;
    }

    private static XElement? ReadValue(XElement? dictionary, string key)
    {
        if (dictionary is null)
        {
            return null;
        }

        var elements = dictionary.Elements().ToList();
        for (var index = 0; index < elements.Count - 1; index++)
        {
            if (elements[index].Name == KeyName && elements[index].Value == key)
            {
                return elements[index + 1];
            }
        }

        return null;
    }

    private static string ReadRequiredString(XElement dictionary, string key, ICollection<ValidationIssue> issues)
    {
        var value = ReadString(dictionary, key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        issues.Add(MissingKeyIssue(key));
        return string.Empty;
    }

    private static DateTimeOffset ReadRequiredDate(XElement dictionary, string key, ICollection<ValidationIssue> issues)
    {
        var value = ReadDate(dictionary, key);
        if (value.HasValue)
        {
            return value.Value;
        }

        issues.Add(MissingKeyIssue(key));
        return DateTimeOffset.MinValue;
    }

    private static string? ReadFirstStringFromArray(XElement dictionary, string key) =>
        ReadArray(dictionary, key)?.Elements(StringName).FirstOrDefault()?.Value;

    private static IReadOnlyList<string> ReadDeveloperCertificateFingerprints(XElement dictionary)
    {
        var array = ReadArray(dictionary, "DeveloperCertificates");
        if (array is null)
        {
            return Array.Empty<string>();
        }

        var fingerprints = new List<string>();
        foreach (var data in array.Elements(DataName))
        {
            var normalized = string.Concat(data.Value.Where(character => !char.IsWhiteSpace(character)));
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            try
            {
                var certificateBytes = Convert.FromBase64String(normalized);
                var fingerprint = Convert.ToHexString(SHA256.HashData(certificateBytes));
                fingerprints.Add(fingerprint);
            }
            catch (FormatException)
            {
                // Invalid embedded certificate data should not block profile metadata parsing.
            }
        }

        return fingerprints;
    }

    private static ProvisioningProfileType ClassifyProfile(
        int provisionedDeviceCount,
        bool provisionsAllDevices,
        bool getTaskAllow)
    {
        if (provisionsAllDevices)
        {
            return ProvisioningProfileType.Enterprise;
        }

        if (provisionedDeviceCount > 0)
        {
            return getTaskAllow ? ProvisioningProfileType.Development : ProvisioningProfileType.AdHoc;
        }

        return getTaskAllow ? ProvisioningProfileType.Unknown : ProvisioningProfileType.AppStore;
    }

    private static string DeriveBundleIdentifier(string applicationIdentifier, string teamId)
    {
        if (string.IsNullOrWhiteSpace(applicationIdentifier))
        {
            return string.Empty;
        }

        var prefix = string.IsNullOrWhiteSpace(teamId) ? string.Empty : $"{teamId}.";
        return applicationIdentifier.StartsWith(prefix, StringComparison.Ordinal)
            ? applicationIdentifier[prefix.Length..]
            : applicationIdentifier;
    }

    private static ValidationIssue MissingKeyIssue(string key) =>
        new(
            ProvisioningProfileErrorCodes.MissingRequiredKey,
            ValidationSeverity.Error,
            $"Provisioning profile is missing required key '{key}'.",
            "Export the profile again from Apple Developer and retry.");
}
