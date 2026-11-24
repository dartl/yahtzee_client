using UnityEngine;
using UnityEngine.UI;

namespace Yahtzee.UI
{
    /// <summary>
    /// Retries <see cref="UiFactory.FinalizeMenuForm"/> until the viewport has a real height,
    /// so submenu stacks end up vertically centered instead of stuck at the top.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuFormFitter : MonoBehaviour
    {
        private ScrollRect _scroll;
        private int _attempts;

        public static void Bind(ScrollRect scroll)
        {
            if (scroll == null)
                return;

            var fitter = scroll.GetComponent<MenuFormFitter>();
            if (fitter == null)
                fitter = scroll.gameObject.AddComponent<MenuFormFitter>();

            fitter._scroll = scroll;
            fitter._attempts = 0;
            fitter.enabled = true;
        }

        private void LateUpdate()
        {
            if (_scroll == null || _scroll.viewport == null)
            {
                enabled = false;
                return;
            }

            Canvas.ForceUpdateCanvases();
            var viewHeight = _scroll.viewport.rect.height;
            if (viewHeight < 8f)
            {
                var root = _scroll.transform as RectTransform;
                if (root != null)
                    viewHeight = root.rect.height;
            }

            if (viewHeight < 8f)
            {
                _attempts++;
                if (_attempts > 12)
                    enabled = false;
                return;
            }

            UiFactory.FinalizeMenuForm(_scroll);
            enabled = false;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_scroll == null)
                return;

            _attempts = 0;
            enabled = true;
        }
    }
}
