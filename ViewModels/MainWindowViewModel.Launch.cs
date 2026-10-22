using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Services;
using System;
using System.Diagnostics;
using System.IO;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    private Process? gameProcess;

    [ObservableProperty]
    private bool isGameRunning;

    [RelayCommand]
    private void ToggleGame()
    {
        if (IsGameRunning) StopGame();
        else LaunchGame();
    }

    private void LaunchGame()
    {
        var exePath = Path.Combine(GamePath, GameLocatorService.ExecutableName);

        if (!File.Exists(exePath))
        {
            ReportError("StatusGameNotFound");
            return;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = GamePath,
                UseShellExecute = true
            });

            if (process == null) return;

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (gameProcess == process)
                {
                    gameProcess = null;
                    IsGameRunning = false;
                }
                process.Dispose();
            });

            gameProcess = process;
            IsGameRunning = true;
        }
        catch (Exception ex)
        {
            ReportError("StatusLaunchFailed", ex.Message);
        }
    }

    private void StopGame()
    {
        try
        {
            gameProcess?.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            ReportError("StatusStopFailed", ex.Message);
        }
    }
}