using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Akeno.ColorVision.App.Imaging;
using Akeno.ColorVision.App.Infrastructure;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Profiles;

namespace Akeno.ColorVision.App.ViewModels;

public sealed record SwatchPairViewModel(string Label, Brush First, Brush Second, string FirstName, string SecondName);

/// <summary>Color Filters page: pick a preset, adjust strength, and edit custom profiles with real matrix parameters.</summary>
public sealed class FiltersViewModel : PageViewModel
{
    private readonly DispatcherTimer _persistTimer;
    private ColorProfile _selected;
    private bool _loading;

    public FiltersViewModel(AppHost host)
        : base(host)
    {
        _selected = host.Profiles.Active;
        _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _persistTimer.Tick += (_, _) =>
        {
            _persistTimer.Stop();
            Host.Profiles.Persist(_selected);
        };

        UseCommand = new RelayCommand(() => Host.Profiles.SetActive(_selected.Id), () => !IsActive);
        UseAndTurnOnCommand = new RelayCommand(() =>
        {
            Host.Profiles.SetActive(_selected.Id);
            if (!Host.Filter.IsOn)
            {
                Host.ToggleFilterFromUser();
            }
        });
        CustomizeCopyCommand = new RelayCommand(CustomizeCopy);
        ResetParametersCommand = new RelayCommand(ResetParameters, () => IsEditable);

        Host.Profiles.ActiveProfileChanged += (_, _) => RefreshAll();
        Host.Profiles.ProfilesChanged += (_, _) => OnPropertyChanged(nameof(Profiles));
        Host.Filter.StatusChanged += (_, _) => RefreshPreview();
        RefreshPreview();
    }

    public ObservableCollection<ColorProfile> Profiles => Host.Profiles.Profiles;

    public ColorProfile SelectedProfile
    {
        get => _selected;
        set
        {
            if (value is null || ReferenceEquals(value, _selected))
            {
                return;
            }

            _persistTimer.Stop();
            Host.Profiles.Persist(_selected);
            _selected = value;
            RefreshAll();
        }
    }

    public string Name => _selected.Name;

    public string Description => _selected.Description;

    public bool IsBuiltIn => _selected.IsBuiltIn;

    public bool IsEditable => !_selected.IsBuiltIn;

    public bool IsActive => Host.Profiles.Active.Id == _selected.Id;

    public string ActiveText => IsActive ? (Host.Filter.IsOn ? "Active — the filter is on" : "Active — the filter is off") : "Not active";

    public double Intensity
    {
        get => _selected.Intensity;
        set
        {
            Host.Profiles.SetIntensity(_selected, value, persist: false);
            SchedulePersist();
            OnPropertyChanged(nameof(IntensityText));
            RefreshPreview();
        }
    }

    public string IntensityText => $"{_selected.Intensity * 100:0} %";

    // ---- Custom parameters (each maps to a real matrix operation; see CorrectionPipeline) ----

    public string BaseModel
    {
        get => _selected.Parameters.Base.ToString();
        set
        {
            if (Enum.TryParse<BaseCorrection>(value, out var model))
            {
                Update(p => p with { Base = model });
            }
        }
    }

    public double ModelSeverity
    {
        get => _selected.Parameters.ModelSeverity;
        set => Update(p => p with { ModelSeverity = value });
    }

    public double RedGreenToBlue
    {
        get => _selected.Parameters.RedGreenToBlue;
        set => Update(p => p with { RedGreenToBlue = value });
    }

    public double RedGreenToLightness
    {
        get => _selected.Parameters.RedGreenToLightness;
        set => Update(p => p with { RedGreenToLightness = value });
    }

    public double RedHueShift
    {
        get => _selected.Parameters.RedHueShift;
        set => Update(p => p with { RedHueShift = value });
    }

    public double GreenHueShift
    {
        get => _selected.Parameters.GreenHueShift;
        set => Update(p => p with { GreenHueShift = value });
    }

    public double Saturation
    {
        get => _selected.Parameters.Saturation;
        set => Update(p => p with { Saturation = value });
    }

    public double Contrast
    {
        get => _selected.Parameters.Contrast;
        set => Update(p => p with { Contrast = value });
    }

    public double Brightness
    {
        get => _selected.Parameters.Brightness;
        set => Update(p => p with { Brightness = value });
    }

    public string ParameterSummary
    {
        get
        {
            var p = _selected.Parameters;
            return FormattableString.Invariant(
                $"Model: {p.Base} ({p.ModelSeverity:0.0}) · Blue/yellow {p.RedGreenToBlue:0.00} · Lightness {p.RedGreenToLightness:+0.00;-0.00;0} · Red hue {p.RedHueShift:+0;-0;0}° · Green hue {p.GreenHueShift:+0;-0;0}° · Saturation {p.Saturation:0.00} · Contrast {p.Contrast:0.00} · Brightness {p.Brightness:+0.00;-0.00;0}");
        }
    }

    public string? ValidationWarning
    {
        get
        {
            var v = TransformValidator.Validate(_selected.BuildMatrix());
            if (!v.IsSafe)
            {
                return "These settings are too extreme and will not be applied: " + string.Join(" ", v.Errors);
            }

            return v.Warnings.Count > 0 ? string.Join(" ", v.Warnings) : null;
        }
    }

    public ObservableCollection<SwatchPairViewModel> PreviewPairs { get; } = [];

    public string PreviewNote { get; private set; } = string.Empty;

    public ICommand UseCommand { get; }

    public ICommand UseAndTurnOnCommand { get; }

    public ICommand CustomizeCopyCommand { get; }

    public ICommand ResetParametersCommand { get; }

    public override void OnNavigatedTo()
    {
        _selected = Host.Profiles.Active;
        RefreshAll();
    }

    public override void OnNavigatedFrom()
    {
        _persistTimer.Stop();
        Host.Profiles.Persist(_selected);
    }

    private void Update(Func<CorrectionParameters, CorrectionParameters> change)
    {
        if (_loading || _selected.IsBuiltIn)
        {
            return;
        }

        Host.Profiles.UpdateParameters(_selected, change(_selected.Parameters), persist: false);
        SchedulePersist();
        OnPropertiesChanged(nameof(ParameterSummary), nameof(ValidationWarning));
        RefreshPreview();
    }

    private void SchedulePersist()
    {
        _persistTimer.Stop();
        _persistTimer.Start();
    }

    private void CustomizeCopy()
    {
        var copy = Host.Profiles.CreateCopy(_selected);
        Host.Profiles.SetActive(copy.Id);
        _selected = copy;
        RefreshAll();
    }

    private void ResetParameters()
    {
        if (_selected.IsBuiltIn)
        {
            return;
        }

        Host.Profiles.UpdateParameters(_selected, BuiltInPresets.RedGreenParameters);
        RefreshAll();
    }

    private void RefreshAll()
    {
        _loading = true;
        try
        {
            OnPropertiesChanged(
                nameof(SelectedProfile), nameof(Name), nameof(Description), nameof(IsBuiltIn), nameof(IsEditable), nameof(IsActive), nameof(ActiveText),
                nameof(Intensity), nameof(IntensityText), nameof(BaseModel), nameof(ModelSeverity), nameof(RedGreenToBlue), nameof(RedGreenToLightness),
                nameof(RedHueShift), nameof(GreenHueShift), nameof(Saturation), nameof(Contrast), nameof(Brightness), nameof(ParameterSummary), nameof(ValidationWarning));
        }
        finally
        {
            _loading = false;
        }

        RefreshPreview();
    }

    private void RefreshPreview()
    {
        // With the filter on, the whole screen (including this preview) is already corrected by the
        // active profile, so show raw colors; otherwise simulate the selected profile in software.
        var filterOn = Host.Filter.IsOn;
        var matrix = filterOn ? ColorMatrix.Identity : _selected.BuildMatrix();
        PreviewNote = !filterOn
            ? "Preview of this profile. (The filter is off, so this is calculated by the app.)"
            : IsActive
                ? "The filter is on: these swatches are being corrected live, like the rest of your screen. Hold \"Compare\" to see the original colors."
                : "The filter is on with another profile. Click \"Use this profile\" to try this one on the whole screen.";

        PreviewPairs.Clear();
        foreach (var pair in TestColorSets.CalibrationPairs.Where(p => !p.IsControl).Take(8))
        {
            PreviewPairs.Add(new SwatchPairViewModel(
                pair.Label,
                matrix.TransformClamped(pair.First.Color).ToBrush(),
                matrix.TransformClamped(pair.Second.Color).ToBrush(),
                pair.First.Name,
                pair.Second.Name));
        }

        OnPropertiesChanged(nameof(PreviewNote), nameof(ActiveText), nameof(IsActive));
    }
}
