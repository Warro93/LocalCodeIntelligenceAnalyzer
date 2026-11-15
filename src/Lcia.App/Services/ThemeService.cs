using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.Messaging;
using Lcia.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Lcia.App.Services;

/// <summary>Inviato quando il tema effettivo (chiaro/scuro) cambia.</summary>
public sealed record ThemeChangedMessage(bool IsDark);

/// <summary>
/// Tema dell'interfaccia: chiaro, scuro o come Windows. Usa il tema Fluent di WPF (ThemeMode) per i controlli
/// standard e le palette Theme.Light/Theme.Dark per gli elementi dell'applicazione; il cambio è immediato.
/// Senza Fluent (disattivato nelle impostazioni o non disponibile) resta il tema chiaro classico di Windows.
/// </summary>
public sealed class ThemeService
{
    private const string PaletteMarkerKey = "Lcia.PaletteName";
    private const string PaletteUriFormat = "/LocalCodeIntelligenceAnalyzer;component/Resources/Theme.{0}.xaml";
    private const string FluentUriFormat = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{0}.xaml";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly ISettingsStore _settings;
    private readonly ILogger<ThemeService> _logger;
    private ResourceDictionary? _fallbackFluentDictionary;
    private bool _useThemeModeApi = true;

    public ThemeService(ISettingsStore settings, ILogger<ThemeService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Tema effettivo corrente, per le viste create dai DataTemplate (senza DI).</summary>
    public static bool IsDarkActive { get; private set; }

    public event EventHandler? ThemeChanged;

    /// <summary>Tema scelto dall'utente (Sistema, Chiaro, Scuro).</summary>
    public AppTheme Theme { get; private set; } = AppTheme.System;

    /// <summary>Stile Fluent attivo: necessario per il tema scuro.</summary>
    public bool FluentEnabled { get; private set; }

    public bool IsDark => IsDarkActive;

    /// <summary>Applica il tema salvato nelle impostazioni. Da chiamare prima di mostrare la finestra principale.</summary>
    public void Initialize()
    {
        var settings = _settings.Load();
        FluentEnabled = settings.UseFluentTheme;
        Apply(settings.Theme, force: true);
    }

    /// <summary>Cambia tema subito e lo salva nelle impostazioni.</summary>
    public void SetTheme(AppTheme theme)
    {
        Apply(theme, force: false);
        try
        {
            var settings = _settings.Load();
            settings.Theme = theme;
            _settings.Save(settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossibile salvare il tema scelto.");
        }
    }

    /// <summary>Passa da chiaro a scuro e viceversa (scelta esplicita, non più "come Windows").</summary>
    public void Toggle() => SetTheme(IsDark ? AppTheme.Light : AppTheme.Dark);

    /// <summary>
    /// Barra del titolo in tinta con il tema e ascolto dei cambi chiaro/scuro di Windows (messaggio
    /// WM_SETTINGCHANGE "ImmersiveColorSet") per il tema "Come Windows".
    /// </summary>
    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SourceInitialized += (_, _) =>
        {
            ApplyTitleBar(window, IsDarkActive);
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook(OnWindowMessage);
        };
        ApplyTitleBar(window, IsDarkActive);
    }

    private void Apply(AppTheme theme, bool force)
    {
        Theme = theme;
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        var dark = FluentEnabled && (theme == AppTheme.Dark || (theme == AppTheme.System && IsWindowsInDarkMode()));
        if (!force && dark == IsDarkActive)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (FluentEnabled)
        {
            ApplyFluent(app, dark);
        }

        if (!FluentEnabled)
        {
            dark = false;
        }

        ApplyPalette(app, dark);
        IsDarkActive = dark;
        foreach (Window window in app.Windows)
        {
            ApplyTitleBar(window, dark);
        }

        _logger.LogInformation("Tema applicato: {Theme} ({Effective}).", theme, dark ? "scuro" : "chiaro");
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(dark));
    }

    private void ApplyFluent(System.Windows.Application app, bool dark)
    {
        if (_useThemeModeApi)
        {
            try
            {
#pragma warning disable WPF0001 // API ThemeMode di WPF (.NET 9+) marcata come sperimentale.
                app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ThemeMode non disponibile: carico i dizionari Fluent manualmente.");
                _useThemeModeApi = false;
            }
        }

        try
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, FluentUriFormat, dark ? "Dark" : "Light"), UriKind.Absolute),
            };
            var merged = app.Resources.MergedDictionaries;
            if (_fallbackFluentDictionary is not null)
            {
                merged.Remove(_fallbackFluentDictionary);
            }

            merged.Insert(0, dictionary);
            _fallbackFluentDictionary = dictionary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tema Fluent non disponibile: viene usato il tema classico di Windows (solo chiaro).");
            FluentEnabled = false;
        }
    }

    private static void ApplyPalette(System.Windows.Application app, bool dark)
    {
        var palette = new ResourceDictionary
        {
            Source = new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, PaletteUriFormat, dark ? "Dark" : "Light"), UriKind.Relative),
        };
        var merged = app.Resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].Contains(PaletteMarkerKey))
            {
                merged[i] = palette;
                return;
            }
        }

        merged.Insert(0, palette);
    }

    private static void ApplyTitleBar(Window window, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var value = dark ? 1 : 0;
            _ = NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            // Versione di Windows senza barra del titolo scura: nessun effetto.
        }
    }

    private static bool IsWindowsInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmSettingChange = 0x001A;
        if (message == WmSettingChange && Theme == AppTheme.System && lParam != IntPtr.Zero &&
            string.Equals(Marshal.PtrToStringUni(lParam), "ImmersiveColorSet", StringComparison.Ordinal))
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => Apply(AppTheme.System, force: false));
        }

        return IntPtr.Zero;
    }

    private static class NativeMethods
    {
        public const int DwmwaUseImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
