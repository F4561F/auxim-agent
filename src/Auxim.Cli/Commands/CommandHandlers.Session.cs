using Auxim.Core.Runtime;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleSession(IReadOnlyList<string> args, IAuximRuntime runtime)
    {
        var subcommand = args.FirstOrDefault() ?? "show";
        return subcommand switch
        {
            "clear" => ClearSession(runtime),
            "list" => ListSessions(runtime),
            "new" => NewSession(runtime, string.Join(' ', args.Skip(1))),
            "search" => SearchSessions(runtime, string.Join(' ', args.Skip(1))),
            "show" => ShowSession(runtime, args.Skip(1).FirstOrDefault()),
            "use" => UseSession(runtime, args.Skip(1).FirstOrDefault()),
            _ => PrintSessionHelp(),
        };
    }

    private static int ListSessions(IAuximRuntime runtime)
    {
        foreach (var session in runtime.ListSessions())
        {
            var marker = session.IsCurrent ? "*" : " ";
            Console.WriteLine($"{marker} {session.Id}  {session.UpdatedAt:yyyy-MM-dd HH:mm}  {session.Title}");
        }

        return 0;
    }

    private static int NewSession(IAuximRuntime runtime, string title)
    {
        var session = runtime.CreateSession(title);
        Console.WriteLine($"Current session: {session.Id}");
        return 0;
    }

    private static int ShowSession(IAuximRuntime runtime, string? id)
    {
        var session = string.IsNullOrWhiteSpace(id)
            ? runtime.GetOrCreateCurrentSession()
            : runtime.GetSession(id);
        if (session is null)
        {
            Console.Error.WriteLine("Session not found.");
            return 1;
        }

        Console.WriteLine($"{session.Id} - {session.Title}");
        Console.WriteLine($"Created: {session.CreatedAt:O}");
        Console.WriteLine($"Updated: {session.UpdatedAt:O}");
        Console.WriteLine();
        foreach (var message in session.Messages)
        {
            Console.WriteLine($"{message.Role}: {message.Content}");
            Console.WriteLine();
        }

        return 0;
    }

    private static int SearchSessions(IAuximRuntime runtime, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine("Usage: auxim session search <query>");
            return 1;
        }

        var sessions = runtime.SearchSessions(query);
        foreach (var session in sessions)
        {
            Console.WriteLine($"{session.Id}  {session.UpdatedAt:yyyy-MM-dd HH:mm}  {session.Title}");
        }

        if (sessions.Count == 0)
        {
            Console.WriteLine("No matching sessions.");
        }

        return 0;
    }

    private static int UseSession(IAuximRuntime runtime, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || runtime.UseSession(id) is null)
        {
            Console.Error.WriteLine("Usage: auxim session use <session-id>");
            return 1;
        }

        Console.WriteLine($"Current session: {id}");
        return 0;
    }

    private static int ClearSession(IAuximRuntime runtime)
    {
        runtime.ClearCurrentSession();
        Console.WriteLine("Current session cleared. The next chat creates a new session.");
        return 0;
    }

    private static int PrintSessionHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  auxim session list");
        Console.WriteLine("  auxim session show [session-id]");
        Console.WriteLine("  auxim session search <query>");
        Console.WriteLine("  auxim session new [title]");
        Console.WriteLine("  auxim session use <session-id>");
        Console.WriteLine("  auxim session clear");
        return 1;
    }
}
