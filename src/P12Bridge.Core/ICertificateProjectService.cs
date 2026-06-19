namespace P12Bridge.Core;

public interface ICertificateProjectService
{
    CertificateProjectCreateResult Create(CertificateProjectCreateRequest request);

    CertificateProjectP12ExportResult ExportP12(CertificateProjectP12ExportRequest request);
}
