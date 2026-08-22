using System.Net;

namespace Auxim.SDK;

public sealed class AuximGatewayException : Exception
{
    public AuximGatewayException(
        HttpStatusCode statusCode,
        AuximGatewayError? error,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        Error = error;
    }

    public HttpStatusCode StatusCode { get; }

    public AuximGatewayError? Error { get; }
}
