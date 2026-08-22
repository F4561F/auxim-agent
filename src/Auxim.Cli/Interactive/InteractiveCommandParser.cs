namespace Auxim.Cli.Interactive;

internal static class InteractiveCommandParser
{
    public static IReadOnlyList<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        char? quote = null;
        foreach (var character in input)
        {
            if (quote is not null)
            {
                if (character == quote)
                {
                    quote = null;
                }
                else
                {
                    current.Add(character);
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (char.IsWhiteSpace(character))
            {
                Flush();
            }
            else
            {
                current.Add(character);
            }
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            tokens.Add(new string(current.ToArray()));
            current.Clear();
        }
    }
}
