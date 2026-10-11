#if BEATLOCATOR_DEBUG_UI
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using Zenject;

namespace BeatLocator.Menu;

internal enum DebugUiScreen { Pp, NonPb, UploadFailed, Failed, Quit, LoadingPp, LoadingSong, Roulette }

internal sealed class DebugUiViewController : BSMLAutomaticViewController
{
    private BeatLocatorFlowCoordinator _flowCoordinator = null!;
    [Inject]
    private void Construct(BeatLocatorFlowCoordinator flowCoordinator) => _flowCoordinator = flowCoordinator;

    [UIAction("pp")] private void Pp() => _flowCoordinator.ShowDebugScreen(DebugUiScreen.Pp);
    [UIAction("non-pb")] private void NonPb() => _flowCoordinator.ShowDebugScreen(DebugUiScreen.NonPb);
    [UIAction("upload-failed")] private void UploadFailed() => _flowCoordinator.ShowDebugScreen(DebugUiScreen.UploadFailed);
    [UIAction("failed")] private void Failed() => _flowCoordinator.ShowDebugScreen(DebugUiScreen.Failed);
    [UIAction("quit")] private void Quit() => _flowCoordinator.ShowDebugScreen(DebugUiScreen.Quit);
    [UIAction("loading-pp")] private void LoadingPp() => _flowCoordinator.ShowDebugScreen(DebugUiScreen.LoadingPp);
    [UIAction("loading-song")] private void LoadingSong() => _flowCoordinator.ShowDebugScreen(DebugUiScreen.LoadingSong);
    [UIAction("roulette")] private void Roulette() => _flowCoordinator.ShowDebugScreen(DebugUiScreen.Roulette);
    [UIAction("back")] private void Back() => _flowCoordinator.ShowSelect();
}
#endif
