using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Drawing.Imaging;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace StellarModManager.Services;


public enum GameInstallSource { Steam }
public record GameInstallationInfo(DirectoryInfo Directory, AvaloniaBitmap? Icon, GameInstallSource Source);

public static class GameLocatorService
{
    public const string ExecutableName = "StellarDrive.exe";


    public static IReadOnlyList<GameInstallationInfo> Locate()
    {
        if (!OperatingSystem.IsWindows()) return [];

        try
        {
            var installs = SteamInfoService.GetSteamGames()
                .Where(d => d.Name.Contains("StellarDrive"))
                .Select(d => CollectInstallInfo(d, GameInstallSource.Steam))
                .OfType<GameInstallationInfo>();

            return [.. installs];
        }
        catch
        {
            return [];
        }
    }


    private static GameInstallationInfo? CollectInstallInfo(DirectoryInfo directory, GameInstallSource source)
    {
        var executablePath = Path.Combine(directory.FullName, ExecutableName);
        if (!Path.Exists(executablePath)) return null;

        AvaloniaBitmap? icon = null;
        if (OperatingSystem.IsWindowsVersionAtLeast(major: 6, minor: 1))
        {
            using var fileIcon = Icon.ExtractAssociatedIcon(executablePath);
            using var systemBitmap = fileIcon?.ToBitmap();

            // https://github.com/AvaloniaUI/Avalonia/discussions/5908
            var data = systemBitmap?.LockBits(new Rectangle(0, 0, systemBitmap.Width, systemBitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            if (data is not null)
            {
                icon = new AvaloniaBitmap
                (
                    format: Avalonia.Platform.PixelFormat.Bgra8888,
                    alphaFormat: Avalonia.Platform.AlphaFormat.Premul,
                    data: data.Scan0,
                    size: new Avalonia.PixelSize(data.Width, data.Height),
                    dpi: new Avalonia.Vector(96, 96),
                    stride: data.Stride
                );
                systemBitmap?.UnlockBits(data);
            }
        }

        return new(directory, icon, source);
    }
}
