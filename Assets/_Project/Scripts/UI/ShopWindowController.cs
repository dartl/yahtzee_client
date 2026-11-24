using System;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Yahtzee.Audio;
using Yahtzee.Feedback;
using Yahtzee.Localization;
using Yahtzee.Network;

namespace Yahtzee.UI
{
    public sealed class ShopWindowController : UIScreen
    {
        public override ScreenType Type => ScreenType.Shop;

        private Text _balance;
        private Text _status;
        private Text _header;
        private RectTransform _list;
        private bool _busy;
        private EconomyCatalogDto _economy;

        public override void Show()
        {
            base.Show();
            var rect = (RectTransform)transform;
            UiFactory.EnsureScreenFillsCanvas(rect);
            UiFactory.ApplyOpaqueMenuBackground(rect);
            EnsureChrome();
            UiMotion.PopIn(transform);
            _ = Reload();
        }

        private void EnsureChrome()
        {
            if (_list != null)
            {
                if (_header != null)
                    _header.text = GameText.Get("ui.shop.title");
                return;
            }

            _header = UiFactory.CreateScreenHeader(
                transform,
                GameText.Get("ui.shop.title"),
                GameText.Get("ui.shop.subtitle"));
            _balance = UiFactory.CreateLabel(transform, "Balance", GameText.Format("ui.shop.coins_line", "—"), 28, TextAnchor.MiddleCenter);
            UiFactory.Place(_balance.rectTransform, 0.08f, 0.84f, 0.92f, 0.9f);
            _status = UiFactory.CreateLabel(transform, "Status", string.Empty, 24, TextAnchor.MiddleCenter);
            UiFactory.Place(_status.rectTransform, 0.08f, 0.78f, 0.92f, 0.84f);

            var listObject = new GameObject("List", typeof(RectTransform));
            listObject.layer = gameObject.layer;
            listObject.transform.SetParent(transform, false);
            _list = listObject.GetComponent<RectTransform>();
            UiFactory.Place(_list, 0.08f, 0.16f, 0.92f, 0.77f);

            var back = UiFactory.CreateButton(transform, GameText.Get("ui.common.back"));
            UiFactory.StyleGlass(back);
            UiFactory.Place(back.GetComponent<RectTransform>(), 0.22f, 0.03f, 0.78f, 0.12f);
            back.onClick.AddListener(ReturnToMenu);
        }

        private async Task Reload()
        {
            if (_busy)
                return;

            if (!ClientGate.ShopEnabled)
            {
                ScreenFeedback.SetInfo(_status, "ui.common.feature_disabled");
                return;
            }

            _busy = true;
            ScreenFeedback.SetInfo(_status, "ui.shop.loading");

            try
            {
                await SkinManager.Ensure().Refresh();
                try
                {
                    _economy = await MonetizationService.LoadCatalogAsync();
                }
                catch (Exception economyException)
                {
                    Debug.LogWarning("Yahtzee shop economy: " + economyException.Message);
                    _economy = null;
                }
            }
            catch (Exception exception)
            {
                ScreenFeedback.SetError(_status, exception);
            }
            finally
            {
                _busy = false;
                if (this != null)
                    Paint();
            }
        }

        private void Paint()
        {
            if (_list == null)
                return;

            for (var index = _list.childCount - 1; index >= 0; index--)
                Destroy(_list.GetChild(index).gameObject);

            var manager = SkinManager.Instance;
            if (_balance != null)
            {
                var coins = manager != null
                    ? manager.SoftCurrency.ToString(CultureInfo.InvariantCulture)
                    : "—";
                _balance.text = GameText.Format("ui.shop.coins_line", coins);
            }

            ScreenFeedback.Clear(_status);

            var rows = 0;
            rows += PaintEconomySection(rows);
            rows += PaintSkinSection(manager, rows);
        }

        private int PaintEconomySection(int startIndex)
        {
            var painted = 0;
            var slot = startIndex;

            if (_economy != null && MonetizationService.AdsAllowed)
            {
                painted += AddActionRow(
                    slot++,
                    GameText.Format("ui.shop.rewarded_ad", _economy.RewardedAdCoins.ToString(CultureInfo.InvariantCulture)),
                    GameText.Get("ui.shop.watch_ad"),
                    () => _ = WatchAd());
            }
            else if (_economy != null && !MonetizationService.AdsAllowed)
            {
                painted += AddInfoRow(slot++, GameText.Get("ui.shop.ad_free_active"));
            }

            if (_economy?.Products != null)
            {
                for (var index = 0; index < _economy.Products.Length; index++)
                {
                    var product = _economy.Products[index];
                    if (product == null || string.IsNullOrWhiteSpace(product.ProductId))
                        continue;

                    var caption = string.IsNullOrWhiteSpace(product.Label) ? product.ProductId : product.Label;
                    var id = product.ProductId;
                    painted += AddActionRow(
                        slot++,
                        caption,
                        GameText.Get("ui.shop.buy"),
                        () => _ = BuyProduct(id));
                }
            }

            return painted;
        }

        private int PaintSkinSection(SkinManager manager, int startIndex)
        {
            if (manager == null)
                return 0;

            var painted = 0;
            var slot = startIndex;
            var count = manager.Skins.Count;
            for (var index = 0; index < count; index++)
            {
                var skin = manager.Skins[index];
                if (skin == null)
                    continue;

                var top = 1f - slot * 0.11f;
                var bottom = top - 0.1f;
                if (bottom < 0f)
                    break;

                var row = new GameObject("SkinRow", typeof(RectTransform), typeof(Image));
                row.layer = gameObject.layer;
                row.transform.SetParent(_list, false);
                var rowImage = row.GetComponent<Image>();
                rowImage.sprite = UiFactory.RoundSprite;
                rowImage.type = Image.Type.Sliced;
                rowImage.color = new Color(1f, 1f, 1f, 0.08f);
                var rowRect = row.GetComponent<RectTransform>();
                rowRect.anchorMin = new Vector2(0f, bottom);
                rowRect.anchorMax = new Vector2(1f, top);
                rowRect.offsetMin = Vector2.zero;
                rowRect.offsetMax = Vector2.zero;

                var preview = SkinPreviewFactory.Create(row.transform, skin);
                UiFactory.Place(preview.rectTransform, 0.02f, 0.12f, 0.22f, 0.88f);

                var caption = skin.Name + "\n" + KindLabel(skin) + " · " + PriceLabel(skin);
                var label = UiFactory.CreateLabel(row.transform, "Name", caption, 22, TextAnchor.MiddleLeft);
                UiFactory.Place(label.rectTransform, 0.24f, 0.08f, 0.62f, 0.92f);
                var button = UiFactory.CreateButton(
                    row.transform,
                    skin.Owned ? GameText.Get("ui.shop.owned") : GameText.Get("ui.shop.buy"));
                UiFactory.StyleGlass(button);
                UiFactory.Place(button.GetComponent<RectTransform>(), 0.64f, 0.15f, 0.98f, 0.85f);
                button.interactable = !skin.Owned && manager.SoftCurrency >= skin.Price;
                var skinId = skin.Id;
                button.onClick.AddListener(() => _ = Purchase(skinId));
                slot++;
                painted++;
            }

            return painted;
        }

        private int AddActionRow(int slot, string caption, string actionLabel, Action onClick)
        {
            var top = 1f - slot * 0.11f;
            var bottom = top - 0.1f;
            if (bottom < 0f)
                return 0;

            var row = new GameObject("EcoRow", typeof(RectTransform), typeof(Image));
            row.layer = gameObject.layer;
            row.transform.SetParent(_list, false);
            var rowImage = row.GetComponent<Image>();
            rowImage.sprite = UiFactory.RoundSprite;
            rowImage.type = Image.Type.Sliced;
            rowImage.color = new Color(1f, 1f, 1f, 0.1f);
            var rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, bottom);
            rowRect.anchorMax = new Vector2(1f, top);
            rowRect.offsetMin = Vector2.zero;
            rowRect.offsetMax = Vector2.zero;

            var label = UiFactory.CreateLabel(row.transform, "Name", caption, 22, TextAnchor.MiddleLeft);
            UiFactory.Place(label.rectTransform, 0.04f, 0.08f, 0.62f, 0.92f);
            var button = UiFactory.CreateButton(row.transform, actionLabel);
            UiFactory.StyleAccent(button);
            UiFactory.Place(button.GetComponent<RectTransform>(), 0.64f, 0.15f, 0.98f, 0.85f);
            button.onClick.AddListener(() => onClick());
            return 1;
        }

        private int AddInfoRow(int slot, string caption)
        {
            var top = 1f - slot * 0.11f;
            var bottom = top - 0.1f;
            if (bottom < 0f)
                return 0;

            var row = new GameObject("InfoRow", typeof(RectTransform), typeof(Image));
            row.layer = gameObject.layer;
            row.transform.SetParent(_list, false);
            var rowImage = row.GetComponent<Image>();
            rowImage.sprite = UiFactory.RoundSprite;
            rowImage.type = Image.Type.Sliced;
            rowImage.color = new Color(1f, 1f, 1f, 0.06f);
            var rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, bottom);
            rowRect.anchorMax = new Vector2(1f, top);
            rowRect.offsetMin = Vector2.zero;
            rowRect.offsetMax = Vector2.zero;

            var label = UiFactory.CreateLabel(row.transform, "Name", caption, 22, TextAnchor.MiddleCenter);
            UiFactory.Place(label.rectTransform, 0.04f, 0.08f, 0.96f, 0.92f);
            return 1;
        }

        private async Task WatchAd()
        {
            if (_busy)
                return;

            _busy = true;
            GameAudio.PlayUiClick();
            try
            {
                await MonetizationService.ShowRewardedAdAsync();
                if (this != null)
                    await Reload();
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task BuyProduct(string productId)
        {
            if (_busy || string.IsNullOrWhiteSpace(productId))
                return;

            _busy = true;
            GameAudio.PlayUiClick();
            try
            {
                await MonetizationService.PurchaseProductAsync(productId);
                if (this != null)
                    await Reload();
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task Purchase(int skinId)
        {
            if (_busy)
                return;

            _busy = true;
            GameAudio.PlayUiClick();
            try
            {
                await SkinManager.Ensure().Buy(skinId);
                ScreenFeedback.SetInfo(_status, "ui.shop.bought");
                if (this != null)
                    Paint();
            }
            catch (Exception exception)
            {
                ScreenFeedback.SetError(_status, exception);
            }
            finally
            {
                _busy = false;
                if (this != null)
                    Paint();
            }
        }

        private static string KindLabel(SkinData skin)
        {
            return skin.Kind == SkinType.TableBox
                ? GameText.Get("ui.shop.kind_table")
                : GameText.Get("ui.shop.kind_dice");
        }

        private static string PriceLabel(SkinData skin)
        {
            return skin.Price <= 0
                ? GameText.Get("ui.shop.free")
                : GameText.Format("ui.shop.price_coins", skin.Price.ToString(CultureInfo.InvariantCulture));
        }

        private static void ReturnToMenu()
        {
            GameAudio.PlayUiClick();
            if (UIManager.Instance != null)
                UIManager.Instance.SwitchToScreen(ScreenType.MainMenu);
        }
    }
}
