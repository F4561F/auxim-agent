using Auxim.Gateway.Platforms;

namespace Auxim.Gateway;

public sealed class GatewayHost
{
    private readonly IReadOnlyList<IPlatformAdapter> _platforms;

    public GatewayHost(IReadOnlyList<IPlatformAdapter> platforms)
    {
        _platforms = platforms;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var platform in _platforms)
        {
            await platform.StartAsync(cancellationToken);
        }
    }
}
