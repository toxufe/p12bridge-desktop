namespace P12Bridge.Core;

public interface IProvisioningProfileImportService
{
    ProvisioningProfileImportResult Import(ProvisioningProfileImportRequest request);
}
