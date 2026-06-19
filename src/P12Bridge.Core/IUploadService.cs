namespace P12Bridge.Core;

public interface IUploadService
{
    UploadEnvironmentValidationResult ValidateEnvironment(UploadRequest request);

    Task<UploadResult> UploadAsync(
        UploadRequest request,
        IProgress<UploadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
