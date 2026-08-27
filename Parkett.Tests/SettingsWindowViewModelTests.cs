using FluentAssertions;
using Parkett.Licensing;
using Parkett.Localization;
using Parkett.Persistence;
using Parkett.ViewModels;

namespace Parkett.Tests;

/// <summary>
/// Regression zum Absturz beim Sprachwechsel: Der Wechsel baut <c>FeeOptions</c> neu auf,
/// die ComboBox verwirft daraufhin ihre Auswahl und schreibt <c>null</c> ins ViewModel
/// zurück — der Setter lief damit in eine NullReferenceException. Die Tests spielen genau
/// diese Reihenfolge nach, ohne UI.
/// </summary>
[Collection("Localization")]
public class SettingsWindowViewModelTests : IDisposable
{
    private readonly System.Globalization.CultureInfo _original = LocalizationService.Instance.Current;
    private readonly TempDirectory _dir = new();

    public void Dispose()
    {
        LocalizationService.Instance.Current = _original;
        _dir.Dispose();
    }

    private (SettingsWindowViewModel Vm, SettingsService Service) Create(string feeModel)
    {
        var service = new SettingsService(_dir.Path, new TestProtector());
        service.Save(AppSettings.Default with { FeeModel = feeModel });

        LocalizationService.Instance.SetCulture("de");

        return (new SettingsWindowViewModel(service, new LicenseVerifier(string.Empty)), service);
    }

    [Fact]
    public void Eine_von_der_ComboBox_geleerte_Auswahl_stuerzt_nicht_ab()
    {
        var (vm, _) = Create("Hausbank");

        // Das macht die ComboBox beim Neubinden der Liste.
        var act = () => vm.SelectedFee = null;

        act.Should().NotThrow();
    }

    [Fact]
    public void Sprachwechsel_behaelt_das_gewaehlte_Gebuehrenmodell()
    {
        var (vm, _) = Create("Hausbank");

        LocalizationService.Instance.SetCulture("en");

        vm.SelectedFee.Should().NotBeNull();
        vm.SelectedFee!.Id.Should().Be("Hausbank");
    }

    [Fact]
    public void Sprachwechsel_uebersetzt_die_Gebuehreneintraege_neu()
    {
        var (vm, _) = Create("Neobroker");
        var deutsch = vm.FeeOptions.Select(f => f.Display).ToList();

        LocalizationService.Instance.SetCulture("en");

        vm.FeeOptions.Select(f => f.Display).Should().NotEqual(deutsch);
    }

    [Fact]
    public void Eine_geleerte_Auswahl_ueberschreibt_die_Einstellungen_nicht()
    {
        var (vm, service) = Create("Hausbank");

        vm.SelectedFee = null;

        service.Load().FeeModel.Should().Be("Hausbank");
    }

    [Fact]
    public void Ein_echter_Wechsel_wird_gespeichert()
    {
        var (vm, service) = Create("Hausbank");

        vm.SelectedFee = vm.FeeOptions.First(f => f.Id == "Free");

        service.Load().FeeModel.Should().Be("Free");
    }
}
