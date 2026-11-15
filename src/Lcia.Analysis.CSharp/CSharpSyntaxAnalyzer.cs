using System.Globalization;
using Lcia.Application;
using Lcia.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lcia.Analysis.CSharp;

/// <summary>
/// Analisi sintattica C# con Roslyn: non richiede che il progetto compili né che le
/// dipendenze siano disponibili. Le relazioni prodotte non sono risolte semanticamente
/// e sono classificate come "Inferita con evidenza" o "Riferimento testuale".
/// </summary>
public sealed class CSharpSyntaxAnalyzer : ISourceAnalyzer
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview)
        .WithDocumentationMode(DocumentationMode.Parse);

    public string Name => "Roslyn C# (sintassi)";

    public bool CanAnalyze(SourceLanguage language) => language == SourceLanguage.CSharp;

    public SourceAnalysisResult Analyze(string relativePath, string text, CancellationToken cancellationToken)
    {
        var tree = CSharpSyntaxTree.ParseText(text, options: ParseOptions, path: relativePath, cancellationToken: cancellationToken);
        var root = tree.GetCompilationUnitRoot(cancellationToken);

        var collector = new DeclarationCollector(relativePath, cancellationToken);
        collector.Visit(root);

        var diagnostics = new List<string>();
        var errors = tree.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            var first = errors[0];
            var line = first.Location.GetLineSpan().StartLinePosition.Line + 1;
            diagnostics.Add($"{errors.Count} errori di sintassi (il primo alla riga {line}: {first.GetMessage(CultureInfo.GetCultureInfo("it-IT"))}). " +
                            "Simboli estratti in modalità tollerante.");
        }

        return new SourceAnalysisResult(collector.Symbols, collector.Relations, SupportLevel.Syntactic, diagnostics);
    }
}
