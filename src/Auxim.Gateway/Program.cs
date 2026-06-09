using Auxim.Gateway;
using Auxim.Gateway.Platforms;

var gateway = new GatewayHost([new ConsolePlatform()]);
await gateway.RunAsync(CancellationToken.None);
