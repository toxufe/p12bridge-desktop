namespace P12Bridge.Core;

public interface ILocalAssetLibraryService
{
    LocalAssetLibraryResult Scan(LocalAssetLibraryRequest request);
}
