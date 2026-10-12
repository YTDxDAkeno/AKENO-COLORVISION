namespace Akeno.ColorVision.Core.Gaming;

public enum SwitchActionKind
{
    None,

    /// <summary>A game came to the foreground: activate its profile.</summary>
    ActivateGameProfile,

    /// <summary>The game is no longer in front (after the delay): restore what was active before.</summary>
    RestorePrevious,
}

public sealed record SwitchAction(SwitchActionKind Kind, Guid? ProfileId = null, bool? FilterOn = null, GameRule? Rule = null)
{
    public static SwitchAction None { get; } = new(SwitchActionKind.None);
}

/// <summary>
/// Pure decision logic for automatic game profiles (no Windows calls, fully unit-testable):
/// <code>
///   game focused  → remember (profile, filter on/off), activate game profile
///   game loses focus or closes → wait <see cref="RestoreDelay"/> → restore remembered state
///   user picks a profile manually while in game mode → keep the user's choice, cancel restore
/// </code>
/// The delay avoids flicker when quickly alt-tabbing.
/// </summary>
public sealed class AutoProfileSwitcher
{
    private readonly Func<DateTimeOffset> _clock;
    private Guid? _previousProfile;
    private bool _previousFilterOn;
    private DateTimeOffset? _restoreAt;

    public AutoProfileSwitcher(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public TimeSpan RestoreDelay { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Whether a game profile is currently active because of a detected game.</summary>
    public bool InGameMode => ActiveRule is not null;

    public GameRule? ActiveRule { get; private set; }

    /// <summary>Called whenever the foreground window changes.</summary>
    /// <param name="matchedRule">The rule matching the new foreground app, or null.</param>
    /// <param name="gameProfileId">Profile to use for that rule.</param>
    /// <param name="enableFilterInGame">Whether to turn the filter on while the game is in front.</param>
    public SwitchAction OnForegroundChanged(GameRule? matchedRule, Guid gameProfileId, Guid currentProfileId, bool filterOn, bool enableFilterInGame)
    {
        if (matchedRule is not null)
        {
            _restoreAt = null;
            if (ActiveRule is not null)
            {
                // Still (or again) in a game; switch only if it is a different game with a different profile.
                if (ActiveRule.Id == matchedRule.Id)
                {
                    return SwitchAction.None;
                }

                ActiveRule = matchedRule;
                return new SwitchAction(SwitchActionKind.ActivateGameProfile, gameProfileId, enableFilterInGame ? true : null, matchedRule);
            }

            _previousProfile = currentProfileId;
            _previousFilterOn = filterOn;
            ActiveRule = matchedRule;
            return new SwitchAction(SwitchActionKind.ActivateGameProfile, gameProfileId, enableFilterInGame ? true : null, matchedRule);
        }

        if (ActiveRule is not null && _restoreAt is null)
        {
            _restoreAt = _clock() + RestoreDelay;
        }

        return SwitchAction.None;
    }

    /// <summary>Call periodically (e.g. every 250 ms); returns the restore action once the delay has passed.</summary>
    public SwitchAction Tick()
    {
        if (_restoreAt is { } at && _clock() >= at)
        {
            _restoreAt = null;
            var action = new SwitchAction(SwitchActionKind.RestorePrevious, _previousProfile, _previousFilterOn, ActiveRule);
            ActiveRule = null;
            _previousProfile = null;
            return action;
        }

        return SwitchAction.None;
    }

    /// <summary>The user changed profile or toggled the filter manually while a game profile was active.</summary>
    public void OnUserOverride()
    {
        ActiveRule = null;
        _restoreAt = null;
        _previousProfile = null;
    }

    /// <summary>Feature turned off: restore immediately if a game profile is active.</summary>
    public SwitchAction Reset()
    {
        if (ActiveRule is null)
        {
            return SwitchAction.None;
        }

        var action = new SwitchAction(SwitchActionKind.RestorePrevious, _previousProfile, _previousFilterOn, ActiveRule);
        ActiveRule = null;
        _restoreAt = null;
        _previousProfile = null;
        return action;
    }
}
