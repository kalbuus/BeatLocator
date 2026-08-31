using System.Runtime.CompilerServices;
using IPA.Config.Stores;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace BeatLocator;

internal class PluginConfig
{
    // Members must be 'virtual' if you want BSIPA to detect a value change and save the config automatically
    // You can assign a default value to be used when the config is first created by assigning one after '=' 
    // examples:
    // public virtual bool FeatureEnabled { get; set; } = true;
    // public virtual int NumValue { get; set; } = 42;
    // public virtual Color TheColor { get; set; } = new Color(0.12f, 0.34f, 0.56f);

    public virtual float SpeedAnimationValue { get; set; } = 1f;

    public virtual bool isEndMePossible { get; set; } = true;

    // Shared search-screen values for every ranking provider. The legacy
    // BeatLeader prefix is retained so existing config files keep their values.
    public virtual int BeatLeaderDifficultySelection { get; set; } = 0;

    public virtual int BeatLeaderBalanceSelection { get; set; } = 0;

    public virtual bool BeatLeaderPlayedEnabled { get; set; } = false;

    public virtual bool BeatLeaderTwoSaberEnabled { get; set; } = false;

    public virtual bool BeatLeaderSecretDifficultyEnabled { get; set; } = false;

    public virtual bool BeatLeaderBotSpeechEnabled { get; set; } = true;

    public virtual int DurationSelection { get; set; } = 0;

    public virtual int ScoreSaberPlayedSelection { get; set; } = 0;

    /*
    /// <summary>
    /// This is called whenever BSIPA reads the config from disk (including when file changes are detected).
    /// </summary>
    public virtual void OnReload() { }

    /// <summary>
    /// Call this to force BSIPA to update the config file. This is also called by BSIPA if it detects the file was modified.
    /// </summary>
    public virtual void Changed() { }

    /// <summary>
    /// Call this to have BSIPA copy the values from <paramref name="other"/> into this config.
    /// </summary>
    public virtual void CopyFrom(PluginConfig other) { }
    */
}
