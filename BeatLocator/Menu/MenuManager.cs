using BeatSaberMarkupLanguage.MenuButtons;
using Zenject;

namespace BeatLocator.Menu;

internal class MenuManager : IInitializable
{
    public MenuManager(BeatLocatorFlowCoordinator flowCoordinator)
    {
        _flowCoordinator = flowCoordinator;
    }
    
    public void Initialize()
    {
        MenuButtons.Instance.RegisterButton(
            new MenuButton(
                "BeatLocator",
                "Find the songs YOU need to play",
                OpenMenu,
                true));

    }
    
    private readonly BeatLocatorFlowCoordinator _flowCoordinator;

    private void OpenMenu()
    {
        _flowCoordinator.Present();
    }
}
