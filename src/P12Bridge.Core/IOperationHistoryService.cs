namespace P12Bridge.Core;

public interface IOperationHistoryService
{
    OperationHistoryResult List();

    OperationHistoryResult Record(OperationHistoryRecordRequest request);

    OperationHistoryResult Clear();
}
