namespace Auxim.SDK;

public sealed class AuximGatewayClientOptions
{
    public Uri BaseAddress { get; set; } = new("http://127.0.0.1:5055");

    public string? Token { get; set; }

    public bool OwnsHttpClient { get; set; }
}
