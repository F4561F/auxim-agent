namespace Auxim.VAFS;

internal sealed class BuiltinCommandRunner
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "pwd",
        "echo",
        "ls",
        "cat",
        "head",
        "tail",
        "wc",
        "find",
        "grep",
        "stat",
        "test",
    };

    private readonly VirtualAgentFileSystem _vafs;

    public BuiltinCommandRunner(VirtualAgentFileSystem vafs)
    {
        _vafs = vafs;
    }

    public static bool CanRun(string executable) => Commands.Contains(executable);

    public string Run(IReadOnlyList<string> tokens)
    {
        var executable = tokens[0];
        return executable switch
        {
            "pwd" => "exit_code: 0\nstdout:\n/workspace\nstderr:\n",
            "echo" => RunEcho(tokens),
            "ls" => RunList(tokens),
            "cat" => RunRead(tokens, maxLines: null),
            "head" => RunHeadOrTail(tokens, fromEnd: false),
            "tail" => RunHeadOrTail(tokens, fromEnd: true),
            "wc" => RunWordCount(tokens),
            "find" => RunFind(tokens),
            "grep" => RunGrep(tokens),
            "stat" => RunStat(tokens),
            "test" => RunTest(tokens),
            _ => $"VAShell: command '{executable}' is not implemented.",
        };
    }

    private static string RunEcho(IReadOnlyList<string> tokens)
    {
        return FormatResult(0, string.Join(' ', tokens.Skip(1)) + Environment.NewLine, "");
    }

    private string RunList(IReadOnlyList<string> tokens)
    {
        var options = ParseListOptions(tokens.Skip(1));
        var paths = options.Paths.Count == 0 ? ["/workspace"] : options.Paths;
        var outputs = new List<string>();

        foreach (var path in paths)
        {
            outputs.Add(ListOne(path, options, paths.Count > 1));
        }

        return FormatResult(0, string.Join("", outputs), "");
    }

    private string ListOne(string path, ListOptions options, bool includeHeader)
    {
        if (path == "/")
        {
            return FormatListEntries(path, ["/workspace/", "/tmp/", "/volumes/"], options, includeHeader);
        }

        if (path == "/volumes")
        {
            var volumes = _vafs.ListMounts()
                .Where(mount => mount.VirtualPath.StartsWith("/volumes/", StringComparison.Ordinal))
                .OrderBy(mount => mount.VirtualPath)
                .Select(mount => mount.VirtualPath + "/");
            return FormatListEntries(path, volumes, options, includeHeader);
        }

        var hostPath = _vafs.ResolveToHostPath(path);
        if (File.Exists(hostPath))
        {
            return FormatListEntries(path, [_vafs.ToVirtualPath(hostPath)], options, includeHeader);
        }

        var entries = Directory.EnumerateFileSystemEntries(hostPath)
            .OrderBy(entry => entry)
            .Where(entry => options.IncludeHidden || !Path.GetFileName(entry).StartsWith('.'))
            .Select(entry => Directory.Exists(entry)
                ? _vafs.ToVirtualPath(entry) + "/"
                : _vafs.ToVirtualPath(entry));
        return FormatListEntries(path, entries, options, includeHeader);
    }

    private string RunRead(IReadOnlyList<string> tokens, int? maxLines)
    {
        var paths = tokens.Skip(1).Where(token => token != "--").ToArray();
        if (paths.Length == 0)
        {
            return FormatResult(2, "", "usage: cat <virtual-path> [virtual-path ...]\n");
        }

        var output = new List<string>();
        foreach (var path in paths)
        {
            var hostPath = _vafs.ResolveToHostPath(path);
            var lines = File.ReadLines(hostPath);
            if (maxLines is not null)
            {
                lines = lines.Take(maxLines.Value);
            }

            output.AddRange(lines);
        }

        return FormatResult(0, string.Join(Environment.NewLine, output) + Environment.NewLine, "");
    }

    private string RunHeadOrTail(IReadOnlyList<string> tokens, bool fromEnd)
    {
        var parsed = ParseReadWindow(tokens.Skip(1), defaultLines: 10);
        if (parsed.Paths.Count == 0)
        {
            return FormatResult(2, "", $"usage: {(fromEnd ? "tail" : "head")} [-n count] <virtual-path> [virtual-path ...]\n");
        }

        var output = new List<string>();
        for (var index = 0; index < parsed.Paths.Count; index++)
        {
            var path = parsed.Paths[index];
            if (parsed.Paths.Count > 1)
            {
                if (output.Count > 0)
                {
                    output.Add("");
                }

                output.Add($"==> {path} <==");
            }

            output.AddRange(fromEnd ? TailLines(path, parsed.Count) : HeadLines(path, parsed.Count));
        }

        return FormatResult(0, string.Join(Environment.NewLine, output) + Environment.NewLine, "");
    }

    private string RunWordCount(IReadOnlyList<string> tokens)
    {
        var parsed = ParseWordCount(tokens.Skip(1));
        if (parsed.Paths.Count == 0)
        {
            return FormatResult(2, "", "usage: wc [-l|-w|-c] <virtual-path> [virtual-path ...]\n");
        }

        var rows = new List<WordCountRow>();
        foreach (var path in parsed.Paths)
        {
            var hostPath = _vafs.ResolveToHostPath(path);
            var text = File.ReadAllText(hostPath);
            rows.Add(new WordCountRow(
                Lines: CountLines(text),
                Words: CountWords(text),
                Characters: text.Length,
                Path: path));
        }

        var output = rows.Select(row => FormatWordCountRow(row, parsed.Mode)).ToList();
        if (rows.Count > 1)
        {
            output.Add(FormatWordCountRow(
                new WordCountRow(
                    rows.Sum(row => row.Lines),
                    rows.Sum(row => row.Words),
                    rows.Sum(row => row.Characters),
                    "total"),
                parsed.Mode));
        }

        return FormatResult(0, string.Join(Environment.NewLine, output) + Environment.NewLine, "");
    }

    private string RunFind(IReadOnlyList<string> tokens)
    {
        var options = ParseFindOptions(tokens.Skip(1));
        var roots = options.Paths.Count == 0 ? ["/workspace"] : options.Paths;
        var output = new List<string>();
        foreach (var root in roots)
        {
            output.AddRange(FindEntries(root, options));
        }

        return FormatResult(0, string.Join(Environment.NewLine, output.OrderBy(path => path, StringComparer.Ordinal)) + Environment.NewLine, "");
    }

    private string RunGrep(IReadOnlyList<string> tokens)
    {
        var options = ParseGrepOptions(tokens.Skip(1));
        if (string.IsNullOrEmpty(options.Pattern) || options.Paths.Count == 0)
        {
            return FormatResult(2, "", "usage: grep [-i] [-n] <text> <path ...>\n");
        }

        var comparison = options.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var output = new List<string>();
        var files = ExpandFileInputs(options.Paths).ToArray();
        var includePrefix = files.Length > 1 || options.Paths.Any(IsVirtualDirectory);
        foreach (var path in files)
        {
            var hostPath = _vafs.ResolveToHostPath(path);
            var lineNumber = 0;
            foreach (var line in File.ReadLines(hostPath))
            {
                lineNumber++;
                if (!line.Contains(options.Pattern, comparison))
                {
                    continue;
                }

                var prefix = includePrefix ? $"{path}:" : "";
                var number = options.LineNumbers ? $"{lineNumber}:" : "";
                output.Add($"{prefix}{number}{line}");
            }
        }

        return FormatResult(output.Count == 0 ? 1 : 0, string.Join(Environment.NewLine, output) + Environment.NewLine, "");
    }

    private string RunStat(IReadOnlyList<string> tokens)
    {
        var paths = tokens.Skip(1).ToArray();
        if (paths.Length == 0)
        {
            return FormatResult(2, "", "usage: stat <path ...>\n");
        }

        var output = new List<string>();
        foreach (var path in paths)
        {
            var hostPath = _vafs.ResolveToHostPath(path);
            if (Directory.Exists(hostPath))
            {
                var info = new DirectoryInfo(hostPath);
                output.Add($"directory  - {path} modified={info.LastWriteTimeUtc:O}");
                continue;
            }

            var file = new FileInfo(hostPath);
            output.Add($"file {file.Length,8} {path} modified={file.LastWriteTimeUtc:O}");
        }

        return FormatResult(0, string.Join(Environment.NewLine, output) + Environment.NewLine, "");
    }

    private string RunTest(IReadOnlyList<string> tokens)
    {
        if (tokens.Count != 3 || tokens[1] is not ("-e" or "-f" or "-d"))
        {
            return FormatResult(2, "", "usage: test -e|-f|-d <path>\n");
        }

        var hostPath = _vafs.ResolveToHostPath(tokens[2]);
        var ok = tokens[1] switch
        {
            "-e" => File.Exists(hostPath) || Directory.Exists(hostPath),
            "-f" => File.Exists(hostPath),
            "-d" => Directory.Exists(hostPath),
            _ => false,
        };
        return FormatResult(ok ? 0 : 1, "", "");
    }

    private static string FormatResult(int exitCode, string stdout, string stderr) =>
        $"exit_code: {exitCode}\nstdout:\n{EnsureTrailingNewline(stdout)}stderr:\n{stderr}";

    private static string EnsureTrailingNewline(string value) =>
        string.IsNullOrEmpty(value) || value.EndsWith('\n') ? value : value + Environment.NewLine;

    private string FormatListEntries(
        string path,
        IEnumerable<string> entries,
        ListOptions options,
        bool includeHeader)
    {
        var output = new List<string>();
        if (includeHeader)
        {
            output.Add($"{path}:");
        }

        foreach (var entry in entries)
        {
            output.Add(options.LongFormat ? FormatLongListEntry(entry) : entry);
        }

        if (includeHeader)
        {
            output.Add("");
        }

        return string.Join(Environment.NewLine, output) + Environment.NewLine;
    }

    private string FormatLongListEntry(string virtualPath)
    {
        var cleanPath = virtualPath.EndsWith('/') ? virtualPath[..^1] : virtualPath;
        if (virtualPath is "/workspace/" or "/tmp/" or "/volumes/")
        {
            return $"dir        - {virtualPath}";
        }

        var hostPath = _vafs.ResolveToHostPath(cleanPath);
        if (Directory.Exists(hostPath))
        {
            return $"dir        - {virtualPath}";
        }

        var size = new FileInfo(hostPath).Length;
        return $"file {size,8} {virtualPath}";
    }

    private IEnumerable<string> HeadLines(string path, int count)
    {
        var hostPath = _vafs.ResolveToHostPath(path);
        return File.ReadLines(hostPath).Take(count).ToArray();
    }

    private IEnumerable<string> TailLines(string path, int count)
    {
        var hostPath = _vafs.ResolveToHostPath(path);
        var queue = new Queue<string>();
        foreach (var line in File.ReadLines(hostPath))
        {
            queue.Enqueue(line);
            if (queue.Count > count)
            {
                queue.Dequeue();
            }
        }

        return queue.ToArray();
    }

    private static ListOptions ParseListOptions(IEnumerable<string> args)
    {
        var includeHidden = false;
        var longFormat = false;
        var paths = new List<string>();
        foreach (var arg in args)
        {
            if (arg == "--")
            {
                continue;
            }

            if (arg.StartsWith('-') && arg.Length > 1)
            {
                foreach (var option in arg.Skip(1))
                {
                    switch (option)
                    {
                        case 'a':
                            includeHidden = true;
                            break;
                        case 'l':
                            longFormat = true;
                            break;
                        case '1':
                            break;
                        default:
                            throw new InvalidOperationException($"ls option -{option} is not supported by VAShell.");
                    }
                }

                continue;
            }

            paths.Add(arg);
        }

        return new ListOptions(includeHidden, longFormat, paths);
    }

    private static ReadWindow ParseReadWindow(IEnumerable<string> args, int defaultLines)
    {
        var count = defaultLines;
        var paths = new List<string>();
        var pendingCount = false;
        foreach (var arg in args)
        {
            if (pendingCount)
            {
                count = ParsePositiveCount(arg);
                pendingCount = false;
                continue;
            }

            if (arg == "-n")
            {
                pendingCount = true;
                continue;
            }

            if (arg.StartsWith("-n", StringComparison.Ordinal) && arg.Length > 2)
            {
                count = ParsePositiveCount(arg[2..]);
                continue;
            }

            paths.Add(arg);
        }

        if (pendingCount)
        {
            throw new InvalidOperationException("-n requires a positive line count.");
        }

        return new ReadWindow(count, paths);
    }

    private static WordCountOptions ParseWordCount(IEnumerable<string> args)
    {
        var mode = WordCountMode.All;
        var paths = new List<string>();
        foreach (var arg in args)
        {
            if (arg == "-l")
            {
                mode = WordCountMode.Lines;
                continue;
            }

            if (arg == "-w")
            {
                mode = WordCountMode.Words;
                continue;
            }

            if (arg == "-c")
            {
                mode = WordCountMode.Characters;
                continue;
            }

            paths.Add(arg);
        }

        return new WordCountOptions(mode, paths);
    }

    private static FindOptions ParseFindOptions(IEnumerable<string> args)
    {
        var paths = new List<string>();
        string? name = null;
        char? type = null;
        int? maxDepth = null;
        var values = args.ToArray();
        for (var index = 0; index < values.Length; index++)
        {
            var arg = values[index];
            switch (arg)
            {
                case "-name":
                    name = ReadOptionValue(values, ref index, "-name");
                    break;
                case "-type":
                    var rawType = ReadOptionValue(values, ref index, "-type");
                    if (rawType is not ("f" or "d"))
                    {
                        throw new InvalidOperationException("find -type supports only f or d.");
                    }

                    type = rawType[0];
                    break;
                case "-maxdepth":
                    maxDepth = ParseNonNegativeCount(ReadOptionValue(values, ref index, "-maxdepth"));
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new InvalidOperationException($"find option {arg} is not supported by VAShell.");
                    }

                    paths.Add(arg);
                    break;
            }
        }

        return new FindOptions(paths, name, type, maxDepth);
    }

    private static GrepOptions ParseGrepOptions(IEnumerable<string> args)
    {
        var ignoreCase = false;
        var lineNumbers = false;
        string? pattern = null;
        var paths = new List<string>();
        foreach (var arg in args)
        {
            if (pattern is null && arg.StartsWith('-') && arg.Length > 1)
            {
                foreach (var option in arg.Skip(1))
                {
                    switch (option)
                    {
                        case 'i':
                            ignoreCase = true;
                            break;
                        case 'n':
                            lineNumbers = true;
                            break;
                        default:
                            throw new InvalidOperationException($"grep option -{option} is not supported by VAShell.");
                    }
                }

                continue;
            }

            if (pattern is null)
            {
                pattern = arg;
                continue;
            }

            paths.Add(arg);
        }

        return new GrepOptions(pattern ?? "", paths, ignoreCase, lineNumbers);
    }

    private static int ParsePositiveCount(string value)
    {
        if (!int.TryParse(value, out var count) || count <= 0)
        {
            throw new InvalidOperationException("Line count must be a positive integer.");
        }

        return Math.Min(count, 10_000);
    }

    private static int ParseNonNegativeCount(string value)
    {
        if (!int.TryParse(value, out var count) || count < 0)
        {
            throw new InvalidOperationException("Count must be a non-negative integer.");
        }

        return Math.Min(count, 10_000);
    }

    private static string ReadOptionValue(IReadOnlyList<string> values, ref int index, string option)
    {
        if (index + 1 >= values.Count)
        {
            throw new InvalidOperationException($"{option} requires a value.");
        }

        index++;
        return values[index];
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        return text.Count(character => character == '\n') + (text.EndsWith('\n') ? 0 : 1);
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                inWord = false;
                continue;
            }

            if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
    }

    private static string FormatWordCountRow(WordCountRow row, WordCountMode mode)
    {
        return mode switch
        {
            WordCountMode.Lines => $"{row.Lines,8} {row.Path}",
            WordCountMode.Words => $"{row.Words,8} {row.Path}",
            WordCountMode.Characters => $"{row.Characters,8} {row.Path}",
            _ => $"{row.Lines,8} {row.Words,8} {row.Characters,8} {row.Path}",
        };
    }

    private IEnumerable<string> FindEntries(string root, FindOptions options)
    {
        var hostRoot = _vafs.ResolveToHostPath(root);
        var entries = new List<string>();
        AddIfMatches(hostRoot, depth: 0);
        if (!Directory.Exists(hostRoot))
        {
            return entries;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(
            hostRoot,
            "*",
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
            }))
        {
            var relative = Path.GetRelativePath(hostRoot, entry);
            var depth = relative == "." ? 0 : relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
            if (options.MaxDepth is not null && depth > options.MaxDepth)
            {
                continue;
            }

            AddIfMatches(entry, depth);
        }

        return entries;

        void AddIfMatches(string hostPath, int depth)
        {
            if (options.MaxDepth is not null && depth > options.MaxDepth)
            {
                return;
            }

            var isDirectory = Directory.Exists(hostPath);
            if (options.Type == 'f' && isDirectory)
            {
                return;
            }

            if (options.Type == 'd' && !isDirectory)
            {
                return;
            }

            if (options.NamePattern is not null
                && !WildcardMatch(Path.GetFileName(hostPath), options.NamePattern))
            {
                return;
            }

            entries.Add(_vafs.ToVirtualPath(hostPath) + (isDirectory ? "/" : ""));
        }
    }

    private IEnumerable<string> ExpandFileInputs(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var hostPath = _vafs.ResolveToHostPath(path);
            if (File.Exists(hostPath))
            {
                yield return path;
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(
                hostPath,
                "*",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false,
                }).OrderBy(file => file, StringComparer.Ordinal))
            {
                yield return _vafs.ToVirtualPath(file);
            }
        }
    }

    private bool IsVirtualDirectory(string path)
    {
        try
        {
            return Directory.Exists(_vafs.ResolveToHostPath(path));
        }
        catch (VirtualPathException)
        {
            return false;
        }
    }

    private static bool WildcardMatch(string value, string pattern)
    {
        return Match(value, pattern, valueIndex: 0, patternIndex: 0);

        static bool Match(string value, string pattern, int valueIndex, int patternIndex)
        {
            while (patternIndex < pattern.Length)
            {
                if (pattern[patternIndex] == '*')
                {
                    while (patternIndex + 1 < pattern.Length && pattern[patternIndex + 1] == '*')
                    {
                        patternIndex++;
                    }

                    if (patternIndex + 1 == pattern.Length)
                    {
                        return true;
                    }

                    for (var index = valueIndex; index <= value.Length; index++)
                    {
                        if (Match(value, pattern, index, patternIndex + 1))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                if (valueIndex >= value.Length)
                {
                    return false;
                }

                if (pattern[patternIndex] != '?' && pattern[patternIndex] != value[valueIndex])
                {
                    return false;
                }

                valueIndex++;
                patternIndex++;
            }

            return valueIndex == value.Length;
        }
    }

    private sealed record ListOptions(bool IncludeHidden, bool LongFormat, IReadOnlyList<string> Paths);

    private sealed record ReadWindow(int Count, IReadOnlyList<string> Paths);

    private sealed record WordCountOptions(WordCountMode Mode, IReadOnlyList<string> Paths);

    private sealed record WordCountRow(int Lines, int Words, int Characters, string Path);

    private sealed record FindOptions(
        IReadOnlyList<string> Paths,
        string? NamePattern,
        char? Type,
        int? MaxDepth);

    private sealed record GrepOptions(
        string Pattern,
        IReadOnlyList<string> Paths,
        bool IgnoreCase,
        bool LineNumbers);

    private enum WordCountMode
    {
        All,
        Lines,
        Words,
        Characters,
    }
}
