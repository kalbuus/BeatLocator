namespace BeatLocator.Settings;

internal enum ScoreSaberPlayedFilter
{
    Any = 0,
    Played = 1,
    New = 2
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
