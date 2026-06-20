namespace P12Bridge.Core;

public sealed record CertificateProjectCreateRequest(
    string ProjectName,
    SigningPurpose Purpose,
    CertificateSubject Subject,
    string BaseDirectory,
    string Note = "");
