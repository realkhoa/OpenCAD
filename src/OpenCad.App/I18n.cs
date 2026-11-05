namespace OpenCad.App;

/// <summary>
/// @brief Supported UI languages for internationalization.
/// </summary>
public enum AppLanguage
{
    /// <summary>Vietnamese language (default for local workflows).</summary>
    Vietnamese,

    /// <summary>English language (standard AutoCAD terminology).</summary>
    English,
}

/// <summary>
/// @brief Centralized internationalization (i18n) service for the OpenCAD desktop application.
/// @details Provides dynamic runtime language switching, string dictionary lookups with fallback,
/// and notification events for UI controls to re-bind their textual content on the fly.
/// </summary>
public static partial class I18n
{
    private static AppLanguage _currentLanguage = AppLanguage.Vietnamese;

    /// <summary>
    /// @brief Event raised whenever the active UI language is changed.
    /// </summary>
    public static event Action? LanguageChanged;

    /// <summary>
    /// @brief Gets or sets the current application UI language.
    /// </summary>
    public static AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage == value) return;
            _currentLanguage = value;
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>
    /// @brief Gets the two-letter ISO language code for the current language.
    /// @return "vi" for Vietnamese, "en" for English.
    /// </summary>
    public static string CurrentLanguageCode =>
        _currentLanguage == AppLanguage.Vietnamese ? "vi" : "en";

    /// <summary>
    /// @brief Sets the active language by code string (e.g. "en", "vi", "en-us", "vi-vn").
    /// @param code Two-letter or culture language code.
    /// @return True if the language was recognized and set; false otherwise.
    /// </summary>
    public static bool SetLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.StartsWith("en"))
        {
            CurrentLanguage = AppLanguage.English;
            return true;
        }

        if (normalized.StartsWith("vi"))
        {
            CurrentLanguage = AppLanguage.Vietnamese;
            return true;
        }

        return false;
    }

    /// <summary>
    /// @brief Translates a localization key into the active language.
    /// @param key Unique dot-separated translation key.
    /// @param args Optional formatting parameters.
    /// @return Localized string, or the key itself if no translation is found.
    /// </summary>
    public static string T(string key, params object[] args)
    {
        var dict = _currentLanguage == AppLanguage.Vietnamese ? VietnameseStrings : EnglishStrings;

        if (!dict.TryGetValue(key, out var text))
        {
            // Fallback to English dictionary before giving up
            if (!EnglishStrings.TryGetValue(key, out text))
            {
                text = key;
            }
        }

        if (args.Length > 0 && text.Contains('{'))
        {
            try
            {
                return string.Format(text, args);
            }
            catch
            {
                return text;
            }
        }

        return text;
    }
}
