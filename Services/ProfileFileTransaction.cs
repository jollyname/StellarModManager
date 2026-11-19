using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StellarModManager.Services;

internal sealed class ProfileFileTransaction : IDisposable
{
    private enum ChangeKind { Move, Link, CreateDirectory }
    private sealed record Change(ChangeKind Kind, string Path, string Destination, bool Directory);

    private sealed class Journal
    {
        public bool Committed { get; set; }
        public List<string> Workspaces { get; set; } = [];
        public List<Change> Changes { get; set; } = [];
    }

    private readonly string journalPath;
    private readonly Journal journal;
    private bool completed;

    public ProfileFileTransaction(string journalPath)
    {
        this.journalPath = journalPath;
        Recover(journalPath);
        journal = new Journal();
        Save();
    }

    public static void Recover(string journalPath)
    {
        if (!File.Exists(journalPath))
            return;

        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath))
            ?? throw new IOException($"Invalid profile transaction: {journalPath}");

        if (!journal.Committed)
        {
            while (journal.Changes.Count > 0)
            {
                var change = journal.Changes[^1];
                switch (change.Kind)
                {
                    case ChangeKind.Link when IsLinkTo(change.Path, change.Destination, change.Directory):
                        Delete(change.Path);
                        break;
                    case ChangeKind.Move when Exists(change.Destination):
                        if (Exists(change.Path))
                            throw new IOException($"Could not restore '{change.Path}' because it is in use. Its backup is at '{change.Destination}'.");

                        Rename(change.Destination, change.Path, change.Directory);
                        break;
                    case ChangeKind.CreateDirectory when IsRegularDirectory(change.Path)
                        && !Directory.EnumerateFileSystemEntries(change.Path).Any():
                        Directory.Delete(change.Path);
                        break;
                }

                journal.Changes.RemoveAt(journal.Changes.Count - 1);
                WriteJson(journalPath, journal);
            }

            journal.Committed = true;
            WriteJson(journalPath, journal);
        }

        foreach (var workspace in journal.Workspaces)
            Delete(workspace);

        File.Delete(journalPath);
    }

    public string ScratchPath(string root)
    {
        var workspace = journal.Workspaces.FirstOrDefault(path =>
            string.Equals(Path.GetDirectoryName(path), root, PathComparison));
        if (workspace == null)
        {
            workspace = Path.Combine(root, $".smm-{Guid.NewGuid():N}");
            journal.Workspaces.Add(workspace);
            Save();
            Directory.CreateDirectory(workspace);
        }

        return Path.Combine(workspace, Guid.NewGuid().ToString("N"));
    }

    public void Move(string path, string destination, bool directory = false)
    {
        if (!Exists(path))
            throw new IOException($"Missing file or directory: {path}");
        if (Exists(destination))
            throw new IOException($"Path is already in use: {destination}");

        journal.Changes.Add(new Change(ChangeKind.Move, path, destination, directory));
        Save();
        Rename(path, destination, directory);
    }

    public void MoveAside(string root, string path, bool directory = false) =>
        Move(path, ScratchPath(root), directory);

    public void CreateDirectory(string path)
    {
        if (Exists(path))
        {
            if (!IsRegularDirectory(path))
                throw new IOException($"Expected a regular directory at '{path}'.");
            return;
        }

        CreateDirectory(Path.GetDirectoryName(path)!);
        journal.Changes.Add(new Change(ChangeKind.CreateDirectory, path, "", Directory: true));
        Save();
        Directory.CreateDirectory(path);
    }

    public void CreateLink(string path, string target, bool directory = false)
    {
        if (Exists(path))
            throw new IOException($"Path is already in use: {path}");

        journal.Changes.Add(new Change(ChangeKind.Link, path, target, directory));
        Save();
        try
        {
            if (directory)
                Directory.CreateSymbolicLink(path, target);
            else
                File.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            var guidance = OperatingSystem.IsWindows()
                ? " Check write permissions and enable Windows Developer Mode or run with permission to create symbolic links."
                : " Check write permissions and symbolic link support on the filesystem.";
            throw new IOException($"Could not link '{path}' to '{target}'.{guidance}", ex);
        }
    }

    public void ReplaceJson<T>(string path, T value)
    {
        var root = Path.GetDirectoryName(path)!;
        var staged = ScratchPath(root);
        WriteJson(staged, value);
        if (Exists(path))
            MoveAside(root, path);
        Move(staged, path);
    }

    public void Commit()
    {
        journal.Committed = true;
        Save();
        completed = true;
        try
        {
            Recover(journalPath);
        }
        catch (IOException){}
        catch (UnauthorizedAccessException){}
    }

    public void Dispose()
    {
        if (!completed)
            Recover(journalPath);
    }

    public static FileAttributes? Attributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public static bool Exists(string path) => Attributes(path).HasValue;

    public static bool IsRegularDirectory(string path) => Attributes(path) is { } attributes
        && attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.ReparsePoint);

    public static bool IsLinkTo(string path, string target, bool directory)
    {
        FileSystemInfo info = directory ? new DirectoryInfo(path) : new FileInfo(path);
        var parent = Path.GetDirectoryName(path)!;
        return info.LinkTarget is { } actual && string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual, parent)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(target, parent)),
            PathComparison);
    }

    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private void Save() => WriteJson(journalPath, journal);

    private static void WriteJson<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static void Rename(string path, string destination, bool directory)
    {
        if (directory)
            Directory.Move(path, destination);
        else
            File.Move(path, destination);
    }

    private static void Delete(string path)
    {
        if (Attributes(path) is not { } attributes)
            return;

        if (attributes.HasFlag(FileAttributes.Directory))
            Directory.Delete(path, recursive: !attributes.HasFlag(FileAttributes.ReparsePoint));
        else
            File.Delete(path);
    }
}
