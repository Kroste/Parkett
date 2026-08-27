using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using Parkett.Licensing;
using Parkett.Localization;
using Parkett.Persistence;

namespace Parkett.ViewModels;

/// <summary>Auswahleintrag des Sprachumschalters — Flagge plus Eigenbezeichnung.</summary>
public sealed record CultureOption(string Iso, string Display, string Flag)
{
    public override string ToString() => $"{Flag} {Display}";
}

/// <summary>
/// Auswahleintrag des Gebührenmodells. Die Bezeichnung wird bei jedem Zugriff übersetzt,
/// damit der Sprachwechsel auch in einer bereits gefüllten ComboBox greift.
/// </summary>
public sealed record FeeOption(string Id, string ResourceKey)
{
    public string Display => L.T(ResourceKey);

    public override string ToString() => Display;
}

public sealed partial class SettingsWindowViewModel : ViewModelBase
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly SettingsService _settingsService;
    private readonly LicenseVerifier _verifier;

    private AppSettings _settings;

    [ObservableProperty]
    private string _licenseKey = string.Empty;

    [ObservableProperty]
    private string _licenseFeedback = string.Empty;

    public SettingsWindowViewModel(SettingsService settingsService, LicenseVerifier verifier)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));

        _settings = _settingsService.Load();
        _licenseKey = _settings.LicenseKey ?? string.Empty;

        Cultures = LocalizationService.SupportedCultures
            .Select(c => new CultureOption(c.Iso, c.Display, c.Flag))
            .ToList();

        _selectedCulture = Cultures.FirstOrDefault(c => c.Iso == LocalizationService.Instance.CurrentIso)
                           ?? Cultures[0];

        _feeOptions = BuildFeeOptions();

        _selectedFee = FeeOptions.FirstOrDefault(f => f.Id == _settings.FeeModel) ?? FeeOptions[1];

        LocalizationService.Instance.PropertyChanged += (_, _) => OnLanguageChanged();
    }

    /// <summary>
    /// Die ComboBox rendert ihre Einträge über ToString und merkt sich das Ergebnis.
    /// Beim Sprachwechsel muss die Liste deshalb neu gebunden werden — und dabei geht
    /// die Auswahl verloren, also vorher merken und danach zurücksetzen.
    /// </summary>
    private void OnLanguageChanged()
    {
        var selectedId = SelectedFee.Id;

        FeeOptions = BuildFeeOptions();
        SelectedFee = FeeOptions.First(f => f.Id == selectedId);
    }

    private static IReadOnlyList<FeeOption> BuildFeeOptions() =>
    [
        new FeeOption("Free", "Fee_Free"),
        new FeeOption("Neobroker", "Fee_Neobroker"),
        new FeeOption("Hausbank", "Fee_Hausbank"),
    ];

    public IReadOnlyList<CultureOption> Cultures { get; }

    /// <summary>
    /// Wird beim Sprachwechsel durch eine NEUE Listeninstanz ersetzt. Dieselbe Instanz erneut
    /// zu melden reicht nicht: die ComboBox rendert ihre Einträge einmal über ToString und
    /// baut nur bei einem echten ItemsSource-Wechsel neu auf.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<FeeOption> _feeOptions;

    [ObservableProperty]
    private CultureOption _selectedCulture;

    [ObservableProperty]
    private FeeOption _selectedFee;

    partial void OnSelectedCultureChanged(CultureOption value)
    {
        // Wirkt sofort in allen Fenstern — kein Neustart-Hinweis.
        LocalizationService.Instance.SetCulture(value.Iso);
        Persist(_settings with { UiCulture = value.Iso });

        Log.Info("UI-Sprache gewechselt auf {Iso}.", value.Iso);
    }

    partial void OnSelectedFeeChanged(FeeOption value)
    {
        Persist(_settings with { FeeModel = value.Id });
        Log.Info("Gebührenmodell gewechselt auf {Model}.", value.Id);
    }

    [RelayCommand]
    private void ApplyLicense()
    {
        var key = LicenseKey.Trim();

        if (key.Length == 0)
        {
            Persist(_settings with { LicenseKey = null });
            LicenseFeedback = string.Empty;
            return;
        }

        if (!_verifier.IsConfigured)
        {
            // Entwicklungsbuild ohne hinterlegten öffentlichen Schlüssel: Eingabe annehmen,
            // aber ehrlich sagen, dass hier nichts geprüft werden kann.
            LicenseFeedback = L.T("Settings_License_Unchecked");
            return;
        }

        var result = _verifier.Check(key, DateTimeOffset.UtcNow);

        if (!result.IsValid)
        {
            LicenseFeedback = L.T("Settings_License_Invalid");
            return;
        }

        Persist(_settings with { LicenseKey = key });
        LicenseFeedback = L.T("Settings_License_Restart");
    }

    private void Persist(AppSettings updated)
    {
        _settings = updated;
        _settingsService.Save(_settings);
    }

    /// <summary>Aktuell gewählte Kultur als CultureInfo — für Vorschauen und Tests.</summary>
    public CultureInfo CurrentCulture => CultureInfo.GetCultureInfo(SelectedCulture.Iso);
}
