using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    private Process? GameProcess
    {
        get;
        set
        {
            if (field == value) return;

            if (field is not null)
            {
                field.EnableRaisingEvents = false;
                field.Exited -= OnExitGameProcess;

                field.Dispose();
            }

            if (value is not null)
            {
                value.EnableRaisingEvents = true;
                value.Exited += OnExitGameProcess;
            }

            field = value;
            IsGameRunning = field is not null;
        }
    }

    [ObservableProperty]
    private bool isGameRunning;


    private void OnExitGameProcess(object? sender, EventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (sender == GameProcess)
                GameProcess = null;
        });
    }


    [RelayCommand]
    private async Task ToggleGame()
    {
        if (IsGameRunning) StopGame();
        else await RunProfileOperation("StatusLaunchFailed", LaunchGame);
    }


    private async Task LaunchGame()
    {
        var game = FindGameExecutable();

        if (game is null)
        {
            ReportError("StatusGameNotFound");
            return;
        }

        var path = GamePath;
        ApplyProfileSnapshot(await Task.Run(() => profileStorage.Initialize(path)));
        GameProcess = await GameLaunchService.Launch(game);
    }

    private void StopGame()
    {
        try
        {
            GameProcess?.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            ReportError("StatusStopFailed", ex.Message);
        }
    }


    private FileInfo? FindGameExecutable()
    {
        var path = Path.Combine(GamePath, GameLocatorService.ExecutableName);
        return Path.Exists(path) ? new FileInfo(path) : null;
    }

    private void SearchGameProcessIfNull()
    {
        if (GameProcess is null && FindGameExecutable() is FileInfo game)
        {
            GameProcess = GameLaunchService.DetectExisting(game);
        }
    }
}