namespace P12Bridge.Core;

public static class CertificateProofErrorCodes
{
    public const string EmptySubjectCommonName = "CERT_SUBJECT_COMMON_NAME_EMPTY";
    public const string InvalidCountryCode = "CERT_SUBJECT_COUNTRY_CODE_INVALID";
    public const string InvalidKeySize = "PRIVATE_KEY_SIZE_INVALID";
    public const string MissingPrivateKey = "PRIVATE_KEY_MISSING";
    public const string InvalidPrivateKey = "PRIVATE_KEY_INVALID";
    public const string MissingCertificate = "CERTIFICATE_MISSING";
    public const string InvalidCertificate = "CERTIFICATE_INVALID";
    public const string EmptyP12Password = "P12_PASSWORD_EMPTY";
    public const string P12ExportFailed = "P12_EXPORT_FAILED";
    public const string CsrGenerationFailed = "CSR_GENERATION_FAILED";
    public const string EmptyProjectName = "CERT_PROJECT_NAME_EMPTY";
    public const string MissingProjectDirectory = "CERT_PROJECT_DIRECTORY_MISSING";
    public const string ProjectCreateFailed = "CERT_PROJECT_CREATE_FAILED";
    public const string ProjectNotFound = "CERT_PROJECT_NOT_FOUND";
    public const string ProjectExportFailed = "CERT_PROJECT_EXPORT_FAILED";
}
