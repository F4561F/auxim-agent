namespace Auxim.VAFS;

public sealed class VAShell
{
    private readonly BuiltinCommandRunner _builtins;
    private readonly ExternalCommandRunner _externalCommands;

    public VAShell(VirtualAgentFileSystem vafs)
    {
        _builtins = new BuiltinCommandRunner(vafs);
        _externalCommands = new ExternalCommandRunner(vafs);
    }

    public async Task<string> RunAsync(string command, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var tokens = Parse(command);
        if (tokens.Count == 0)
        {
            return "VAShell: empty command";
        }

        var executable = tokens[0];
        if (BuiltinCommandRunner.CanRun(executable))
        {
            return _builtins.Run(tokens);
        }

        return await _externalCommands.RunAsync(tokens, timeoutSeconds, cancellationToken);
    }

    private static IReadOnlyList<string> Parse(string command)
    {
        if (command.Any(character => character is ';' or '|' or '&' or '>' or '<' or '$' or '`' or '\n' or '\r'))
        {
            throw new InvalidOperationException(
                "VAShell does not allow shell operators, pipes, redirects, substitutions, or command chaining.");
        }

        return CommandTokenizer.Tokenize(command, throwOnUnclosedQuote: true);
    }
}

