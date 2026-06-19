namespace P12Bridge.Core;

public static class CertificateProjectBackupErrorCodes
{
    public const string ProjectDirectoryMissing = "CERT_BACKUP_PROJECT_DIRECTORY_MISSING";
    public const string ProjectNotFound = "CERT_BACKUP_PROJECT_NOT_FOUND";
    public const string MetadataMissing = "CERT_BACKUP_METADATA_MISSING";
    public const string OutputDirectoryMissing = "CERT_BACKUP_OUTPUT_DIRECTORY_MISSING";
    public const string OutputDirectoryNotFound = "CERT_BACKUP_OUTPUT_DIRECTORY_NOT_FOUND";
    public const string ExportFailed = "CERT_BACKUP_EXPORT_FAILED";
}
