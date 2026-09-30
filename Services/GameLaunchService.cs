using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace StellarModManager.Services;

public class GameLaunchService
{
    private static readonly string[] IgnoredExecutables =
    {
        "UnityCrashHandler32.exe",
        "UnityCrashHandler64.exe",
        "CrashHandler.exe",
        "CrashReportClient.exe"
    };

    public string? FindGameExecutable(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            return null;

        string[] candidates;

        try
        {
            candidates = Directory.GetFiles(gamePath, "*.exe", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return null;
        }

        string folderName = new DirectoryInfo(gamePath).Name;

        string? best = null;
        long bestSize = -1;
        int bestNamed = 0;

        foreach (string path in candidates)
        {
            string fileName = Path.GetFileName(path);

            if (IgnoredExecutables.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                continue;

            int named = Path.GetFileNameWithoutExtension(fileName)
                .Contains(folderName, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            long size;

            try
            {
                size = new FileInfo(path).Length;
            }
            catch
            {
                continue;
            }

            if (named > bestNamed || (named == bestNamed && size > bestSize))
            {
                best = path;
                bestSize = size;
                bestNamed = named;
            }
        }

        return best;
    }

    public Process? Launch(string gamePath)
    {
        string? exe = FindGameExecutable(gamePath);

        if (exe is null)
            return null;

        return Process.Start(new ProcessStartInfo(exe)
        {
            WorkingDirectory = gamePath,
            UseShellExecute = true
        });
    }
}
