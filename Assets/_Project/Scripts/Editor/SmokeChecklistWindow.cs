#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Yahtzee.Core;
using Yahtzee.Network;
using Yahtzee.UI;

namespace Yahtzee.Editor
{
    public sealed class SmokeChecklistWindow : EditorWindow
    {
        private Vector2 _scroll;
        private readonly bool[] _done = new bool[28];

        [MenuItem("YADZY/Smoke Checklist")]
        private static void Open()
        {
            GetWindow<SmokeChecklistWindow>("YADZY Smoke");
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Ручной смоук перед soft launch", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Полный runbook: yadzee_api/docs/staging-runbook.md. Отмечай после прогона. Автопроверка — wiring.",
                MessageType.Info);

            DrawGroup("Онлайн", 0,
                "Quick match → матч → итоги",
                "Queues: disconnect → reconnect или forfeit W/L (без bot choice)",
                "Custom 1v1: wait reconnect; peer leave cancels",
                "Лобби 3–4: leave freeze без бота + reconnect overlay");
            DrawGroup("Офлайн", 4,
                "Бот beginner → полный матч → итоги",
                "Бот professional → полный матч",
                "Pass & Play 2 / 3 / 4",
                "Wi-Fi LAN host + client");
            DrawGroup("Мета", 8,
                "Магазин: монеты / ad-free / скины",
                "Кастомизация применяет скин на столе",
                "Настройки: ник, export, delete, bind device",
                "Privacy/Terms + version gate / feature flags");
            DrawGroup("Платформы", 12,
                "Bluetooth виден только на Android (store note)",
                "WebGL: нет Bluetooth/LAN если так задумано",
                "Первый запуск: экран подсказок",
                "Краш → /api/crash-reports (+ Crashlytics hook)");
            DrawGroup("Регресс UI", 16,
                "Онлайн/Офлайн меню: фон сукна, не стол",
                "Итоги матча: MMR без монет за игру");
            DrawGroup("Стол / кубики", 18,
                "Portrait: pit между таблицей и pedestal held",
                "Landscape: pit справа от таблицы",
                "Tablet/phone: рамки перестраиваются без дыр",
                "Held nests на постаменте, бросок только в pit");
            DrawGroup("Classic Yahtzee / турнир / метрики", 22,
                "Upper bonus 63→+35 offline+online одинаково",
                "Yahtzee joker +100 bonus; forced upper если открыт",
                "Live tournament queue only (нет solo submit score)",
                "GET /api/admin/metrics как is_admin показывает счётчики",
                "Maintenance + min client version gate",
                "staging-runbook.md пройден целиком");

            EditorGUILayout.Space(12);
            if (GUILayout.Button("Автопроверка wiring"))
                RunWiringCheck();

            EditorGUILayout.EndScrollView();
        }

        private void DrawGroup(string title, int start, params string[] items)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            for (var i = 0; i < items.Length; i++)
                _done[start + i] = EditorGUILayout.ToggleLeft(items[i], _done[start + i]);
        }

        private static void RunWiringCheck()
        {
            var config = Resources.Load<YahtzeeConfig>("YahtzeeConfig");
            if (config == null)
            {
                Debug.LogError("YahtzeeConfig missing in Resources.");
                return;
            }

            var issues = 0;
            if (string.IsNullOrWhiteSpace(config.ApiBaseUrl))
            {
                Debug.LogError("ApiBaseUrl empty.");
                issues++;
            }

            if (config.PrivacyPolicyUrl.Contains("yahtzee.local") && !Application.isEditor)
                Debug.LogWarning("Privacy URL still points to yahtzee.local — смени на прод перед стором.");

            if (Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include) == null)
                Debug.LogWarning("UIManager not in open scenes (OK if checking offline).");

            var board = Object.FindAnyObjectByType<GameBoardController>(FindObjectsInactive.Include);
            if (board != null)
            {
                if (!board.ValidatePlayLayout(out var boardMessage))
                {
                    Debug.LogWarning("GameBoard layout: " + boardMessage);
                    issues++;
                }
                else
                    Debug.Log("GameBoard layout: " + boardMessage);
            }

            Debug.Log(issues == 0
                ? "Wiring OK. Bluetooth.IsSupported=" + BluetoothNetworkManager.IsSupported
                    + " UseProduction=" + config.UseProductionEndpoints
                : "Wiring issues: " + issues);
        }
    }
}
#endif
