using Avalonia.Threading;
using StellarModManager.Models;
using StellarModManager.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    private readonly ReleaseFingerprintIndex releaseFingerprints = new(
        Path.Combine(AppContext.BaseDirectory, "Data", "release-fingerprints.json"));

    private IReadOnlySet<string> ownedProfileFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> changedExternalFiles = new(StringComparer.Ordinal);
    private FileSystemWatcher? externalWatcher;
    private DispatcherTimer? externalScanTimer;
    private CancellationTokenSource? externalScan;
    private bool invalidateExternalCache;
    private bool externalDiscoveryStopped;
    private bool retryExternalScan;

    private void WatchExternalMods(Guid profileId)
    {
        externalScan?.Cancel();
        externalWatcher?.Dispose();
        externalWatcher = null;
        changedExternalFiles.Clear();
        invalidateExternalCache = false;

        if (externalDiscoveryStopped)
            return;

        if (externalScanTimer == null)
        {
            externalScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            externalScanTimer.Tick += (_, _) =>
            {
                if (IsLibraryPage && externalScan == null)
                    QueueExternalScan();
            };
            externalScanTimer.Start();
        }

        try
        {
            var watcher = new FileSystemWatcher(profileStorage.GetModsDirectory(profileId))
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            externalWatcher = watcher;
            watcher.Created += (_, args) => ExternalFilesChanged(watcher, args.FullPath);
            watcher.Deleted += (_, args) => ExternalFilesChanged(watcher, args.FullPath);
            watcher.Renamed += (_, args) => ExternalFilesChanged(watcher, args.FullPath, args.OldFullPath);
            watcher.Changed += (_, args) => ExternalFilesChanged(watcher, args.FullPath);
            watcher.Error += (_, _) => ExternalFilesChanged(watcher);
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            externalWatcher?.Dispose();
            externalWatcher = null;
        }
    }

    private void ExternalFilesChanged(FileSystemWatcher watcher, params string[] paths) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(externalWatcher, watcher))
                return;

            if (paths.Length == 0)
                invalidateExternalCache = true;
            foreach (var path in paths)
                changedExternalFiles.Add(ExternalModDiscovery.RelativePath(watcher.Path, path));

            QueueExternalScan();
        });

    private void QueueExternalScan()
    {
        if (externalDiscoveryStopped || ActiveProfile is not { } profile)
            return;

        externalScan?.Cancel();
        var source = new CancellationTokenSource();
        externalScan = source;
        _ = ScanExternalAsync(profile.Id, source);
    }

    internal void StopExternalDiscovery()
    {
        externalDiscoveryStopped = true;
        externalScanTimer?.Stop();
        externalScan?.Cancel();
        externalWatcher?.Dispose();
        externalWatcher = null;
    }

    private async Task ScanExternalAsync(Guid profileId, CancellationTokenSource source)
    {
        var cancellation = source.Token;
        try
        {
            await Task.Delay(250, cancellation);
            var ownedFiles = ownedProfileFiles;
            var changed = changedExternalFiles.ToHashSet(StringComparer.Ordinal);
            var invalidate = invalidateExternalCache;
            var files = await Task.Run(() => profileStorage.ScanExternal(profileId,
                ownedFiles, changed, invalidate, cancellation), cancellation);

            cancellation.ThrowIfCancellationRequested();
            if (ActiveProfile?.Id != profileId)
                return;
            if (IsProfileBusy)
            {
                retryExternalScan = true;
                return;
            }

            changedExternalFiles.ExceptWith(changed);
            invalidateExternalCache = false;
            ApplyExternalRows(files);

            if (files.Count == 0)
                return;

            var catalog = OnlineMods.Select(mod =>
                new CatalogModRelease(mod.Id, mod.Name, mod.RepoName, mod.DownloadUrl)).ToList();
            var matches = await Task.Run(() => releaseFingerprints.MatchAsync(files,
                catalog, repositoryService, installerService, cancellation), cancellation);

            cancellation.ThrowIfCancellationRequested();
            if (matches.Count == 0 || ActiveProfile?.Id != profileId)
                return;
            if (IsProfileBusy)
            {
                retryExternalScan = true;
                return;
            }

            IsProfileBusy = true;
            try
            {
                var game = GamePath;
                var snapshot = await Task.Run(() => profileStorage.RegisterExternal(profileId, game, matches, cancellation));
                if (snapshot != null && !externalDiscoveryStopped)
                    ApplyProfileSnapshot(snapshot);
            }
            finally
            {
                IsProfileBusy = false;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.WriteLine($"External mods refresh failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(externalScan, source))
                externalScan = null;
            source.Dispose();
        }
    }

    private void ApplyExternalRows(List<ExternalDll> files)
    {
        var selectedId = LibrarySelection?.Id;
        var previous = InstalledMods.Where(mod => mod.IsExternal).ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        foreach (var file in files)
        {
            for (var index = 0; index < file.Melons.Count; index++)
            {
                var id = $"external:{index}:{file.Path}";
                var metadata = file.Melons[index];
                if (previous.Remove(id, out var existing))
                {
                    if (existing.ExternalFile is { } previousFile && previousFile.Hash == file.Hash && previousFile.LinkTarget == file.LinkTarget)
                        continue;
                    InstalledMods.Remove(existing);
                }

                InstalledMods.Add(new InstalledModInfo
                {
                    Id = id,
                    Name = metadata.Name,
                    Author = metadata.Author,
                    Version = metadata.Version,
                    ExternalFile = file
                });
            }
        }

        foreach (var stale in previous.Values)
            InstalledMods.Remove(stale);

        if (LibrarySelection == null && selectedId != null)
            LibrarySelection = InstalledMods.FirstOrDefault(mod => mod.Id == selectedId);
    }
}
