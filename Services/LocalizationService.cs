using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace StellarModManager.Services;

public class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private Dictionary<string, string> currentTranslations = new();
    private Dictionary<string, string> fallbackTranslations = new();
    private Dictionary<string, string> languageDisplayNames = new();

    private const string FallbackLanguage = "en";
    public string CurrentLanguage { get; private set; } = FallbackLanguage;

    private LocalizationService()
    {
        fallbackTranslations = LoadLanguage(FallbackLanguage);
        currentTranslations = fallbackTranslations;
    }

    public string this[string key]
    {
        get
        {
            if (currentTranslations.TryGetValue(key, out var value))
                return value;

            if (fallbackTranslations.TryGetValue(key, out var fallback))
                return fallback;

            return key;
        }
    }

    public string Format(string key, params object[] args)
    {
        return string.Format(this[key], args);
    }

    public bool TrySetLanguage(string desiredLanguage, out string usedLanguage)
    {
        var language = LoadLanguage(desiredLanguage);
        bool languageExists = language.Count != 0;

        if (languageExists)
        {
            CurrentLanguage = usedLanguage = desiredLanguage;
            currentTranslations = language;
        }
        else
        {
            CurrentLanguage = usedLanguage = FallbackLanguage;
            currentTranslations = fallbackTranslations;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        return languageExists;
    }

    public void SetLanguage(string languageCode)
    {
        TrySetLanguage(languageCode, out _);
    }

    public IEnumerable<string> GetAvailableLanguages()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Data/Localization");
        if (!Directory.Exists(folder))
            yield break;

        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(file);
            var translations = LoadLanguage(code);
            languageDisplayNames[code] = translations.TryGetValue("LanguageName", out var name) ? name : code;

            yield return code;
        }
    }

    public string GetLanguageDisplayName(string code)
    {
        return languageDisplayNames.TryGetValue(code, out var name) ? name : code;
    }

    private static Dictionary<string, string> LoadLanguage(string languageCode)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data/Localization", $"{languageCode}.json");

        if (!File.Exists(path))
            return new();

        try
        {
            var json = File.ReadAllText(path);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }
}