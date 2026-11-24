using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Yahtzee.Network;

namespace Yahtzee.Localization
{
    public static class GameText
    {
        private static Dictionary<string, string> _table = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _loadedLocale;

        public static string Get(string key)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            if (_table.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;

            return key;
        }

        public static string Format(string key, params object[] args)
        {
            var template = Get(key);
            if (args == null || args.Length == 0)
                return template;

            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        public static string FromError(string code, string serverFallback = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.IsNullOrWhiteSpace(serverFallback) ? Get("errors.UNKNOWN_ERROR") : serverFallback;

            var key = "errors." + code.Trim();
            EnsureLoaded();
            if (_table.TryGetValue(key, out var localized) && !string.IsNullOrEmpty(localized))
                return localized;

            if (!string.IsNullOrWhiteSpace(serverFallback))
                return serverFallback;

            return Get("errors.UNKNOWN_ERROR");
        }

        public static string FromException(Exception exception)
        {
            if (exception is NetworkException network)
                return FromError(network.ErrorCode, network.Message);

            if (exception == null || string.IsNullOrWhiteSpace(exception.Message))
                return Get("errors.UNKNOWN_ERROR");

            return exception.Message;
        }

        public static void Reload()
        {
            _loadedLocale = null;
            _table = new Dictionary<string, string>(StringComparer.Ordinal);
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            var locale = GameLocale.Current;
            if (string.Equals(_loadedLocale, locale, StringComparison.Ordinal) && _table.Count > 0)
                return;

            _table = LoadLocale(locale);
            if (_table.Count == 0 && !string.Equals(locale, GameLocale.English, StringComparison.Ordinal))
                _table = LoadLocale(GameLocale.English);

            _loadedLocale = locale;
        }

        private static Dictionary<string, string> LoadLocale(string locale)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var asset = Resources.Load<TextAsset>("Localization/" + locale);
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
                return map;

            try
            {
                var root = JObject.Parse(asset.text);
                Flatten(root, string.Empty, map);
            }
            catch (JsonException exception)
            {
                Debug.LogWarning("Yahtzee localization parse failed (" + locale + "): " + exception.Message);
            }

            return map;
        }

        private static void Flatten(JToken token, string prefix, Dictionary<string, string> map)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    var next = string.IsNullOrEmpty(prefix) ? property.Name : prefix + "." + property.Name;
                    Flatten(property.Value, next, map);
                }

                return;
            }

            if (token is JValue value && value.Type == JTokenType.String)
                map[prefix] = value.ToString();
        }
    }
}
