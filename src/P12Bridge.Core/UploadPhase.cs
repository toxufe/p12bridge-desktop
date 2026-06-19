namespace P12Bridge.Core;

public enum UploadPhase
{
    ValidatingEnvironment = 0,
    BuildingCommand = 1,
    RunningTransporter = 2,
    Completed = 3,
    Failed = 4
}
