namespace Auxim.Core.Vafs;

public sealed class VirtualPathException : InvalidOperationException
{
    public VirtualPathException(string message)
        : base(message)
    {
    }
}
