namespace Auxim.Core.Utilities;

/// <summary>
/// Quote-aware command-line tokenizer shared by InteractiveShell and AuximShell.
/// Splits on whitespace, respects single and double quotes.
/// </summary>
public static class CommandTokenizer
{
    /// <summary>
    /// Splits <paramref name="input"/> into tokens, respecting single- and
    /// double-quoted segments.  Quotes are consumed (not included in output).
    /// </summary>
    /// <param name="input">Raw command-line text to tokenize.</param>
    /// <param name="throwOnUnclosedQuote">
    /// When true, throws <see cref="InvalidOperationException"/> if a quote is
    /// opened but never closed.</param>
    /// <returns>List of tokens.</returns>
    public static IReadOnlyList<string> Tokenize(string input, bool throwOnUnclosedQuote = false)
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
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                Flush();
                continue;
            }

            current.Add(character);
        }

        if (quote is not null && throwOnUnclosedQuote)
        {
            throw new InvalidOperationException("Unclosed quote in command.");
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
