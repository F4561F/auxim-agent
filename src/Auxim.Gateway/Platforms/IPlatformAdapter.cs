namespace Auxim.Gateway.Platforms;

public interface IPlatformAdapter
{
    string Name { get; }
    Task StartAsync(CancellationToken cancellationToken);
}
