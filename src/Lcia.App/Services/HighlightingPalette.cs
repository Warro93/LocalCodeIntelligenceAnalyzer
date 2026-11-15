using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Lcia.App.Services;

/// <summary>
/// Adatta i colori dell'evidenziazione della sintassi di AvalonEdit al tema: nel tema scuro i colori
/// troppo scuri (pensati per lo sfondo bianco) vengono schiariti mantenendo la tinta; nel tema chiaro
/// vengono ripristinati gli originali.
/// </summary>
internal static class HighlightingPalette
{
    private static readonly Dictionary<HighlightingColor, HighlightingBrush?> Originals = new(ReferenceEqualityComparer.Instance);

    public static void Apply(IHighlightingDefinition? definition, bool dark)
    {
        if (definition is null)
        {
            return;
        }

        foreach (var color in definition.NamedHighlightingColors)
        {
            if (!Originals.TryGetValue(color, out var original))
            {
                original = color.Foreground;
                Originals[color] = original;
            }

            var target = original;
            if (dark && original?.GetColor(null!) is Color value)
            {
                target = new SimpleHighlightingBrush(ForDarkBackground(value));
            }

            try
            {
                color.Foreground = target;
            }
            catch (InvalidOperationException)
            {
                // Colore non modificabile (definizione congelata): resta quello originale.
            }
        }
    }

    /// <summary>Schiarisce un colore pensato per sfondo chiaro (stessa tinta, luminosità almeno 0,68).</summary>
    internal static Color ForDarkBackground(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        if (lightness >= 0.6)
        {
            return color;
        }

        double hue = 0, saturation = 0;
        if (max > min)
        {
            var delta = max - min;
            saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
            if (max == r)
            {
                hue = ((g - b) / delta + (g < b ? 6 : 0)) / 6;
            }
            else if (max == g)
            {
                hue = ((b - r) / delta + 2) / 6;
            }
            else
            {
                hue = ((r - g) / delta + 4) / 6;
            }
        }

        return FromHsl(hue, saturation * 0.75, Math.Max(lightness, 0.68), color.A);
    }

    private static Color FromHsl(double hue, double saturation, double lightness, byte alpha)
    {
        double r, g, b;
        if (saturation <= 0)
        {
            r = g = b = lightness;
        }
        else
        {
            var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
            var p = 2 * lightness - q;
            r = HueToChannel(p, q, hue + 1.0 / 3);
            g = HueToChannel(p, q, hue);
            b = HueToChannel(p, q, hue - 1.0 / 3);
        }

        return Color.FromArgb(alpha, ToByte(r), ToByte(g), ToByte(b));
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6)
        {
            return p + (q - p) * 6 * t;
        }

        if (t < 0.5)
        {
            return q;
        }

        if (t < 2.0 / 3)
        {
            return p + (q - p) * (2.0 / 3 - t) * 6;
        }

        return p;
    }

    private static byte ToByte(double channel) => (byte)Math.Clamp(Math.Round(channel * 255), 0, 255);
}
