using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BeatLocator.WebUtils;
using IPA.Loader;

namespace BeatLocator.Integrations;

/// <summary>
/// Bridges BeatLocator to BetterSongSearch's internal downloader.
/// BetterSongSearch 0.8.x does not expose a public download API.
/// </summary>
internal static class BetterSongSearchDownloadManager
{
    private const string BetterSongSearchPluginId = "BetterSongSearch";
    private const string SongCorePluginId = "SongCore";
    private const string DownloaderTypeName = "BetterSongSearch.Util.SongDownloader";
    private const string DownloadEntryTypeName =
        "BetterSongSearch.UI.DownloadHistoryView+DownloadHistoryEntry";

    public static async Task<bool> DownloadMapAsync(
        BLWebUtil.MapEntry map,
        CancellationToken cancellationToken)
    {
        var hash = GetMapHash(map);
        if (IsMapInstalled(hash))
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
        RefreshSongCore();
        return true;
    }

    internal static string GetMapHash(BLWebUtil.MapEntry map)
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
            "BeatLeader did not provide a valid map SHA-1 hash or download URL.");
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

    private static bool IsMapInstalled(string hash)
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

    private static void RefreshSongCore()
    {
        var songCore = PluginManager.GetPluginFromId(SongCorePluginId)
            ?? throw new InvalidOperationException("SongCore is not installed.");
        var loaderType = songCore.Assembly.GetType("SongCore.Loader")
            ?? throw new MissingMemberException("SongCore.Loader");
        var instanceField = loaderType.GetField(
            "Instance",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingFieldException("SongCore.Loader", "Instance");
        var loader = instanceField.GetValue(null)
            ?? throw new InvalidOperationException("SongCore loader is not initialized.");
        var refreshMethod = loaderType.GetMethod(
            "RefreshSongs",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(bool) },
            null)
            ?? throw new MissingMethodException("SongCore.Loader", "RefreshSongs");

        refreshMethod.Invoke(loader, new object[] { false });
    }
}
