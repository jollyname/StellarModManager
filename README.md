# Stellar Mod Manager

A mod manager for the game **StellarDrive**.

Stellar Mod Manager makes it easier to install, update, and manage mods for StellarDrive. It can automatically install MelonLoader if needed and provides a simple interface for managing your mods.

## Features

- Automatic MelonLoader installation
- Browse available mods
- Install and manage installed mods
- Simple setup process
- Built for StellarDrive

## Getting Started

1. Download the latest release.
2. Launch Stellar Mod Manager.
3. Select your StellarDrive installation folder when prompted.
4. If MelonLoader is not installed, the manager can install it automatically.
5. Use Browse, Library and Updates in the sidebar to manage your mods.

## Submitting Your Mods

Want to make your mod available to other StellarDrive players? Your mod has to be **open source** and hosted in a public GitHub repository, with an open source license file (for example MIT) in the root.

1. Create your `mod.json` with the [Mod Metadata Generator](https://jollyname.dev/create/) and save it in the root of your repository, together with the images it lists.
2. Publish a GitHub release tagged `v` plus your version (for version `1.0.0`, the tag is `v1.0.0`). Attach a zip named exactly like your repository (`<repository name>.zip`) containing `mod.json` and a `Mods` folder with your built `.dll`.
3. Visit the [Mod Submission page](https://jollyname.dev/submit/) and submit your repository. Submissions are checked and reviewed before they appear in the manager.

## Download

You can find releases in the GitHub Releases section.

## Windows SmartScreen Warning

Windows may display a warning because the application is not code-signed.

This project is open source, and the complete source code is available in this repository. You can inspect the code or build the application yourself if you prefer.

## Reporting Issues

If you find a bug or have a suggestion, open an issue on GitHub.

## License

This project is licensed under the MIT License.
