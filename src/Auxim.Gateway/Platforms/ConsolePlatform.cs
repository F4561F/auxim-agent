namespace Auxim.Gateway.Platforms;

public sealed class ConsolePlatform : IPlatformAdapter
{
    public string Name => "console";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Auxim gateway started with console platform placeholder.");
        return Task.CompletedTask;
    }
}
