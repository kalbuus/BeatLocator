using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BeatLocator.EvaluationManagers;
using IPA.Loader;

namespace BeatLocator.Integrations;

/// <summary>
/// Bridges BeatLocator to BetterSongSearch's internal downloader.
/// </summary>
internal static class BetterSongSearchDownloadManager
{
    private const string BetterSongSearchPluginId = "BetterSongSearch";
    private const string SongCorePluginId = "SongCore";
    private const string DownloaderTypeName = "BetterSongSearch.Util.SongDownloader";
    private const string DownloadEntryTypeName =
        "BetterSongSearch.UI.DownloadHistoryView+DownloadHistoryEntry";
    private const int SongCoreRefreshTimeoutMilliseconds = 15000;

    public static async Task<bool> DownloadMapAsync(
        RecommendationMap map,
        CancellationToken cancellationToken)
    {
        var hash = GetMapHash(map);
        if (IsMapHashInstalled(hash))
        {
            return false;
        }

        var betterSongSearch = PluginManager.GetPluginFromId(BetterSongSearchPluginId)
            ?? throw new InvalidOperationException("BetterSongSearch is not installed.");
        var assembly = betterSongSearch.Assembly;
        var entryType = assembly.GetType(DownloadEntryTypeName)
            ?? throw new MissingMemberException(DownloadEntryTypeName);
        var downloaderType = assembly.GetType(DownloaderTypeName)
            ?? throw new MissingMemberException(DownloaderTypeName);
        var downloadMethod = downloaderType.GetMethod(
            "BeatmapDownload",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { entryType, typeof(CancellationToken), typeof(Action<float>) },
            null)
            ?? throw new MissingMethodException(DownloaderTypeName, "BeatmapDownload");

        // Its public constructor needs BetterSongSearch's private cached song model,
        // which is not initialized unless its own screen has been opened. The downloader
        // itself only reads these four fields, so create the entry without that constructor.
        var entry = FormatterServices.GetUninitializedObject(entryType);
        SetField(entryType, entry, "songName", map.Name ?? "Unknown song");
        SetField(entryType, entry, "levelAuthorName", map.Mapper ?? "Unknown mapper");
        SetField(entryType, entry, "key", map.Id ?? hash.Substring(0, 8));
        SetField(entryType, entry, "hash", hash);
        SetField(entryType, entry, "downloadProgress", 1f);

        Task downloadTask;
        try
        {
            downloadTask = downloadMethod.Invoke(
                null,
                new object[] { entry, cancellationToken, new Action<float>(_ => { }) }) as Task
                ?? throw new InvalidOperationException(
                    "BetterSongSearch returned an unexpected download result.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        await downloadTask;
        await RefreshSongCoreAsync(cancellationToken);
        return true;
    }

    internal static string GetMapHash(RecommendationMap map)
    {
        if (map.Hash is string hash && IsSha1Hash(hash))
        {
            return hash.ToLowerInvariant();
        }

        if (map.DownloadUrl is string downloadUrl &&
            !string.IsNullOrWhiteSpace(downloadUrl))
        {
            return ExtractMapHash(downloadUrl);
        }

        throw new InvalidDataException(
            "The ranking service did not provide a valid map SHA-1 hash or download URL.");
    }

    internal static bool IsMapInstalled(RecommendationMap map)
    {
        return IsMapHashInstalled(GetMapHash(map));
    }

    private static string ExtractMapHash(string downloadUrl)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidDataException($"Invalid map download URL: {downloadUrl}");
        }

        var fileName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        if (!IsSha1Hash(fileName))
        {
            throw new InvalidDataException(
                $"Could not find a BeatSaver SHA-1 hash in the map download URL: {downloadUrl}");
        }

        return fileName.ToLowerInvariant();
    }

    private static bool IsSha1Hash(string value)
    {
        if (value.Length != 40) return false;

        foreach (var character in value)
        {
            var isHexCharacter = character >= '0' && character <= '9' ||
                                 character >= 'a' && character <= 'f' ||
                                 character >= 'A' && character <= 'F';
            if (!isHexCharacter) return false;
        }

        return true;
    }

    private static void SetField(Type type, object instance, string fieldName, object value)
    {
        var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingFieldException(type.FullName, fieldName);
        field.SetValue(instance, value);
    }

    private static bool IsMapHashInstalled(string hash)
    {
        var songCore = PluginManager.GetPluginFromId(SongCorePluginId)
            ?? throw new InvalidOperationException("SongCore is not installed.");
        var collectionsType = songCore.Assembly.GetType("SongCore.Collections")
            ?? throw new MissingMemberException("SongCore.Collections");
        var checkMethod = collectionsType.GetMethod(
            "songWithHashPresent",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException("SongCore.Collections", "songWithHashPresent");

        return checkMethod.Invoke(null, new object[] { hash }) is true;
    }

    private static async Task RefreshSongCoreAsync(CancellationToken cancellationToken)
    {
        var completionSource = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLevelPacksRefreshed()
        {
            completionSource.TrySetResult(true);
        }

        SongCore.Loader.OnLevelPacksRefreshed += OnLevelPacksRefreshed;
        try
        {
            var loader = SongCore.Loader.Instance
                ?? throw new InvalidOperationException("SongCore loader is not initialized.");
            Plugin.Log.Info("Waiting for SongCore level packs after the roulette download.");
            loader.RefreshSongs(false);

            using var timeoutSource =
                new CancellationTokenSource(SongCoreRefreshTimeoutMilliseconds);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);
            using var registration = linkedSource.Token.Register(
                () => completionSource.TrySetCanceled());

            try
            {
                await completionSource.Task;
            }
            catch (OperationCanceledException) when (
                timeoutSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "SongCore did not finish refreshing level packs within 15 seconds.");
            }

            Plugin.Log.Info("SongCore level packs are ready after the roulette download.");
        }
        finally
        {
            SongCore.Loader.OnLevelPacksRefreshed -= OnLevelPacksRefreshed;
        }
    }
}
