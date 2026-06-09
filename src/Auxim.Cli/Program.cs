using Auxim.Core.Agent;
using Auxim.Core.Config;
using Auxim.Core.State;
using Auxim.Cli;
using Auxim.Tools;

try
{
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

            var config = ConfigLoader.Load();
            var options = new AgentOptions
            {
                Provider = config.Model.Provider,
                Model = config.Model.Name,
                MaxIterations = config.Agent.MaxIterations,
            };

            var sessions = new SessionStore();
            var session = sessions.GetOrCreateCurrent();
            var agent = new AuximAgent(AgentClientFactory.Create(config), BuiltInTools.CreateDefaultRegistry(), options);
            var result = await agent.RunConversationAsync(prompt, session.Messages);
            sessions.AppendTurn(session, prompt, result.FinalResponse);
            Console.WriteLine(result.FinalResponse);
            return 0;
        }
        case "tools":
        {
            var registry = BuiltInTools.CreateDefaultRegistry();
            foreach (var tool in registry.List().OrderBy(tool => tool.Name))
            {
                Console.WriteLine($"{tool.Name} [{tool.Toolset}] - {tool.Description}");
            }

            return 0;
        }
        case "model":
            return CommandHandlers.HandleModel(args.Skip(1).ToArray());
        case "auth":
            return CommandHandlers.HandleAuth(args.Skip(1).ToArray());
        case "config":
            return CommandHandlers.HandleConfig(args.Skip(1).ToArray());
        case "session":
            return CommandHandlers.HandleSession(args.Skip(1).ToArray());
        case "tool":
            return await CommandHandlers.HandleTool(args.Skip(1).ToArray());
        case "approval":
            return CommandHandlers.HandleApproval(args.Skip(1).ToArray());
        case "sandbox":
            return CommandHandlers.HandleSandbox(args.Skip(1).ToArray());
        case "doctor":
            return CommandHandlers.HandleDoctor();
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
