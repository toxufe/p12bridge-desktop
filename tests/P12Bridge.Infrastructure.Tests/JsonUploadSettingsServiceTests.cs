using System.Text;
using P12Bridge.Core;
using P12Bridge.Infrastructure;
using Xunit;

namespace P12Bridge.Infrastructure.Tests;

public sealed class JsonUploadSettingsServiceTests : IDisposable
{
    private readonly string tempDirectory;
    private readonly string settingsPath;

    public JsonUploadSettingsServiceTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), $"p12bridge-settings-{Guid.NewGuid():N}");
        settingsPath = Path.Combine(tempDirectory, "upload-settings.json");
    }

    [Fact]
    public void LoadReturnsEmptySettingsWhenFileDoesNotExist()
    {
        var service = CreateService();

        var result = service.Load();

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Settings.TransporterExecutablePath);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void SaveAndLoadRestoresNonSensitiveSettings()
    {
        var service = CreateService();
        var settings = new UploadSettings(
            TransporterExecutablePath: @"C:\Tools\iTMSTransporter.cmd",
            PackagePath: @"C:\Builds\App.ipa",
            AssetDescriptionPath: @"C:\Builds\AppStoreInfo.plist",
            CredentialMode: UploadCredentialMode.AppleIdAppPassword,
            ApiKeyId: "KEY123",
            IssuerId: "issuer",
            PrivateKeyPath: @"C:\Keys\AuthKey_KEY123.p8",
            AppleAccount: "developer@example.com",
            CertificateDirectory: @"C:\P12Bridge\Certificates",
            ProfileDirectory: @"C:\P12Bridge\Profiles",
            IpaDirectory: @"C:\P12Bridge\IPAs");

        var saveResult = service.Save(settings);
        var loadResult = service.Load();

        Assert.True(saveResult.IsSuccess);
        Assert.True(loadResult.IsSuccess);
        Assert.Equal(settings.TransporterExecutablePath, loadResult.Settings.TransporterExecutablePath);
        Assert.Equal(settings.PackagePath, loadResult.Settings.PackagePath);
        Assert.Equal(settings.AssetDescriptionPath, loadResult.Settings.AssetDescriptionPath);
        Assert.Equal(settings.CredentialMode, loadResult.Settings.CredentialMode);
        Assert.Equal(settings.ApiKeyId, loadResult.Settings.ApiKeyId);
        Assert.Equal(settings.IssuerId, loadResult.Settings.IssuerId);
        Assert.Equal(settings.PrivateKeyPath, loadResult.Settings.PrivateKeyPath);
        Assert.Equal(settings.AppleAccount, loadResult.Settings.AppleAccount);
        Assert.Equal(settings.CertificateDirectory, loadResult.Settings.CertificateDirectory);
        Assert.Equal(settings.ProfileDirectory, loadResult.Settings.ProfileDirectory);
        Assert.Equal(settings.IpaDirectory, loadResult.Settings.IpaDirectory);
    }

    [Fact]
    public void SaveDoesNotPersistSecretsWhenOptInIsDisabled()
    {
        var service = CreateService();
        var settings = new UploadSettings(
            SaveSensitiveValues: false,
            Jwt: "eyJheader.payload.signature",
            AppSpecificPassword: "abcd-efgh-ijkl-mnop");

        service.Save(settings);

        var json = File.ReadAllText(settingsPath);
        var loaded = service.Load();
        Assert.DoesNotContain(settings.Jwt, json, StringComparison.Ordinal);
        Assert.DoesNotContain(settings.AppSpecificPassword, json, StringComparison.Ordinal);
        Assert.Equal(string.Empty, loaded.Settings.Jwt);
        Assert.Equal(string.Empty, loaded.Settings.AppSpecificPassword);
    }

    [Fact]
    public void SaveClearsExistingSecretsWhenOptInIsDisabled()
    {
        var service = CreateService();
        service.Save(new UploadSettings(
            SaveSensitiveValues: true,
            Jwt: "eyJheader.payload.signature",
            AppSpecificPassword: "abcd-efgh-ijkl-mnop"));

        service.Save(new UploadSettings(
            SaveSensitiveValues: false,
            Jwt: "new-jwt",
            AppSpecificPassword: "new-password"));

        var json = File.ReadAllText(settingsPath);
        var loaded = service.Load();
        Assert.Contains("\"ProtectedJwt\": \"\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ProtectedAppSpecificPassword\": \"\"", json, StringComparison.Ordinal);
        Assert.Equal(string.Empty, loaded.Settings.Jwt);
        Assert.Equal(string.Empty, loaded.Settings.AppSpecificPassword);
    }

    [Fact]
    public void SaveAndLoadRestoresSecretsWhenOptInIsEnabled()
    {
        var service = CreateService();
        var settings = new UploadSettings(
            CredentialMode: UploadCredentialMode.Jwt,
            SaveSensitiveValues: true,
            Jwt: "eyJheader.payload.signature",
            AppSpecificPassword: "abcd-efgh-ijkl-mnop");

        service.Save(settings);

        var json = File.ReadAllText(settingsPath);
        var loaded = service.Load();
        Assert.DoesNotContain(settings.Jwt, json, StringComparison.Ordinal);
        Assert.DoesNotContain(settings.AppSpecificPassword, json, StringComparison.Ordinal);
        Assert.Equal(settings.Jwt, loaded.Settings.Jwt);
        Assert.Equal(settings.AppSpecificPassword, loaded.Settings.AppSpecificPassword);
        Assert.True(loaded.Settings.SaveSensitiveValues);
    }

    [Fact]
    public void LoadReturnsWarningWhenProtectedSecretCannotBeRestored()
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(
            settingsPath,
            """
            {
              "SaveSensitiveValues": true,
              "ProtectedJwt": "bad-secret"
            }
            """);
        var service = CreateService();

        var result = service.Load();

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == UploadSettingsErrorCodes.SecretUnprotectFailed);
        Assert.Equal(string.Empty, result.Settings.Jwt);
    }

    [Fact]
    public void ClearDeletesStoredSettings()
    {
        var service = CreateService();
        service.Save(new UploadSettings(TransporterExecutablePath: @"C:\Tools\iTMSTransporter.cmd"));

        var clearResult = service.Clear();

        Assert.True(clearResult.IsSuccess);
        Assert.False(File.Exists(settingsPath));
        Assert.Equal(string.Empty, service.Load().Settings.TransporterExecutablePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private JsonUploadSettingsService CreateService() =>
        new(settingsPath, new FakeSecretProtector());

    private sealed class FakeSecretProtector : ISecretProtector
    {
        public string Protect(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        public string Unprotect(string protectedValue)
        {
            if (protectedValue == "bad-secret")
            {
                throw new FormatException("Invalid protected value.");
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
        }
    }
}
