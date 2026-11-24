using System.Collections.Generic;
using UnityEngine;
using Yahtzee.Core;
using Yahtzee.Feedback;
using Yahtzee.Localization;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [SerializeField] private List<UIScreen> screens = new List<UIScreen>();

        private void Awake()
        {
            // Resolve Classic Clean theme/skin configs before any UI/3D paint.
            UIThemeConfig.ResolveFromResources();
            BoardSkinConfig.ResolveFromResources();
            NeonVisualBootstrap.Ensure();
            DiceSkinApplier.ApplySaved();
            TableBoxSkinApplier.ApplySaved(null);
            GameText.Reload();
            var canvas = GetComponentInParent<Canvas>();
            UiFactory.RepairCanvas(canvas);
            if (canvas != null && canvas.GetComponent<SafeAreaFit>() == null)
                canvas.gameObject.AddComponent<SafeAreaFit>();
            DicePhysicsController.EnsurePhysicsRaycaster();
            GameplayScreen.ApplyGameplayCamera();

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Stretch(GetComponent<RectTransform>());
            EnsureLocalNetworkScreen();
            EnsureLobbyListScreen();
            EnsureCustomLobbyScreen();
            EnsureLeaderboardScreen();
            EnsureOnlineMenuScreen();
            EnsureOfflineMenuScreen();
            EnsureShopScreen();
            EnsureCustomizationScreen();
            EnsureMatchResultsScreen();
            EnsureSettingsScreen();
            FeedbackOverlay.Ensure(transform);
            Yahtzee.Audio.GameAudio.Ensure();
            StretchScreens();

            if (NetworkManager.Instance != null)
                _ = NetworkManager.Instance.RefreshServiceStatusAsync();
        }

        private void StretchScreens()
        {
            if (screens == null)
                return;

            for (var index = 0; index < screens.Count; index++)
            {
                var screen = screens[index];
                if (screen == null)
                    continue;

                Stretch(screen.transform as RectTransform);
            }
        }

        private static void Stretch(RectTransform rect)
        {
            if (rect == null)
                return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private void EnsureOnlineMenuScreen()
        {
            if (screens == null)
                screens = new List<UIScreen>();

            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is OnlineMenuScreen)
                    return;
            }

            screens.Add(CreateRuntimeScreen<OnlineMenuScreen>("Screen_OnlineMenu"));
        }

        private void EnsureOfflineMenuScreen()
        {
            if (screens == null)
                screens = new List<UIScreen>();

            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is OfflineMenuScreen)
                    return;
            }

            screens.Add(CreateRuntimeScreen<OfflineMenuScreen>("Screen_OfflineMenu"));
        }

        private void EnsureShopScreen()
        {
            if (screens == null)
                screens = new List<UIScreen>();

            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is ShopWindowController)
                    return;
            }

            screens.Add(CreateRuntimeScreen<ShopWindowController>("Screen_Shop"));
        }

        private void EnsureCustomizationScreen()
        {
            if (screens == null)
                screens = new List<UIScreen>();

            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is CustomizationWindowController)
                    return;
            }

            screens.Add(CreateRuntimeScreen<CustomizationWindowController>("Screen_Customization"));
        }

        private void EnsureMatchResultsScreen()
        {
            if (screens == null)
                screens = new List<UIScreen>();

            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is MatchResultsScreen)
                    return;
            }

            screens.Add(CreateRuntimeScreen<MatchResultsScreen>("Screen_MatchResults"));
        }

        private void EnsureSettingsScreen()
        {
            if (screens == null)
                screens = new List<UIScreen>();

            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is SettingsScreen)
                    return;
            }

            screens.Add(CreateRuntimeScreen<SettingsScreen>("Screen_Settings"));
        }

        private void EnsureLeaderboardScreen()
        {
            if (screens == null)
                screens = new List<UIScreen>();

            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is LeaderboardScreen)
                    return;
            }

            screens.Add(CreateRuntimeScreen<LeaderboardScreen>("Screen_Leaderboard"));
        }

        private void EnsureLobbyListScreen()
        {
            if (screens == null)
                screens = new List<UIScreen>();

            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is LobbyListScreen)
                    return;
            }

            screens.Add(CreateRuntimeScreen<LobbyListScreen>("Screen_LobbyList"));
        }

        private void EnsureCustomLobbyScreen()
        {
            if (screens == null)
                screens = new List<UIScreen>();

            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is CustomLobbyScreen)
                    return;
            }

            screens.Add(CreateRuntimeScreen<CustomLobbyScreen>("Screen_CustomLobby"));
        }

        private void EnsureLocalNetworkScreen()
        {
            if (screens == null)
                screens = new List<UIScreen>();

            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is LocalNetworkScreen)
                    return;
            }

            screens.Add(CreateRuntimeScreen<LocalNetworkScreen>("Screen_LocalNetwork"));
        }

        private T CreateRuntimeScreen<T>(string name) where T : UIScreen
        {
            var screenObject = new GameObject(name, typeof(RectTransform));
            screenObject.layer = gameObject.layer;
            screenObject.SetActive(false);
            var parent = transform;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
                parent = canvas.transform;

            screenObject.transform.SetParent(parent, false);
            Stretch(screenObject.GetComponent<RectTransform>());
            return screenObject.AddComponent<T>();
        }

        private void OnEnable()
        {
            NetworkManager.OnUserBanned += HandleUserBanned;
            NetworkManager.OnConnectionLost += HandleConnectionLost;
        }

        private void OnDisable()
        {
            NetworkManager.OnUserBanned -= HandleUserBanned;
            NetworkManager.OnConnectionLost -= HandleConnectionLost;
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(NetworkManager.LastBanReason))
            {
                HandleUserBanned(NetworkManager.LastBanReason);
                return;
            }

            SwitchToScreen(ScreenType.MainMenu);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public T GetScreen<T>() where T : UIScreen
        {
            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is T typed)
                    return typed;
            }

            return null;
        }

        public void SwitchToScreen(ScreenType type)
        {
            UIScreen selected = null;
            for (var index = 0; index < screens.Count; index++)
            {
                var screen = screens[index];
                if (screen != null && screen.Type == type)
                    selected = screen;
            }

            if (selected == null)
            {
                Debug.LogError("Экран не назначен в UIManager: " + type);
                return;
            }

            for (var index = 0; index < screens.Count; index++)
            {
                var screen = screens[index];
                if (screen != null)
                    screen.Hide();
            }

            selected.Show();
        }

        private void HandleUserBanned(string reason)
        {
            for (var index = 0; index < screens.Count; index++)
            {
                if (screens[index] is BannedOverlayScreen overlay)
                    overlay.SetReason(reason);
            }

            SwitchToScreen(ScreenType.BannedOverlay);
        }

        private void HandleConnectionLost()
        {
            if (!string.IsNullOrEmpty(NetworkManager.LastBanReason))
                return;

            // Match already ended / forgotten — don't yank results or flash reconnect UI.
            if (NetworkManager.Instance == null || !NetworkManager.Instance.ActiveMatchId.HasValue)
                return;

            SwitchToScreen(ScreenType.MainMenu);
        }
    }
}
