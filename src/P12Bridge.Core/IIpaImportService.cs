namespace P12Bridge.Core;

public interface IIpaImportService
{
    IpaImportResult Import(IpaImportRequest request);
}
