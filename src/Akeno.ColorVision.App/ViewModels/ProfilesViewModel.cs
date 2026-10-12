using System.Collections.ObjectModel;
using System.Windows.Input;
using Akeno.ColorVision.App.Infrastructure;
using Akeno.ColorVision.App.Services;
using Akeno.ColorVision.App.Views;
using Akeno.ColorVision.ColorScience;
using Akeno.ColorVision.Core.Profiles;
using Microsoft.Win32;

namespace Akeno.ColorVision.App.ViewModels;

/// <summary>Profiles page: activate, duplicate, rename, delete, import and export.</summary>
public sealed class ProfilesViewModel : PageViewModel
{
    private ColorProfile? _selected;
    private string? _message;

    public ProfilesViewModel(AppHost host)
        : base(host)
    {
        _selected = host.Profiles.Active;
        ActivateCommand = new RelayCommand(() => Host.Profiles.SetActive(_selected!.Id), () => _selected is not null && _selected.Id != Host.Profiles.Active.Id);
        DuplicateCommand = new RelayCommand(Duplicate, () => _selected is not null);
        RenameCommand = new RelayCommand(Rename, () => _selected is { IsBuiltIn: false });
        DeleteCommand = new RelayCommand(Delete, () => _selected is { IsBuiltIn: false });
        ExportCommand = new RelayCommand(Export, () => _selected is not null);
        ImportCommand = new RelayCommand(Import);
        ResetStrengthsCommand = new RelayCommand(() =>
        {
            Host.Profiles.ResetPresetStrengths();
            Message = "Built-in presets are back to their default strengths.";
            OnPropertyChanged(nameof(Details));
        });
        Host.Profiles.ActiveProfileChanged += (_, _) => OnPropertiesChanged(nameof(Details), nameof(ActiveName));
    }

    public ObservableCollection<ColorProfile> Profiles => Host.Profiles.Profiles;

    public ColorProfile? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertiesChanged(nameof(Details), nameof(CanEdit));
            }
        }
    }

    public bool CanEdit => _selected is { IsBuiltIn: false };

    public string ActiveName => Host.Profiles.Active.Name;

    public string Details
    {
        get
        {
            if (_selected is null)
            {
                return string.Empty;
            }

            var origin = _selected.Origin switch
            {
                ProfileOrigin.BuiltIn => "Built-in preset (cannot be renamed or deleted; duplicate it to customize).",
                ProfileOrigin.Calibration => $"Created by calibration on {_selected.CreatedUtc.ToLocalTime():d}.",
                ProfileOrigin.Imported => $"Imported on {_selected.CreatedUtc.ToLocalTime():d}.",
                _ => $"Custom profile created on {_selected.CreatedUtc.ToLocalTime():d}.",
            };
            var active = _selected.Id == Host.Profiles.Active.Id ? "This is the active profile." : "Not active.";
            return $"{origin} {active} Strength {_selected.Intensity * 100:0} %.";
        }
    }

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public ICommand ActivateCommand { get; }

    public ICommand DuplicateCommand { get; }

    public ICommand RenameCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand ResetStrengthsCommand { get; }

    public override void OnNavigatedTo() => OnPropertiesChanged(nameof(Details), nameof(ActiveName));

    private void Duplicate()
    {
        var copy = Host.Profiles.CreateCopy(_selected!);
        Selected = copy;
        Message = $"Created \"{copy.Name}\". You can change its settings on the Color Filters page.";
    }

    private void Rename()
    {
        var name = Dialogs.Prompt("Rename profile", "New name for this profile:", _selected!.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            Host.Profiles.Rename(_selected, name);
            Message = $"Renamed to \"{_selected.Name}\".";
            OnPropertyChanged(nameof(Details));
        }
    }

    private void Delete()
    {
        if (!Dialogs.Confirm("Delete profile", $"Delete \"{_selected!.Name}\"? This cannot be undone.", "Delete"))
        {
            return;
        }

        var name = _selected.Name;
        if (Host.Profiles.Delete(_selected))
        {
            Selected = Host.Profiles.Active;
            Message = $"Deleted \"{name}\".";
        }
    }

    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export profile",
            FileName = MakeSafeFileName(_selected!.Name) + ".akeno-profile.json",
            Filter = "Akeno ColorVision profile|*.json",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ProfileService.Export(_selected, dialog.FileName);
            Message = "Profile exported.";
        }
        catch (Exception ex)
        {
            Message = "Export failed: " + ex.Message;
        }
    }

    private void Import()
    {
        var dialog = new OpenFileDialog { Title = "Import profile", Filter = "Akeno ColorVision profile|*.json|All files|*.*" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = Host.Profiles.Import(dialog.FileName);
        if (result.Profile is not { } profile)
        {
            Message = "Import failed: " + result.Error;
            return;
        }

        Selected = profile;
        Message = $"Imported \"{profile.Name}\"." + (result.Warnings.Count > 0 ? " Notes: " + string.Join(" ", result.Warnings) : string.Empty);

        var validation = TransformValidator.Validate(profile.BuildMatrix());
        if (!validation.IsSafe)
        {
            Message += " Its settings are too extreme to apply safely, so it was not activated.";
            return;
        }

        if (Dialogs.Confirm("Try imported profile", "Try this profile on your screen now? You can go back within 15 seconds.", "Try it", "Not now"))
        {
            var previous = Host.Profiles.Active.Id;
            var wasOn = Host.Filter.IsOn;
            Host.Profiles.SetActive(profile.Id);
            if (!wasOn)
            {
                Host.Filter.Enable();
            }

            if (!Dialogs.KeepChanges("Do the colors look OK with the imported profile?"))
            {
                Host.Profiles.SetActive(previous);
                if (!wasOn)
                {
                    Host.Filter.Disable();
                }

                Message = "Went back to your previous profile.";
            }
        }
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
