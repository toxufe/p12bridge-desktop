using System.Text;

namespace P12Bridge.Core;

public sealed record CertificateSubject(
    string CommonName,
    string? EmailAddress = null,
    string? Organization = null,
    string? OrganizationalUnit = null,
    string? Locality = null,
    string? StateOrProvince = null,
    string? CountryCode = null)
{
    public IReadOnlyList<ValidationIssue> Validate()
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(CommonName))
        {
            issues.Add(new ValidationIssue(
                CertificateProofErrorCodes.EmptySubjectCommonName,
                ValidationSeverity.Error,
                "Certificate subject common name is required.",
                "Enter the Apple developer account name or a recognizable certificate name."));
        }

        if (!string.IsNullOrWhiteSpace(CountryCode) && CountryCode.Trim().Length != 2)
        {
            issues.Add(new ValidationIssue(
                CertificateProofErrorCodes.InvalidCountryCode,
                ValidationSeverity.Error,
                "Country code must be exactly two letters.",
                "Use a two-letter ISO country code such as CN or US."));
        }

        return issues;
    }

    public string ToDistinguishedName()
    {
        var parts = new List<string>
        {
            FormatPart("CN", CommonName)
        };

        AddOptionalPart(parts, "E", EmailAddress);
        AddOptionalPart(parts, "OU", OrganizationalUnit);
        AddOptionalPart(parts, "O", Organization);
        AddOptionalPart(parts, "L", Locality);
        AddOptionalPart(parts, "S", StateOrProvince);
        AddOptionalPart(parts, "C", CountryCode?.ToUpperInvariant());

        return string.Join(", ", parts);
    }

    private static void AddOptionalPart(ICollection<string> parts, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(FormatPart(key, value));
        }
    }

    private static string FormatPart(string key, string value) => $"{key}={Escape(value.Trim())}";

    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is ',' or '+' or '"' or '\\' or '<' or '>' or ';')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
