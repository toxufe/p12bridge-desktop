namespace P12Bridge.Core;

public interface IUploadSettingsService
{
    UploadSettingsResult Load();

    UploadSettingsResult Save(UploadSettings settings);

    UploadSettingsResult Clear();
}
