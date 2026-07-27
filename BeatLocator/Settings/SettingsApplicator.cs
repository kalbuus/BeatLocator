using System.Collections.Generic;
using Zenject;

namespace BeatLocator.Settings;

/// <summary>
/// Applies the current saved configuration to every registered subsystem.
/// </summary>
internal class SettingsApplicator : IInitializable
{
    private readonly PluginConfig config;
    private readonly IReadOnlyList<ISettingsApplier> appliers;

    public SettingsApplicator(PluginConfig config, List<ISettingsApplier> appliers)
    {
        this.config = config;
        this.appliers = appliers;
    }

    public void Initialize()
    {
        ApplyAll();
    }

    public void ApplyAll()
    {
        foreach (var applier in appliers)
        {
            try
            {
                applier.Apply(config);
            }
            catch (System.Exception exception)
            {
                Plugin.Log.Error($"Failed to apply settings with {applier.GetType().Name}: {exception}");
            }
        }
    }
}
