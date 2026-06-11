using Auxim.Core.Config;

namespace Auxim.Cli.Interactive;

internal sealed class InteractiveHistory
{
    private const int MaxEntries = 200;
    private readonly List<string> _entries;
    private readonly string _path;

    private InteractiveHistory(string path, IEnumerable<string> entries)
    {
        _path = path;
        _entries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .TakeLast(MaxEntries)
            .ToList();
    }

    public IReadOnlyList<string> Entries => _entries;

    public static InteractiveHistory Load()
    {
        var path = Path.Combine(ConfigLoader.GetAuximHome(), "history");
        try
        {
            if (!File.Exists(path))
            {
                return new InteractiveHistory(path, []);
            }

            return new InteractiveHistory(path, File.ReadAllLines(path));
        }
        catch (IOException)
        {
            return new InteractiveHistory(path, []);
        }
        catch (UnauthorizedAccessException)
        {
            return new InteractiveHistory(path, []);
        }
    }

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

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            File.WriteAllLines(_path, _entries);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
