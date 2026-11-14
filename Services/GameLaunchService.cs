using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

namespace StellarModManager.Services;


public static partial class GameLaunchService
{
    private static readonly TimeSpan LaunchThroughSteamTimeout = TimeSpan.FromSeconds(10);


    public async static Task<Process?> Launch(FileInfo executable)
    {
        return await LaunchThroughSteam(executable) ?? LaunchDirectly(executable);
    }

    public static Process? DetectExisting(FileInfo executable)
    {
        Process? result = null;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!process.HasExited && process.MainModule?.FileName == executable.FullName)
                {
                    result = process;
                    continue;
                }
            }
            catch (Exception) {}

            process.Dispose();
        }

        return result;
    }


    private static async Task<Process?> LaunchThroughSteam(FileInfo executable)
    {
        Console.WriteLine($"Attempting to launch through Steam");

        if (!OperatingSystem.IsWindows()) return null;


        if (executable.Directory is null)
        {
            Console.WriteLine($"Failed to launch through Steam (directory is null)");
            return null;
        }

        Process? process = null;
        try
        {
            var appId = SteamInfoService.GetAppId(executable.Directory);
            if (appId is null)
            {
                Console.WriteLine($"Failed to launch through Steam (couldn't determine appid)");
                return null;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = $"steam://rungameid/{appId}",
                UseShellExecute = true
            });


            var processName = Path.GetFileNameWithoutExtension(executable.Name);
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < LaunchThroughSteamTimeout)
            {
                process = DetectExisting(executable);

                if (process is not null)
                {
                    Console.WriteLine($"Successfully launched through Steam");
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(0.1));
            }
            
            if (process is null) Console.WriteLine($"Failed to launch through Steam (timeout, couldn't find started process)");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to launch through Steam: {e.Message}");
        }
        
        return process;
    }

    private static Process? LaunchDirectly(FileInfo executable) // ig this would work on Linux?
    {
        Console.WriteLine($"Attempting to launch directly");

        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = executable.Name,
                WorkingDirectory = executable.DirectoryName,
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to launch directly: {e.Message}");
        }

        if (process is not null) Console.WriteLine($"Successfully launched directly (direct launching will cause issues with Steamworks)");
    
        return process;
    }
}
