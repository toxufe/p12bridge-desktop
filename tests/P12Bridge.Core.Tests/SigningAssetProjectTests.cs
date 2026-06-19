using P12Bridge.Core;

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
}
