namespace P12Bridge.Core;

public interface ICertificateProjectBackupService
{
    CertificateProjectBackupResult Export(CertificateProjectBackupRequest request);
}
