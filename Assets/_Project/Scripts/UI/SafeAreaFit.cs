using UnityEngine;

namespace Yahtzee.UI
{
    [DisallowMultipleComponent]
    public sealed class SafeAreaFit : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _applied;
        private int _width = -1;
        private int _height = -1;

        private void OnEnable()
        {
            _rect = transform as RectTransform;
            ForceApply();
        }

        private void Update()
        {
            if (_rect == null)
                return;

            var safe = Screen.safeArea;
            if (_width == Screen.width && _height == Screen.height && _applied == safe)
                return;

            ForceApply();
        }

        public void ForceApply()
        {
            if (_rect == null)
                _rect = transform as RectTransform;
            if (_rect == null)
                return;

            var width = Screen.width;
            var height = Screen.height;
            var safe = Screen.safeArea;
            _width = width;
            _height = height;
            _applied = safe;
            if (width <= 0 || height <= 0)
                return;

            _rect.anchorMin = new Vector2(safe.xMin / width, safe.yMin / height);
            _rect.anchorMax = new Vector2(safe.xMax / width, safe.yMax / height);
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
            _rect.pivot = new Vector2(0.5f, 0.5f);
        }
    }
}
