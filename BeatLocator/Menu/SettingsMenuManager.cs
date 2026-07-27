using System;
using BeatSaberMarkupLanguage.Settings;
using Zenject;

namespace BeatLocator.Menu;

internal class SettingsMenuManager : IInitializable, IDisposable
{
    private readonly SettingsMenu settingsMenu;
    private readonly BSMLSettings bsmlSettings;

    private const string MenuName = nameof(BeatLocator);

    private const string ResourcePath = nameof(BeatLocator) + ".Menu.SettingsMenu.bsml";

    // Zenject will inject our SettingsMenu instance on this object's creation.
    // BSMLSettings is bound by BSML. SiraUtil also lets us inject services from other mods.
    public SettingsMenuManager(SettingsMenu settingsMenu, BSMLSettings bsmlSettings)
    {
        this.settingsMenu = settingsMenu;
        this.bsmlSettings = bsmlSettings;
    }

    // Zenject will call IInitializable.Initialize for any menu bindings when the main menu loads for the first
    // time or when the game restarts internally, such as when settings are applied.
    public void Initialize()
    {
        // Adds a custom menu in the Mod Settings section of the main menu.
        bsmlSettings.AddSettingsMenu(MenuName, ResourcePath, settingsMenu);
    }

    // Zenject will call IDisposable.Dispose for any menu bindings when the menu scene unloads.
    public void Dispose()
    {
        bsmlSettings.RemoveSettingsMenu(settingsMenu);
    }
}
