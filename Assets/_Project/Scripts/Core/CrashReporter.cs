using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Yahtzee.Core
{
    /// <summary>
    /// Soft-launch crash sink: local log + POST /api/crash-reports.
    /// Production: also wire Firebase Crashlytics / Sentry in this class (same HandleLog hooks).
    /// </summary>
    public sealed class CrashReporter : MonoBehaviour
    {
        private static CrashReporter _instance;
        private string _logPath;
        private float _lastUploadTime = -30f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            var host = new GameObject("CrashReporter");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<CrashReporter>();
        }

        /// <summary>Hook for Firebase Crashlytics / Sentry when those SDKs are added.</summary>
        public static void RecordBreadcrumb(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            Debug.Log("[YahtzeeCrash] " + message);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _logPath = Path.Combine(Application.persistentDataPath, "crash-log.txt");
            Application.logMessageReceived += HandleLog;
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandled;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= HandleLog;
            AppDomain.CurrentDomain.UnhandledException -= HandleUnhandled;
            if (_instance == this)
                _instance = null;
        }

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error)
                return;

            Append(condition, stackTrace);
            if (type == LogType.Exception)
                TryUpload(condition, stackTrace);
        }

        private void HandleUnhandled(object sender, UnhandledExceptionEventArgs args)
        {
            var text = args.ExceptionObject != null ? args.ExceptionObject.ToString() : "Unhandled";
            Append(text, string.Empty);
            TryUpload(text, string.Empty);
        }

        private void Append(string condition, string stackTrace)
        {
            try
            {
                var stamp = DateTime.UtcNow.ToString("o");
                File.AppendAllText(_logPath, stamp + "\n" + condition + "\n" + stackTrace + "\n---\n");
            }
            catch (Exception)
            {
            }
        }

        private void TryUpload(string condition, string stackTrace)
        {
            if (Time.unscaledTime - _lastUploadTime < 15f)
                return;

            _lastUploadTime = Time.unscaledTime;
            StartCoroutine(Upload(condition, stackTrace));
        }

        private IEnumerator Upload(string condition, string stackTrace)
        {
            string url;
            try
            {
                url = YahtzeeConfig.Current.ApiBaseUrl + YahtzeeConfig.Current.CrashReportPath;
            }
            catch (Exception)
            {
                yield break;
            }

            var payload = new StringBuilder(512);
            payload.Append('{');
            payload.Append("\"platform\":").Append(JsonString(Application.platform.ToString())).Append(',');
            payload.Append("\"version\":").Append(JsonString(Application.version)).Append(',');
            payload.Append("\"clientVersion\":").Append(JsonString(Network.ClientGate.ClientVersion)).Append(',');
            payload.Append("\"deviceModel\":").Append(JsonString(SystemInfo.deviceModel)).Append(',');
            payload.Append("\"message\":").Append(JsonString(condition ?? string.Empty)).Append(',');
            payload.Append("\"stack\":").Append(JsonString(Truncate(stackTrace, 4000)));
            payload.Append('}');

            var token = PlayerPrefs.GetString(YahtzeeConfig.Current.TokenPrefKey, string.Empty);
            if (string.IsNullOrEmpty(token))
                yield break;

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                var body = Encoding.UTF8.GetBytes(payload.ToString());
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + token);
                request.SetRequestHeader("X-Client-Version", Network.ClientGate.ClientVersion);
                request.timeout = 8;
                yield return request.SendWebRequest();
            }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? string.Empty;
            return value.Substring(0, max);
        }

        private static string JsonString(string value)
        {
            if (value == null)
                return "\"\"";

            var escaped = value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
            return "\"" + escaped + "\"";
        }
    }
}
