using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Yahtzee.UI
{
    /// <summary>Subtle hover/press scale for Material flat buttons.</summary>
    [DisallowMultipleComponent]
    public sealed class NeonButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private float hoverScale = 1.035f;
        [SerializeField] private float pressScale = 0.97f;
        [SerializeField] private float lerpSpeed = 14f;
        [SerializeField] private float hoverRimBoost = 0.22f;

        private RectTransform _rect;
        private Image _rim;
        private Color _rimBase;
        private Vector3 _restScale = Vector3.one;
        private float _target = 1f;
        private bool _hovered;
        private bool _pressed;

        public static void Ensure(Button button)
        {
            // Forward to tactile Flat 2.5D feedback used by Classic Clean.
            ButtonVisualFeedback.Ensure(button);
        }

        private void Awake()
        {
            _rect = transform as RectTransform;
            _restScale = _rect != null ? _rect.localScale : Vector3.one;
            var rim = transform.Find("Rim");
            if (rim != null)
            {
                _rim = rim.GetComponent<Image>();
                if (_rim != null)
                    _rimBase = _rim.color;
            }
        }

        private void OnEnable()
        {
            _hovered = false;
            _pressed = false;
            _target = 1f;
            ApplyImmediate();
        }

        private void Update()
        {
            if (_rect == null)
                return;

            var current = _rect.localScale.x / Mathf.Max(0.0001f, _restScale.x);
            var next = Mathf.Lerp(current, _target, 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime));
            _rect.localScale = _restScale * next;

            if (_rim == null)
                return;

            var boost = _hovered || _pressed ? hoverRimBoost : 0f;
            var c = _rimBase;
            c.a = Mathf.Clamp01(_rimBase.a + boost);
            _rim.color = Color.Lerp(_rim.color, c, 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime));
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
                _target = pressScale;
            else if (_hovered)
                _target = hoverScale;
            else
                _target = 1f;
        }

        private void ApplyImmediate()
        {
            if (_rect != null)
                _rect.localScale = _restScale * _target;
            if (_rim != null)
                _rim.color = _rimBase;
        }
    }
}
