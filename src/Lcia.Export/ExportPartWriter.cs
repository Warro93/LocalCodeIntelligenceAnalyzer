using System.Text;
using Lcia.Application;

namespace Lcia.Export;

/// <summary>
/// Scrive il dossier in streaming (UTF-8 senza BOM), creando una nuova parte quando si supera
/// il limite di caratteri. Un file non viene mai spezzato tra due parti, salvo che da solo
/// superi il limite: in quel caso viene diviso per righe con avvisi di continuazione.
/// Non sovrascrive mai file esistenti.
/// </summary>
internal sealed class ExportPartWriter : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly string _directory;
    private readonly string _baseName;
    private readonly long _maxCharacters;
    private readonly ExportFormatter _formatter;
    private readonly List<PartState> _parts = [];
    private StreamWriter? _writer;

    public ExportPartWriter(string directory, string baseName, long maxCharacters, ExportFormatter formatter)
    {
        _directory = directory;
        _baseName = baseName;
        _maxCharacters = maxCharacters;
        _formatter = formatter;
    }

    public string IndexFileName => _baseName + ".index" + _formatter.Extension;

    public int PartCount => _parts.Count;

    public async Task WriteAsync(string text, string? fileLabel, CancellationToken cancellationToken)
    {
        if (_parts.Count == 0)
        {
            await StartPartAsync(cancellationToken).ConfigureAwait(false);
        }

        var current = _parts[^1];
        if (_maxCharacters > 0 && current.HasContent && current.Characters + text.Length > _maxCharacters)
        {
            await StartPartAsync(cancellationToken).ConfigureAwait(false);
            current = _parts[^1];
        }

        if (_maxCharacters > 0 && current.Characters + text.Length > _maxCharacters)
        {
            await WriteOversizedAsync(text, fileLabel, cancellationToken).ConfigureAwait(false);
            return;
        }

        await AppendAsync(text, cancellationToken).ConfigureAwait(false);
        current.HasContent = true;
        if (fileLabel is not null)
        {
            current.Files.Add(fileLabel);
        }
    }

    /// <summary>Chiude i file; con una sola parte la rinomina senza il suffisso ".part001".</summary>
    public async Task<IReadOnlyList<ExportPartInfo>> CompleteAsync()
    {
        await CloseWriterAsync().ConfigureAwait(false);
        if (_parts.Count == 1)
        {
            var single = _parts[0];
            var finalPath = UniquePath(Path.Combine(_directory, _baseName + _formatter.Extension));
            File.Move(single.Path, finalPath);
            single.Path = finalPath;
        }

        return _parts.Select(p => new ExportPartInfo(p.Number, p.Path, p.Characters, TokenEstimator.Estimate(p.Characters), p.Files.ToArray())).ToArray();
    }

    /// <summary>Elimina i file parziali in caso di errore o annullamento.</summary>
    public async Task AbortAsync()
    {
        await CloseWriterAsync().ConfigureAwait(false);
        foreach (var part in _parts)
        {
            try
            {
                File.Delete(part.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignorato: il file resta nella cartella di destinazione scelta dall'utente.
            }
        }
    }

    public async ValueTask DisposeAsync() => await CloseWriterAsync().ConfigureAwait(false);

    private async Task WriteOversizedAsync(string text, string? fileLabel, CancellationToken cancellationToken)
    {
        var budget = Math.Max(1000, _maxCharacters - 600);
        var segments = Split(text, budget);
        for (var i = 0; i < segments.Count; i++)
        {
            var current = _parts[^1];
            if (current.HasContent)
            {
                await StartPartAsync(cancellationToken).ConfigureAwait(false);
                current = _parts[^1];
            }

            if (i > 0)
            {
                await AppendAsync(_formatter.ContinuedNotice(fileLabel ?? string.Empty, current.Number - 1), cancellationToken).ConfigureAwait(false);
            }

            await AppendAsync(segments[i], cancellationToken).ConfigureAwait(false);
            current.HasContent = true;
            if (fileLabel is not null)
            {
                current.Files.Add(segments.Count > 1 ? $"{fileLabel} (segment {i + 1}/{segments.Count})" : fileLabel);
            }

            if (i < segments.Count - 1)
            {
                await AppendAsync(_formatter.ContinuationNotice(fileLabel ?? string.Empty, current.Number + 1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static List<string> Split(string text, long budget)
    {
        var segments = new List<string>();
        var builder = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            var piece = line + "\n";
            while (piece.Length > budget)
            {
                if (builder.Length > 0)
                {
                    segments.Add(builder.ToString());
                    builder.Clear();
                }

                segments.Add(piece[..(int)budget]);
                piece = piece[(int)budget..];
            }

            if (builder.Length + piece.Length > budget && builder.Length > 0)
            {
                segments.Add(builder.ToString());
                builder.Clear();
            }

            builder.Append(piece);
        }

        if (builder.Length > 0)
        {
            segments.Add(builder.ToString());
        }

        return segments;
    }

    private async Task StartPartAsync(CancellationToken cancellationToken)
    {
        await CloseWriterAsync().ConfigureAwait(false);
        var number = _parts.Count + 1;
        var path = Path.Combine(_directory, $"{_baseName}.part{number:000}{_formatter.Extension}");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, Utf8);
        var part = new PartState { Number = number, Path = path };
        _parts.Add(part);
        if (number > 1)
        {
            await AppendAsync(_formatter.PartHeader(number, _baseName, IndexFileName), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AppendAsync(string text, CancellationToken cancellationToken)
    {
        await _writer!.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        _parts[^1].Characters += text.Length;
    }

    private async Task CloseWriterAsync()
    {
        if (_writer is null)
        {
            return;
        }

        await _writer.FlushAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
        _writer = null;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name}-{i}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private sealed class PartState
    {
        public int Number { get; init; }
        public string Path { get; set; } = string.Empty;
        public long Characters { get; set; }
        public bool HasContent { get; set; }
        public List<string> Files { get; } = [];
    }
}
