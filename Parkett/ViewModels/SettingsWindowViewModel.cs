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

/// <summary>Auswahleintrag des Gebührenmodells.</summary>
public sealed record FeeOption(string Id, string Display)
{
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

        FeeOptions =
        [
            new FeeOption("Free", "Ohne Gebühren"),
            new FeeOption("Neobroker", "Neobroker — 1,00 € je Order"),
            new FeeOption("Hausbank", "Hausbank — 4,90 € + 0,25 %, min. 9,90 €"),
        ];

        _selectedFee = FeeOptions.FirstOrDefault(f => f.Id == _settings.FeeModel) ?? FeeOptions[1];
    }

    public IReadOnlyList<CultureOption> Cultures { get; }

    public IReadOnlyList<FeeOption> FeeOptions { get; }

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
            LicenseFeedback = "Dieser Build prüft keine Lizenzen.";
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
