using System.Text;
using System.Security;

namespace Auxim.Cli;

internal static class ConsoleHost
{
    public static void Configure()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
        }
        catch (SecurityException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}
