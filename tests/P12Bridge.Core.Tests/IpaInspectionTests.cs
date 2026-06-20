using P12Bridge.Core;
using Xunit;

namespace P12Bridge.Core.Tests;

public sealed class IpaInspectionTests
{
    [Fact]
    public void InspectionResultReportsSuccessOnlyWhenMetadataHasNoErrors()
    {
        var metadata = new IpaMetadata(
            1234,
            "Payload/Demo.app",
            "com.example.demo",
            "1.0",
            "42",
            "Demo",
            false,
            null,
            new IpaSignaturePresence(true, false));

        var result = IpaInspectionResult.Success(metadata);

        Assert.True(result.IsSuccess);
        Assert.Equal("com.example.demo", result.Metadata?.BundleIdentifier);
    }

    [Fact]
    public void InspectionResultFailureHasNoMetadata()
    {
        var result = IpaInspectionResult.Failure(new ValidationIssue(
            IpaInspectionErrorCodes.InvalidArchive,
            ValidationSeverity.Error,
            "IPA archive is invalid."));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void InspectionResultWithMetadataAndErrorIssueIsNotSuccess()
    {
        var metadata = new IpaMetadata(
            1234,
            "Payload/Demo.app",
            "com.example.demo",
            "1.0",
            "42",
            "Demo",
            false,
            null,
            new IpaSignaturePresence(true, false));

        var result = IpaInspectionResult.Success(
            metadata,
            [new ValidationIssue(IpaInspectionErrorCodes.EmbeddedProfileInvalid, ValidationSeverity.Error, "Profile failed.")]);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Metadata);
    }
}
