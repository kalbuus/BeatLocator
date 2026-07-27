using Zenject;
using BeatLocator.Menu;

namespace BeatLocator.Installers;

// This particular installer relates to bindings that are used in the main menu. It is related to the
// MainSettingsMenuViewControllersInstaller installer in the base game, and its InstallBindings is called when the
// game first loads into the main menu, and after settings are applied, which causes an internal reload of the game.

internal class MenuInstaller : Installer
{
    public override void InstallBindings()
    {
        // This will create a single instance of the type SettingsMenuManager and implement its interfaces
        // The BindInterfacesTo shortcut is useful since you don't want to write out and remember every base type:
        // Container.Bind(typeof(IInitializable, typeof(IDisposable)).To<SettingsMenuManager>().AsSingle();
        // Is the same as:
        Container.BindInterfacesTo<SettingsMenuManager>().AsSingle();
        Container.BindInterfacesTo<MenuManager>().AsSingle();

        // This will create a single instance of SettingsMenu, and lets it be injected into other types
        Container.Bind<SettingsMenu>().AsSingle();

        Container.Bind<BeatLocatorFlowCoordinator>()
            .FromNewComponentOnNewGameObject()
            .AsSingle();

        Container.Bind<SelectViewController>()
            .FromNewComponentAsViewController()
            .AsSingle();

        Container.Bind<BeatLeaderSelect>()
            .FromNewComponentAsViewController()
            .AsSingle();

        Container.Bind<RouletteAnimationViewController>()
            .FromNewComponentAsViewController()
            .AsSingle();
    }
}
