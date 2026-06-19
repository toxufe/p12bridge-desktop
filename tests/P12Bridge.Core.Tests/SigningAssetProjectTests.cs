using P12Bridge.Core;
using Xunit;

namespace P12Bridge.Core.Tests;

public sealed class SigningAssetProjectTests
{
    [Fact]
    public void ProjectKeepsPurpose()
    {
        var createdAt = DateTimeOffset.Parse("2026-06-19T00:00:00Z");
        var project = new SigningAssetProject(
            "Demo",
            SigningPurpose.Distribution,
            "C:\\Projects\\Demo",
            createdAt);

        Assert.Equal(SigningPurpose.Distribution, project.Purpose);
    }

    [Fact]
    public void CertificateSubjectRequiresCommonName()
    {
        var subject = new CertificateSubject(" ");

        var issue = Assert.Single(subject.Validate());

        Assert.Equal(CertificateProofErrorCodes.EmptySubjectCommonName, issue.Code);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
    }

    [Fact]
    public void CertificateSubjectBuildsDistinguishedName()
    {
        var subject = new CertificateSubject(
            "Developer Name",
            EmailAddress: "developer@example.com",
            Organization: "P12Bridge",
            Locality: "Shenzhen",
            StateOrProvince: "Guangdong",
            CountryCode: "cn");

        Assert.Equal(
            "CN=Developer Name, E=developer@example.com, O=P12Bridge, L=Shenzhen, S=Guangdong, C=CN",
            subject.ToDistinguishedName());
    }
}
