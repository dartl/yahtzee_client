using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using Yahtzee.Core;

namespace Yahtzee.Network
{
    public sealed class SkinManager : MonoBehaviour
    {
        public const string CoinsPrefKey = "yahtzee.local.coins";

        public static SkinManager Instance { get; private set; }

        public static event Action<int> CurrencyChanged;

        private readonly List<SkinData> _skins = new List<SkinData>();
        private int _softCurrency;
        private bool _hasCatalog;

        public IReadOnlyList<SkinData> Skins => _skins;
        public int SoftCurrency => _softCurrency;
        public bool HasCatalog => _hasCatalog;

        public static SkinManager Ensure()
        {
            if (Instance != null)
                return Instance;

            var existing = FindAnyObjectByType<SkinManager>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var host = new GameObject("SkinManager");
            DontDestroyOnLoad(host);
            return host.AddComponent<SkinManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (int.TryParse(PlayerPrefs.GetString(CoinsPrefKey, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var coins))
                _softCurrency = Mathf.Max(0, coins);
        }

        public void ApplyServerBalance(int coins)
        {
            SetCurrency(coins);
        }

        public void GrantLocalReward(int coins)
        {
            if (coins <= 0)
                return;

            SetCurrency(_softCurrency + coins);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public static void Remember(ProfileResponse profile)
        {
            if (profile == null)
                return;

            if (!string.IsNullOrEmpty(profile.EquippedDiceSkinSlug))
                PlayerPrefs.SetString(DiceSkinApplier.PrefKey, profile.EquippedDiceSkinSlug);
            if (!string.IsNullOrEmpty(profile.EquippedTableBoxSkinSlug))
                PlayerPrefs.SetString(TableBoxSkinApplier.PrefKey, profile.EquippedTableBoxSkinSlug);
            PlayerPrefs.Save();
            DiceSkinApplier.ApplySaved();
            TableBoxSkinApplier.ApplySaved(null);
        }

        public async Task Refresh()
        {
            var network = NetworkManager.Instance;
            if (network == null)
                throw new InvalidOperationException("Сеть ещё не готова.");

            var catalog = await network.GetShop();
            _skins.Clear();
            if (catalog.Skins != null)
            {
                for (var index = 0; index < catalog.Skins.Length; index++)
                {
                    if (catalog.Skins[index] != null)
                        _skins.Add(catalog.Skins[index]);
                }
            }

            _hasCatalog = true;
            SetCurrency(catalog.SoftCurrency);
        }

        public async Task Buy(int skinId)
        {
            var network = NetworkManager.Instance;
            if (network == null)
                throw new InvalidOperationException("Сеть ещё не готова.");

            var result = await network.BuySkin(skinId);
            ApplyAction(result);
        }

        public async Task Equip(int skinId)
        {
            var network = NetworkManager.Instance;
            if (network == null)
                throw new InvalidOperationException("Сеть ещё не готова.");

            var result = await network.EquipSkin(skinId);
            ApplyAction(result);
            Remember(result.User);
        }

        private void ApplyAction(SkinActionResponse result)
        {
            if (result == null)
                return;

            if (result.Skin != null)
            {
                var replaced = false;
                for (var index = 0; index < _skins.Count; index++)
                {
                    var skin = _skins[index];
                    if (skin == null)
                        continue;

                    if (skin.Id == result.Skin.Id)
                    {
                        _skins[index] = result.Skin;
                        replaced = true;
                        continue;
                    }

                    if (result.Skin.Equipped && skin.Kind == result.Skin.Kind)
                        skin.Equipped = false;
                }

                if (!replaced)
                    _skins.Add(result.Skin);
            }

            SetCurrency(result.SoftCurrency);
        }

        private void SetCurrency(int coins)
        {
            _softCurrency = Math.Max(0, coins);
            PlayerPrefs.SetString(CoinsPrefKey, _softCurrency.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
            CurrencyChanged?.Invoke(_softCurrency);
        }
    }
}
