namespace Lcia.Cli;

/// <summary>Parser minimale: comando, argomenti posizionali, opzioni "--nome valore" e flag "--nome".</summary>
internal sealed class CommandLine
{
    private static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "force", "follow-links", "case", "no-line-numbers", "no-source", "no-tree", "no-symbols", "absolute-paths",
        "no-redact", "i-understand-secrets-risk", "yes", "verbose",
    };

    private readonly List<string> _arguments = [];
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    private CommandLine(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public static CommandLine Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Nessun comando specificato.");
        }

        var command = new CommandLine(args[0].ToLowerInvariant());
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                command._arguments.Add(arg);
                continue;
            }

            var name = arg[2..];
            var equals = name.IndexOf('=');
            if (equals > 0)
            {
                command._options[name[..equals]] = name[(equals + 1)..];
                continue;
            }

            if (KnownFlags.Contains(name))
            {
                command._flags.Add(name);
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Manca il valore per --{name}.");
            }

            command._options[name] = args[++i];
        }

        return command;
    }

    public string Argument(int index, string description) =>
        index < _arguments.Count ? _arguments[index] : throw new ArgumentException($"Argomento mancante: {description}.");

    public string? Option(string name) => _options.TryGetValue(name, out var value) ? value : null;

    public bool Flag(string name) => _flags.Contains(name);

    /// <summary>Opzione con valori separati da virgola o punto e virgola.</summary>
    public IReadOnlyCollection<string> List(string name) =>
        Option(name) is { } value
            ? value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
}
