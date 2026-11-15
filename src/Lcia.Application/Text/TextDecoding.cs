using System.Text;

namespace Lcia.Application;

/// <summary>
/// Decodifica dei file di testo: BOM (UTF-8/16/32), UTF-8 rigoroso, fallback Windows-1252
/// (tipico dei sorgenti legacy italiani). Rilevamento euristico dei file binari.
/// </summary>
public static class TextDecoding
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding LegacyEncoding = CreateLegacyEncoding();

    /// <summary>True se i primi byte contengono un BOM Unicode.</summary>
    public static bool HasUnicodeBom(ReadOnlySpan<byte> bytes) =>
        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ||
        (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) ||
        (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF);

    /// <summary>Euristica: un byte NUL nei primi 8000 byte (senza BOM UTF-16/32) indica un file binario.</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        if (HasUnicodeBom(bytes))
        {
            return false;
        }

        var length = Math.Min(bytes.Length, 8000);
        for (var i = 0; i < length; i++)
        {
            if (bytes[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    public static (string Text, string EncodingName) Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3), "utf-8-bom");
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: false).GetString(bytes, 4, bytes.Length - 4), "utf-32le");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "utf-16le");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "utf-16be");
        }

        try
        {
            return (StrictUtf8.GetString(bytes), "utf-8");
        }
        catch (DecoderFallbackException)
        {
            return (LegacyEncoding.GetString(bytes), LegacyEncoding.WebName);
        }
    }

    private static Encoding CreateLegacyEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }
}
