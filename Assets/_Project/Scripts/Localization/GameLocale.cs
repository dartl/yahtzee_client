using System;
using UnityEngine;

namespace Yahtzee.Localization
{
    public static class GameLocale
    {
        public const string Russian = "ru";
        public const string English = "en";

        private const string PrefKey = "yahtzee.locale";

        private static string _current;

        public static event Action OnChanged;

        public static string Current
        {
            get
            {
                if (!string.IsNullOrEmpty(_current))
                    return _current;

                _current = ResolveInitial();
                return _current;
            }
        }

        public static void Set(string locale)
        {
            var normalized = Normalize(locale);
            if (string.Equals(_current, normalized, StringComparison.Ordinal))
                return;

            _current = normalized;
            PlayerPrefs.SetString(PrefKey, normalized);
            PlayerPrefs.Save();
            GameText.Reload();
            OnChanged?.Invoke();
        }

        public static string Normalize(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
                return Russian;

            locale = locale.Trim().ToLowerInvariant();
            if (locale.StartsWith("en", StringComparison.Ordinal))
                return English;

            return Russian;
        }

        private static string ResolveInitial()
        {
            if (PlayerPrefs.HasKey(PrefKey))
                return Normalize(PlayerPrefs.GetString(PrefKey));

            return Application.systemLanguage == SystemLanguage.Russian ? Russian : English;
        }
    }
}
