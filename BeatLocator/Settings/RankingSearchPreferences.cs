namespace BeatLocator.Settings;

internal enum ScoreSaberPlayedFilter
{
    Any = 0,
    Played = 1,
    New = 2
}

internal enum SongDurationFilter
{
    Any = 0,
    Short = 1,
    Small = 2,
    Normal = 3,
    Long = 4
}

internal static class SongDurationFilterExtensions
{
    internal static int? GetMinimumSeconds(this SongDurationFilter filter)
    {
        return filter switch
        {
            SongDurationFilter.Short => 0,
            SongDurationFilter.Small => 60,
            SongDurationFilter.Normal => 120,
            SongDurationFilter.Long => 240,
            _ => null
        };
    }

    internal static int? GetMaximumSeconds(this SongDurationFilter filter)
    {
        return filter switch
        {
            SongDurationFilter.Short => 60,
            SongDurationFilter.Small => 150,
            SongDurationFilter.Normal => 240,
            _ => null
        };
    }

    internal static bool Matches(this SongDurationFilter filter, int? durationSeconds)
    {
        if (filter == SongDurationFilter.Any) return true;
        if (!durationSeconds.HasValue) return false;

        var minimum = filter.GetMinimumSeconds();
        var maximum = filter.GetMaximumSeconds();
        if (!minimum.HasValue && !maximum.HasValue) return false;

        return (!minimum.HasValue || durationSeconds.Value >= minimum.Value) &&
               (!maximum.HasValue || durationSeconds.Value <= maximum.Value);
    }
}

/// <summary>
/// Provider-neutral access to the shared ranking search preferences. The
/// underlying legacy property names remain unchanged for config compatibility.
/// </summary>
internal sealed class RankingSearchPreferences
{
    private readonly PluginConfig _config;

    internal RankingSearchPreferences(PluginConfig config)
    {
        _config = config;
    }

    internal int DifficultySelection
    {
        get => _config.BeatLeaderDifficultySelection;
        set
        {
            if (_config.BeatLeaderDifficultySelection != value)
            {
                _config.BeatLeaderDifficultySelection = value;
            }
        }
    }

    internal int BalanceSelection
    {
        get => _config.BeatLeaderBalanceSelection;
        set
        {
            if (_config.BeatLeaderBalanceSelection != value)
            {
                _config.BeatLeaderBalanceSelection = value;
            }
        }
    }

    internal bool PlayedEnabled
    {
        get => _config.BeatLeaderPlayedEnabled;
        set
        {
            if (_config.BeatLeaderPlayedEnabled != value)
            {
                _config.BeatLeaderPlayedEnabled = value;
            }
        }
    }

    internal bool TwoSaberEnabled
    {
        get => _config.BeatLeaderTwoSaberEnabled;
        set
        {
            if (_config.BeatLeaderTwoSaberEnabled != value)
            {
                _config.BeatLeaderTwoSaberEnabled = value;
            }
        }
    }

    internal bool SecretDifficultyEnabled
    {
        get => _config.BeatLeaderSecretDifficultyEnabled;
        set
        {
            if (_config.BeatLeaderSecretDifficultyEnabled != value)
            {
                _config.BeatLeaderSecretDifficultyEnabled = value;
            }
        }
    }

    internal SongDurationFilter DurationSelection
    {
        get
        {
            var value = _config.DurationSelection;
            return value >= (int)SongDurationFilter.Any &&
                   value <= (int)SongDurationFilter.Long
                ? (SongDurationFilter)value
                : SongDurationFilter.Any;
        }
        set
        {
            if (_config.DurationSelection != (int)value)
            {
                _config.DurationSelection = (int)value;
            }
        }
    }

    internal ScoreSaberPlayedFilter ScoreSaberPlayedSelection
    {
        get
        {
            var value = _config.ScoreSaberPlayedSelection;
            return value >= (int)ScoreSaberPlayedFilter.Any &&
                   value <= (int)ScoreSaberPlayedFilter.New
                ? (ScoreSaberPlayedFilter)value
                : ScoreSaberPlayedFilter.Any;
        }
        set
        {
            if (_config.ScoreSaberPlayedSelection != (int)value)
            {
                _config.ScoreSaberPlayedSelection = (int)value;
            }
        }
    }
}
