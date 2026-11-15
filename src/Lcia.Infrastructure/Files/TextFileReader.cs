using System.Security.Cryptography;
using Lcia.Application;

namespace Lcia.Infrastructure;

/// <summary>
/// Lettura in sola lettura dei file del repository: condivisione con gli altri processi
/// (FileShare.ReadWrite | Delete) così da non bloccare editor e strumenti aperti.
/// </summary>
public sealed class TextFileReader : ITextFileReader
{
    public TextReadResult Read(string fullPath, long maxBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var stream = new FileStream(fullPath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.SequentialScan,
            });

            var length = stream.Length;
            if (length > maxBytes)
            {
                return TextReadResult.Failure(TextReadStatus.TooLarge, length,
                    $"Dimensione {length:N0} byte oltre il limite di {maxBytes:N0} byte.");
            }

            var bytes = new byte[length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            if (offset < bytes.Length)
            {
                Array.Resize(ref bytes, offset);
            }

            var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (TextDecoding.LooksBinary(bytes))
            {
                return TextReadResult.Failure(TextReadStatus.Binary, bytes.Length, null, sha256);
            }

            var (text, encodingName) = TextDecoding.Decode(bytes);
            return new TextReadResult(TextReadStatus.Ok, text, encodingName, sha256, TextLines.Count(text), bytes.Length, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return TextReadResult.Failure(TextReadStatus.Unreadable, 0, ex.Message);
        }
    }
}
