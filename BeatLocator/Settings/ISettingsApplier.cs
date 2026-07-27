namespace BeatLocator.Settings;

/// <summary>
/// Implement this interface in a subsystem that needs to react when the user
/// confirms changes in BeatLocator's Mod Settings screen.
/// </summary>
internal interface ISettingsApplier
{
    void Apply(PluginConfig config);
}
