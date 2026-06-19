namespace P12Bridge.Core;

public interface ICertificateProjectService
{
    CertificateProjectCreateResult Create(CertificateProjectCreateRequest request);
}
