using P12Bridge.Core;
using Xunit;

namespace P12Bridge.Core.Tests;

public sealed class LocalAssetSelectionTests
{
    [Fact]
    public void FindByPathReturnsMatchingAsset()
    {
        var selectedPath = Path.Combine(Path.GetTempPath(), "P12Bridge", "Project");
        var asset = new LocalAssetItem(
            LocalAssetType.CertificateProject,
            "Project",
            selectedPath,
            DateTimeOffset.UtcNow,
            BackupPath: Path.Combine(Path.GetTempPath(), "P12Bridge", "Backups", "Project-20260620010203.zip"));

        var match = LocalAssetSelection.FindByPath([asset], selectedPath);

        Assert.Same(asset, match);
        Assert.Equal(asset.BackupPath, match?.BackupPath);
    }

    [Fact]
    public void FindByPathReturnsNullWhenPathIsMissingOrUnknown()
    {
        var asset = new LocalAssetItem(
            LocalAssetType.CertificateProject,
            "Project",
            Path.Combine(Path.GetTempPath(), "P12Bridge", "Project"),
            DateTimeOffset.UtcNow);

        Assert.Null(LocalAssetSelection.FindByPath([asset], string.Empty));
        Assert.Null(LocalAssetSelection.FindByPath(
            [asset],
            Path.Combine(Path.GetTempPath(), "P12Bridge", "Other")));
    }
}
