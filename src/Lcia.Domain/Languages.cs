namespace Lcia.Domain;

/// <summary>Linguaggi e formati riconosciuti dallo scanner.</summary>
public enum SourceLanguage
{
    Unknown = 0,
    CSharp,
    VisualBasic,
    FSharp,
    TSql,
    Solution,
    MsBuild,
    Json,
    Xml,
    Config,
    Yaml,
    Razor,
    Xaml,
    JavaScript,
    TypeScript,
    Python,
    Java,
    Go,
    Php,
    Markdown,
}

/// <summary>Livello di analisi disponibile per un linguaggio o ottenuto su un singolo file.</summary>
public enum SupportLevel
{
    /// <summary>Solo indicizzazione e ricerca testuale.</summary>
    TextOnly = 0,

    /// <summary>Lettura della struttura (soluzioni e file di progetto MSBuild).</summary>
    Structural = 1,

    /// <summary>Albero sintattico: dichiarazioni, firme, intervalli di righe.</summary>
    Syntactic = 2,

    /// <summary>Modello semantico: simboli risolti e riferimenti verificati.</summary>
    Semantic = 3,
}

/// <summary>Descrizione dichiarata del livello di supporto di un linguaggio.</summary>
public sealed record LanguageInfo(
    SourceLanguage Language,
    string DisplayName,
    SupportLevel CurrentSupport,
    string SupportDescription,
    string MarkdownFence);

/// <summary>
/// Catalogo delle estensioni riconosciute e del livello di supporto dichiarato.
/// Il livello indicato è quello effettivamente implementato nella versione corrente.
/// </summary>
public static class LanguageCatalog
{
    private static readonly (string Extension, SourceLanguage Language)[] ExtensionMap =
    [
        (".sln", SourceLanguage.Solution),
        (".slnx", SourceLanguage.Solution),
        (".csproj", SourceLanguage.MsBuild),
        (".vbproj", SourceLanguage.MsBuild),
        (".fsproj", SourceLanguage.MsBuild),
        (".sqlproj", SourceLanguage.MsBuild),
        (".props", SourceLanguage.MsBuild),
        (".targets", SourceLanguage.MsBuild),
        (".cs", SourceLanguage.CSharp),
        (".vb", SourceLanguage.VisualBasic),
        (".fs", SourceLanguage.FSharp),
        (".sql", SourceLanguage.TSql),
        (".json", SourceLanguage.Json),
        (".xml", SourceLanguage.Xml),
        (".config", SourceLanguage.Config),
        (".yaml", SourceLanguage.Yaml),
        (".yml", SourceLanguage.Yaml),
        (".cshtml", SourceLanguage.Razor),
        (".razor", SourceLanguage.Razor),
        (".xaml", SourceLanguage.Xaml),
        (".js", SourceLanguage.JavaScript),
        (".jsx", SourceLanguage.JavaScript),
        (".ts", SourceLanguage.TypeScript),
        (".tsx", SourceLanguage.TypeScript),
        (".py", SourceLanguage.Python),
        (".java", SourceLanguage.Java),
        (".go", SourceLanguage.Go),
        (".php", SourceLanguage.Php),
        (".md", SourceLanguage.Markdown),
    ];

    private static readonly Dictionary<string, SourceLanguage> ByExtension =
        ExtensionMap.ToDictionary(e => e.Extension, e => e.Language, StringComparer.OrdinalIgnoreCase);

    private const string TextOnlyNote = "Indicizzazione full-text e ricerca (testo, regex). Nessuna analisi strutturale in questa versione.";

    private static readonly Dictionary<SourceLanguage, LanguageInfo> Infos = new()
    {
        [SourceLanguage.CSharp] = new(SourceLanguage.CSharp, "C#", SupportLevel.Syntactic,
            "Roslyn (sintassi): namespace, tipi, membri, firme, intervalli di righe, documentazione XML, attributi, tipi base, " +
            "dipendenze da costruttore e membri, registrazioni DI, invocazioni per nome. Analisi semantica prevista nella Fase 3.", "csharp"),
        [SourceLanguage.VisualBasic] = new(SourceLanguage.VisualBasic, "Visual Basic .NET", SupportLevel.TextOnly, TextOnlyNote + " Analisi Roslyn VB pianificata.", "vb"),
        [SourceLanguage.FSharp] = new(SourceLanguage.FSharp, "F#", SupportLevel.TextOnly, TextOnlyNote, "fsharp"),
        [SourceLanguage.TSql] = new(SourceLanguage.TSql, "T-SQL", SupportLevel.TextOnly, TextOnlyNote + " Parser ScriptDom previsto nella Fase 4.", "sql"),
        [SourceLanguage.Solution] = new(SourceLanguage.Solution, "Soluzione Visual Studio", SupportLevel.Structural, "Elenco dei progetti di .sln e .slnx.", "text"),
        [SourceLanguage.MsBuild] = new(SourceLanguage.MsBuild, "Progetto / file MSBuild", SupportLevel.Structural,
            "Progetti .csproj/.vbproj/.fsproj/.sqlproj: framework, riferimenti a progetti, pacchetti NuGet (anche Central Package Management e packages.config).", "xml"),
        [SourceLanguage.Json] = new(SourceLanguage.Json, "JSON", SupportLevel.TextOnly, TextOnlyNote + " Rilevamento segreti attivo.", "json"),
        [SourceLanguage.Xml] = new(SourceLanguage.Xml, "XML", SupportLevel.TextOnly, TextOnlyNote + " Rilevamento segreti attivo.", "xml"),
        [SourceLanguage.Config] = new(SourceLanguage.Config, "File .config", SupportLevel.TextOnly, TextOnlyNote + " Rilevamento segreti attivo.", "xml"),
        [SourceLanguage.Yaml] = new(SourceLanguage.Yaml, "YAML", SupportLevel.TextOnly, TextOnlyNote + " Rilevamento segreti attivo.", "yaml"),
        [SourceLanguage.Razor] = new(SourceLanguage.Razor, "Razor (.cshtml/.razor)", SupportLevel.TextOnly, TextOnlyNote, "razor"),
        [SourceLanguage.Xaml] = new(SourceLanguage.Xaml, "XAML", SupportLevel.TextOnly, TextOnlyNote, "xml"),
        [SourceLanguage.JavaScript] = new(SourceLanguage.JavaScript, "JavaScript", SupportLevel.TextOnly, TextOnlyNote + " Parsing Tree-sitter pianificato.", "javascript"),
        [SourceLanguage.TypeScript] = new(SourceLanguage.TypeScript, "TypeScript", SupportLevel.TextOnly, TextOnlyNote + " Parsing Tree-sitter pianificato.", "typescript"),
        [SourceLanguage.Python] = new(SourceLanguage.Python, "Python", SupportLevel.TextOnly, TextOnlyNote + " Parsing Tree-sitter pianificato.", "python"),
        [SourceLanguage.Java] = new(SourceLanguage.Java, "Java", SupportLevel.TextOnly, TextOnlyNote + " Parsing Tree-sitter pianificato.", "java"),
        [SourceLanguage.Go] = new(SourceLanguage.Go, "Go", SupportLevel.TextOnly, TextOnlyNote + " Parsing Tree-sitter pianificato.", "go"),
        [SourceLanguage.Php] = new(SourceLanguage.Php, "PHP", SupportLevel.TextOnly, TextOnlyNote + " Parsing Tree-sitter pianificato.", "php"),
        [SourceLanguage.Markdown] = new(SourceLanguage.Markdown, "Markdown", SupportLevel.TextOnly, TextOnlyNote, "markdown"),
        [SourceLanguage.Unknown] = new(SourceLanguage.Unknown, "Sconosciuto", SupportLevel.TextOnly, "Non riconosciuto.", "text"),
    };

    /// <summary>Estensioni incluse per impostazione predefinita nella scansione.</summary>
    public static IReadOnlyList<string> DefaultExtensions { get; } = ExtensionMap.Select(e => e.Extension).ToArray();

    /// <summary>Tutte le descrizioni dei linguaggi noti (escluso Unknown).</summary>
    public static IReadOnlyList<LanguageInfo> All { get; } =
        Infos.Values.Where(i => i.Language != SourceLanguage.Unknown).OrderByDescending(i => i.CurrentSupport).ThenBy(i => i.DisplayName).ToArray();

    public static SourceLanguage FromExtension(string? extension) =>
        extension is not null && ByExtension.TryGetValue(extension, out var language) ? language : SourceLanguage.Unknown;

    public static SourceLanguage FromPath(string path) => FromExtension(Path.GetExtension(path));

    public static LanguageInfo Describe(SourceLanguage language) =>
        Infos.TryGetValue(language, out var info) ? info : Infos[SourceLanguage.Unknown];
}
