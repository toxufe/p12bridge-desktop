namespace P12Bridge.Infrastructure;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
