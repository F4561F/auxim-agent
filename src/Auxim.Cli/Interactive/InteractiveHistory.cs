using Auxim.Core.Runtime;

namespace Auxim.Cli.Interactive;

internal sealed class InteractiveHistory
{
    private const int MaxEntries = 200;
    private readonly List<string> _entries;
    private readonly IAuximRuntime _runtime;

    private InteractiveHistory(IAuximRuntime runtime, IEnumerable<string> entries)
    {
        _runtime = runtime;
        _entries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .TakeLast(MaxEntries)
            .ToList();
    }

    public IReadOnlyList<string> Entries => _entries;

    public static InteractiveHistory Load(IAuximRuntime runtime) =>
        new(runtime, runtime.LoadInputHistory());

    public void Add(string input)
    {
        input = input.Trim();
        if (input.Length == 0)
        {
            return;
        }

        if (_entries.Count > 0 && _entries[^1] == input)
        {
            return;
        }

        _entries.Add(input);
        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }
    }

    public void Save() => _runtime.SaveInputHistory(_entries);
}
