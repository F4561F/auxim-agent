using Auxim.Core.State;

namespace Auxim.Cli;

public static partial class CommandHandlers
{
    public static int HandleSession(IReadOnlyList<string> args)
    {
        var subcommand = args.FirstOrDefault() ?? "show";
        var store = new SessionStore();
        return subcommand switch
        {
            "clear" => ClearSession(store),
            "list" => ListSessions(store),
            "new" => NewSession(store, string.Join(' ', args.Skip(1))),
            "search" => SearchSessions(store, string.Join(' ', args.Skip(1))),
            "show" => ShowSession(store, args.Skip(1).FirstOrDefault()),
            "use" => UseSession(store, args.Skip(1).FirstOrDefault()),
            _ => PrintSessionHelp(),
        };
    }

    private static int ListSessions(SessionStore store)
    {
        var currentId = store.GetCurrentSessionId();
        foreach (var session in store.List())
        {
            var marker = session.Id == currentId ? "*" : " ";
            Console.WriteLine($"{marker} {session.Id}  {session.UpdatedAt:yyyy-MM-dd HH:mm}  {session.Title}");
        }

        return 0;
    }

    private static int NewSession(SessionStore store, string title)
    {
        var session = store.NewSession(title);
        Console.WriteLine($"Current session: {session.Id}");
        return 0;
    }

    private static int ShowSession(SessionStore store, string? id)
    {
        var session = string.IsNullOrWhiteSpace(id)
            ? store.GetOrCreateCurrent()
            : store.TryLoad(id);
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

    private static int SearchSessions(SessionStore store, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine("Usage: auxim session search <query>");
            return 1;
        }

        var matches = 0;
        foreach (var record in store.List())
        {
            var session = store.TryLoad(record.Id);
            if (session is null)
            {
                continue;
            }

            var hit = session.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || session.Messages.Any(message => message.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (!hit)
            {
                continue;
            }

            matches++;
            Console.WriteLine($"{session.Id}  {session.UpdatedAt:yyyy-MM-dd HH:mm}  {session.Title}");
        }

        if (matches == 0)
        {
            Console.WriteLine("No matching sessions.");
        }

        return 0;
    }

    private static int UseSession(SessionStore store, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || store.TryLoad(id) is null)
        {
            Console.Error.WriteLine("Usage: auxim session use <session-id>");
            return 1;
        }

        store.SetCurrent(id);
        Console.WriteLine($"Current session: {id}");
        return 0;
    }

    private static int ClearSession(SessionStore store)
    {
        store.ClearCurrent();
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
