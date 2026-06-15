namespace Auxim.VAFS;

public sealed class VirtualPathException : InvalidOperationException
{
    public VirtualPathException(string message)
        : base(message)
    {
    }
}
