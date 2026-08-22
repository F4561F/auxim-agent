using Auxim.Cli;
using Auxim.Cli.Interactive;
using Auxim.Cli.Services;
using Auxim.Core.Runtime;
using Auxim.Tools;

ConsoleHost.Configure();
IAuximRuntime runtime = AuximApplication.CreateRuntime();

try
{
    if (args.Length == 0)
    {
        return await InteractiveShell.RunAsync(runtime);
    }

    var command = args.FirstOrDefault() ?? "help";

    switch (command)
    {
        case "chat":
        {
            var prompt = string.Join(' ', args.Skip(1));
            if (string.IsNullOrWhiteSpace(prompt))
            {
                Console.Error.WriteLine("Usage: auxim chat <message>");
                return 1;
            }

            var result = await new ChatRunner(runtime).RunAsync(prompt);
            Console.WriteLine(result.FinalResponse);
            return 0;
        }
        case "tools":
        {
            foreach (var tool in runtime.ListTools())
            {
                Console.WriteLine($"{tool.Name} [{tool.Toolset}] - {tool.Description}");
            }

            return 0;
        }
        case "model":
            return CommandHandlers.HandleModel(args.Skip(1).ToArray(), runtime);
        case "auth":
            return CommandHandlers.HandleAuth(args.Skip(1).ToArray(), runtime);
        case "config":
            return CommandHandlers.HandleConfig(args.Skip(1).ToArray(), runtime);
        case "session":
            return CommandHandlers.HandleSession(args.Skip(1).ToArray(), runtime);
        case "tool":
            return await CommandHandlers.HandleTool(args.Skip(1).ToArray(), runtime);
        case "approval":
            return CommandHandlers.HandleApproval(args.Skip(1).ToArray(), runtime);
        case "sandbox":
            return CommandHandlers.HandleSandbox(args.Skip(1).ToArray(), runtime);
        case "doctor":
            return CommandHandlers.HandleDoctor(runtime);
        default:
            Console.WriteLine("Auxim");
            Console.WriteLine("Usage:");
            Console.WriteLine("  auxim chat <message>");
            Console.WriteLine("  auxim tools");
            Console.WriteLine("  auxim model show");
            Console.WriteLine("  auxim model set");
            Console.WriteLine("  auxim model set <provider> <model> [base-url]");
            Console.WriteLine("  auxim auth status");
            Console.WriteLine("  auxim auth set-api-key [key]");
            Console.WriteLine("  auxim config show");
            Console.WriteLine("  auxim session list");
            Console.WriteLine("  auxim tool run <name> [key=value ...]");
            Console.WriteLine("  auxim approval list");
            Console.WriteLine("  auxim sandbox show");
            Console.WriteLine("  auxim sandbox mount <name> <host-path> [--read-only]");
            Console.WriteLine("  auxim doctor");
            Console.WriteLine();
            Console.WriteLine("Quick setup:");
            Console.WriteLine("  auxim model set");
            Console.WriteLine("  auxim chat \"hello\"");
            return 0;
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Error: {exception.Message}");
    return 1;
}
