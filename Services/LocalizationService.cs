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

    private readonly Dictionary<string, string> _fallbackTranslations = [];
    private readonly Dictionary<string, string> _languageDisplayNames = [];
    private Dictionary<string, string> _currentTranslations = [];

    private const string FallbackLanguage = "en";
    public string CurrentLanguage { get; private set; } = FallbackLanguage;

    public event EventHandler? LanguageChanged;

    private LocalizationService()
    {
        _fallbackTranslations = LoadLanguage(FallbackLanguage);
        _currentTranslations = _fallbackTranslations;
    }

    public string this[string key]
    {
        get
        {
            if (_currentTranslations.TryGetValue(key, out var value))
                return value;

            if (_fallbackTranslations.TryGetValue(key, out var fallback))
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
        var previousLanguage = CurrentLanguage;

        var language = LoadLanguage(desiredLanguage);
        bool languageExists = language.Count != 0;

        if (languageExists)
        {
            CurrentLanguage = usedLanguage = desiredLanguage;
            _currentTranslations = language;
        }
        else
        {
            CurrentLanguage = usedLanguage = FallbackLanguage;
            _currentTranslations = _fallbackTranslations;
        }

        bool changed = previousLanguage != CurrentLanguage;

        if (changed)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

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
            _languageDisplayNames[code] = translations.TryGetValue("LanguageName", out var name) ? name : code;

            yield return code;
        }
    }

    public string GetLanguageDisplayName(string code)
    {
        return _languageDisplayNames.TryGetValue(code, out var name) ? name : code;
    }

    private static Dictionary<string, string> LoadLanguage(string languageCode)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data/Localization", $"{languageCode}.json");

        if (!File.Exists(path))
            return [];

        try
        {
            var json = File.ReadAllText(path);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}