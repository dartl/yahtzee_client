using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Yahtzee.UI
{
    /// <summary>
    /// Tactile Flat 2.5D press feedback.
    /// Layout-driven buttons (submenus) only scale/tint — never touch anchoredPosition.
    /// Absolutely placed buttons may also sink a few pixels on press.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ButtonVisualFeedback : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private float hoverLiftPx = 1.5f;
        [SerializeField] private float pressSinkPx = 3f;
        [SerializeField] private float hoverScale = 1.02f;
        [SerializeField] private float pressScale = 0.97f;
        [SerializeField] private float lerpSpeed = 18f;

        private RectTransform _rect;
        private Image _face;
        private Image _ledge;
        private Color _faceBase = Color.white;
        private Vector3 _restScale = Vector3.one;
        private float _posOffset;
        private float _targetOffset;
        private float _targetScale = 1f;
        private bool _hovered;
        private bool _pressed;
        private bool _layoutDriven;
        private bool _scaleCaptured;

        public static void Ensure(Button button)
        {
            if (button == null)
                return;

            var legacy = button.GetComponent<NeonButtonMotion>();
            if (legacy != null)
                legacy.enabled = false;

            if (button.GetComponent<ButtonVisualFeedback>() == null)
                button.gameObject.AddComponent<ButtonVisualFeedback>();
        }

        public void Configure(float pressSink, Color faceColor)
        {
            pressSinkPx = Mathf.Max(2f, pressSink);
            _faceBase = faceColor;
            if (_face != null)
                _face.color = faceColor;
        }

        private void Awake()
        {
            Cache();
        }

        private void OnEnable()
        {
            Cache();
            _hovered = false;
            _pressed = false;
            _targetOffset = 0f;
            _targetScale = 1f;
            _posOffset = 0f;
            ApplyImmediate();
        }

        private void OnDisable()
        {
            // Leave layout children exactly where the layout put them.
            if (_rect != null && _scaleCaptured)
                _rect.localScale = _restScale;
            if (_face != null)
                _face.color = _faceBase;
            if (_ledge != null)
                _ledge.enabled = true;
            _posOffset = 0f;
        }

        private void Cache()
        {
            _rect = transform as RectTransform;
            var parentLayout = GetComponentInParent<LayoutGroup>();
            _layoutDriven = parentLayout != null && parentLayout.transform != transform;
            // Stretched overlay buttons (tutorial skip, etc.) must not have anchoredPosition rewritten.
            if (_rect != null
                && (Mathf.Abs(_rect.anchorMax.x - _rect.anchorMin.x) > 0.001f
                    || Mathf.Abs(_rect.anchorMax.y - _rect.anchorMin.y) > 0.001f))
                _layoutDriven = true;

            if (_rect != null && !_scaleCaptured)
            {
                _restScale = _rect.localScale;
                if (_restScale.sqrMagnitude < 0.0001f)
                    _restScale = Vector3.one;
                _scaleCaptured = true;
            }

            _face = GetComponent<Image>();
            if (_face != null)
                _faceBase = _face.color;

            var ledge = transform.Find("DepthLedge");
            if (ledge != null)
            {
                _ledge = ledge.GetComponent<Image>();
                var ignore = ledge.GetComponent<LayoutElement>();
                if (ignore == null)
                    ignore = ledge.gameObject.AddComponent<LayoutElement>();
                ignore.ignoreLayout = true;
            }
        }

        private void LateUpdate()
        {
            if (_rect == null)
                return;

            var nextScale = Mathf.Lerp(
                _rect.localScale.x / Mathf.Max(0.0001f, _restScale.x),
                _targetScale,
                1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime));
            _rect.localScale = _restScale * nextScale;

            if (!_layoutDriven)
            {
                var nextOffset = Mathf.Lerp(
                    _posOffset,
                    _targetOffset,
                    1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime));
                var delta = nextOffset - _posOffset;
                if (Mathf.Abs(delta) > 0.001f)
                    _rect.anchoredPosition += new Vector2(0f, delta);
                _posOffset = nextOffset;
            }

            if (_face != null)
            {
                var lit = _hovered && !_pressed ? 1.06f : 1f;
                var target = new Color(
                    Mathf.Clamp01(_faceBase.r * lit),
                    Mathf.Clamp01(_faceBase.g * lit),
                    Mathf.Clamp01(_faceBase.b * lit),
                    _faceBase.a);
                _face.color = Color.Lerp(_face.color, target, 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime));
            }

            if (_ledge != null)
                _ledge.enabled = !_pressed;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            RefreshTarget();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _pressed = false;
            RefreshTarget();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            RefreshTarget();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            RefreshTarget();
        }

        private void RefreshTarget()
        {
            if (_pressed)
            {
                _targetOffset = _layoutDriven ? 0f : -pressSinkPx;
                _targetScale = pressScale;
            }
            else if (_hovered)
            {
                _targetOffset = _layoutDriven ? 0f : hoverLiftPx;
                _targetScale = hoverScale;
            }
            else
            {
                _targetOffset = 0f;
                _targetScale = 1f;
            }
        }

        private void ApplyImmediate()
        {
            if (_rect == null)
                return;

            _rect.localScale = _restScale * _targetScale;
            _posOffset = _targetOffset;
            if (_face != null)
                _face.color = _faceBase;
            if (_ledge != null)
                _ledge.enabled = !_pressed;
        }
    }
}
