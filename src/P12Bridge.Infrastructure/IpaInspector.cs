using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using P12Bridge.Core;

namespace P12Bridge.Infrastructure;

public sealed class IpaInspector : IIpaInspector
{
    private const string PayloadPrefix = "Payload/";
    private const string InfoPlistFileName = "Info.plist";
    private const string EmbeddedProfileFileName = "embedded.mobileprovision";
    private const string CodeResourcesPath = "_CodeSignature/CodeResources";

    private readonly IProvisioningProfileParser provisioningProfileParser;

    public IpaInspector(IProvisioningProfileParser? provisioningProfileParser = null)
    {
        this.provisioningProfileParser = provisioningProfileParser ?? new ProvisioningProfileParser();
    }

    public IpaInspectionResult Inspect(byte[] ipaBytes, DateTimeOffset? now = null)
    {
        if (ipaBytes.Length == 0)
        {
            return IpaInspectionResult.Failure(new ValidationIssue(
                IpaInspectionErrorCodes.EmptyPayload,
                ValidationSeverity.Error,
                "IPA data is empty.",
                "Choose a non-empty .ipa file."));
        }

        try
        {
            using var stream = new MemoryStream(ipaBytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            return InspectArchive(archive, ipaBytes.Length, now ?? DateTimeOffset.UtcNow);
        }
        catch (InvalidDataException)
        {
            return IpaInspectionResult.Failure(new ValidationIssue(
                IpaInspectionErrorCodes.InvalidArchive,
                ValidationSeverity.Error,
                "IPA archive is not a valid zip file.",
                "Choose a valid .ipa archive."));
        }
    }

    private IpaInspectionResult InspectArchive(ZipArchive archive, long fileSizeBytes, DateTimeOffset now)
    {
        var appBundlePaths = FindAppBundlePaths(archive).ToArray();
        if (appBundlePaths.Length == 0)
        {
            return IpaInspectionResult.Failure(new ValidationIssue(
                IpaInspectionErrorCodes.AppBundleMissing,
                ValidationSeverity.Error,
                "IPA does not contain a Payload app bundle.",
                "Choose an IPA with a Payload/<App>.app directory."));
        }

        if (appBundlePaths.Length > 1)
        {
            return IpaInspectionResult.Failure(new ValidationIssue(
                IpaInspectionErrorCodes.MultipleAppBundles,
                ValidationSeverity.Error,
                "IPA contains multiple app bundles.",
                "Use an IPA with exactly one Payload/<App>.app bundle."));
        }

        var appBundlePath = appBundlePaths[0];
        var infoPlistEntry = archive.GetEntry($"{appBundlePath}/{InfoPlistFileName}");
        if (infoPlistEntry is null)
        {
            return IpaInspectionResult.Failure(new ValidationIssue(
                IpaInspectionErrorCodes.InfoPlistMissing,
                ValidationSeverity.Error,
                "IPA app bundle is missing Info.plist.",
                "Rebuild the app package with a valid Info.plist."));
        }

        var infoPlistBytes = ReadEntryBytes(infoPlistEntry);
        var infoPlistResult = ParseInfoPlist(infoPlistBytes);
        if (!infoPlistResult.IsSuccess)
        {
            return IpaInspectionResult.Failure(infoPlistResult.Issues.ToArray());
        }

        var embeddedProfileEntry = archive.GetEntry($"{appBundlePath}/{EmbeddedProfileFileName}");
        var codeResourcesEntry = archive.GetEntry($"{appBundlePath}/{CodeResourcesPath}");
        var issues = new List<ValidationIssue>();
        ProvisioningProfile? embeddedProfile = null;

        if (embeddedProfileEntry is not null)
        {
            var profileResult = provisioningProfileParser.Parse(ReadEntryBytes(embeddedProfileEntry), now);
            embeddedProfile = profileResult.Profile;

            if (!profileResult.IsSuccess)
            {
                issues.Add(new ValidationIssue(
                    IpaInspectionErrorCodes.EmbeddedProfileInvalid,
                    ValidationSeverity.Error,
                    "Embedded provisioning profile could not be parsed as valid profile metadata.",
                    "Inspect or replace the IPA embedded provisioning profile."));
                issues.AddRange(profileResult.Issues);
            }
        }

        var signaturePresence = new IpaSignaturePresence(
            HasCodeResources: codeResourcesEntry is not null,
            HasEmbeddedProvisioningProfile: embeddedProfileEntry is not null);

        var metadata = new IpaMetadata(
            fileSizeBytes,
            appBundlePath,
            infoPlistResult.BundleIdentifier!,
            infoPlistResult.ShortVersion!,
            infoPlistResult.BuildVersion!,
            infoPlistResult.DisplayName,
            embeddedProfileEntry is not null,
            embeddedProfile,
            signaturePresence);

        return IpaInspectionResult.Success(metadata, issues);
    }

    private static IEnumerable<string> FindAppBundlePaths(ZipArchive archive) =>
        archive.Entries
            .Select(entry => NormalizeEntryName(entry.FullName))
            .Where(name => name.StartsWith(PayloadPrefix, StringComparison.Ordinal))
            .Select(TryGetAppBundlePath)
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.Ordinal);

    private static string? TryGetAppBundlePath(string entryName)
    {
        var slashIndex = entryName.IndexOf('/', PayloadPrefix.Length);
        if (slashIndex <= PayloadPrefix.Length)
        {
            return null;
        }

        var appDirectory = entryName[..slashIndex];
        return appDirectory.EndsWith(".app", StringComparison.Ordinal) ? appDirectory : null;
    }

    private static InfoPlistParseResult ParseInfoPlist(byte[] bytes)
    {
        if (BinaryPropertyListReader.HasBinaryHeader(bytes))
        {
            try
            {
                return BuildInfoPlistResult(BinaryPropertyListReader.ReadTopLevelStringDictionary(bytes));
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                return MalformedInfoPlist(
                    "Info.plist is not valid binary plist data.",
                    "Rebuild the app package with a valid Info.plist.");
            }
        }

        try
        {
            var document = XDocument.Parse(Encoding.UTF8.GetString(bytes), LoadOptions.None);
            var dictionary = document.Root?.Name.LocalName == "plist"
                ? document.Root.Elements().FirstOrDefault(element => element.Name.LocalName == "dict")
                : null;

            if (dictionary is null)
            {
                return MalformedInfoPlist();
            }

            return BuildInfoPlistResult(ReadXmlPlistStringDictionary(dictionary));
        }
        catch (ArgumentException)
        {
            return MalformedInfoPlist();
        }
        catch (System.Xml.XmlException)
        {
            return MalformedInfoPlist();
        }
    }

    private static InfoPlistParseResult BuildInfoPlistResult(IReadOnlyDictionary<string, string> values)
    {
        var bundleIdentifier = RequiredString(values, "CFBundleIdentifier");
        var shortVersion = RequiredString(values, "CFBundleShortVersionString");
        var buildVersion = RequiredString(values, "CFBundleVersion");
        var displayName = OptionalString(values, "CFBundleDisplayName")
            ?? OptionalString(values, "CFBundleName")
            ?? string.Empty;
        var issues = new List<ValidationIssue>();

        AddMissingRequiredKeyIssue(issues, bundleIdentifier, "CFBundleIdentifier");
        AddMissingRequiredKeyIssue(issues, shortVersion, "CFBundleShortVersionString");
        AddMissingRequiredKeyIssue(issues, buildVersion, "CFBundleVersion");

        return issues.Count > 0
            ? InfoPlistParseResult.Failure(issues.ToArray())
            : InfoPlistParseResult.Success(bundleIdentifier!, shortVersion!, buildVersion!, displayName);
    }

    private static Dictionary<string, string> ReadXmlPlistStringDictionary(XElement dictionary)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var children = dictionary.Elements().ToArray();

        for (var index = 0; index < children.Length - 1; index++)
        {
            if (children[index].Name.LocalName != "key")
            {
                continue;
            }

            var value = children[index + 1];
            if (value.Name.LocalName == "string")
            {
                values[children[index].Value] = value.Value;
            }
        }

        return values;
    }

    private static string? RequiredString(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? OptionalString(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void AddMissingRequiredKeyIssue(List<ValidationIssue> issues, string? value, string key)
    {
        if (value is not null)
        {
            return;
        }

        issues.Add(new ValidationIssue(
            IpaInspectionErrorCodes.MissingRequiredKey,
            ValidationSeverity.Error,
            $"Info.plist is missing required key '{key}'.",
            "Rebuild the app package with a complete Info.plist."));
    }

    private static InfoPlistParseResult MalformedInfoPlist(
        string message = "Info.plist is not valid XML plist data.",
        string suggestedAction = "Rebuild the app package with a valid XML Info.plist.") =>
        InfoPlistParseResult.Failure(new ValidationIssue(
            IpaInspectionErrorCodes.InfoPlistMalformed,
            ValidationSeverity.Error,
            message,
            suggestedAction));

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var entryStream = entry.Open();
        using var memoryStream = new MemoryStream();
        entryStream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private static string NormalizeEntryName(string name) =>
        name.Replace('\\', '/').TrimEnd('/');

    private sealed record InfoPlistParseResult(
        string? BundleIdentifier,
        string? ShortVersion,
        string? BuildVersion,
        string DisplayName,
        IReadOnlyList<ValidationIssue> Issues)
    {
        public bool IsSuccess => BundleIdentifier is not null
            && ShortVersion is not null
            && BuildVersion is not null
            && !Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

        public static InfoPlistParseResult Success(
            string bundleIdentifier,
            string shortVersion,
            string buildVersion,
            string displayName) =>
            new(bundleIdentifier, shortVersion, buildVersion, displayName, Array.Empty<ValidationIssue>());

        public static InfoPlistParseResult Failure(params ValidationIssue[] issues) =>
            new(null, null, null, string.Empty, issues);
    }
}
