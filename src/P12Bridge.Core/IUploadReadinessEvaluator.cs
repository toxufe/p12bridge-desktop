namespace P12Bridge.Core;

public interface IUploadReadinessEvaluator
{
    UploadReadinessResult Evaluate(UploadReadinessRequest request, DateTimeOffset? now = null);
}
