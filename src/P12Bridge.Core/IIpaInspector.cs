namespace P12Bridge.Core;

public interface IIpaInspector
{
    IpaInspectionResult Inspect(byte[] ipaBytes, DateTimeOffset? now = null);
}
