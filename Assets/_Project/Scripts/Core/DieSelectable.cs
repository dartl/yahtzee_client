using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Yahtzee.Core
{
    public sealed class DieSelectable : MonoBehaviour, IPointerClickHandler
    {
        public static event Action SelectionChanged;
        public static bool ClicksEnabled = true;

        public int dieIndex;
        public bool isSelected;

        private Vector3 _pitRest;
        private Vector3 _home;
        private Vector3 _shelfPoint;
        private Vector3 _target;
        private Quaternion _tableRotation = Quaternion.identity;
        private Quaternion _targetRotation = Quaternion.identity;
        private bool _armed;
        private bool _hasShelf;
        private Rigidbody _body;

        public float PitRestHeight => _pitRest.y;

        public Vector3 ReservedPoint => _armed ? _target : transform.position;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
                return;

            if (!ClicksEnabled)
                return;

            Toggle();
        }

        public void Arm(Vector3 restPosition)
        {
            _home = restPosition;
            _pitRest = restPosition;
            _armed = true;
            _body = GetComponent<Rigidbody>();
            _tableRotation = transform.rotation;
            _targetRotation = isSelected ? DicePhysicsController.Straighten(transform.rotation) : _tableRotation;
            _target = isSelected ? HeldTarget() : _pitRest;
            Park();
        }

        public void SetShelfPoint(Vector3 shelfPoint)
        {
            _shelfPoint = shelfPoint;
            _hasShelf = true;
            if (!_armed)
                return;

            if (isSelected)
                _target = _shelfPoint;
        }

        public void Toggle()
        {
            if (!_armed)
                return;

            SetHeld(!isSelected);
        }

        public void SetHeld(bool held)
        {
            if (!_armed || isSelected == held)
                return;

            if (held)
                _tableRotation = transform.rotation;

            var dieCollider = GetComponent<Collider>();
            if (dieCollider != null && DicePhysicsController.HasBoard)
                DicePhysicsController.LetDieCrossShelf(dieCollider);

            isSelected = held;
            if (held)
            {
                _target = HeldTarget();
                _targetRotation = DicePhysicsController.Straighten(_tableRotation);
            }
            else
            {
                _pitRest = DicePhysicsController.OpenPit(this, _home);
                _pitRest.y = _home.y;
                _target = _pitRest;
                _targetRotation = _tableRotation;
            }

            Park();
            SelectionChanged?.Invoke();
        }

        private float TargetScale => DicePhysicsController.DiceWorldScale;

        private Vector3 HeldTarget()
        {
            if (_hasShelf)
                return _shelfPoint;

            return _pitRest + new Vector3(0f, 0f, -0.5f);
        }

        private void Park()
        {
            if (_body == null)
                _body = GetComponent<Rigidbody>();
            if (_body == null)
                return;

            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.useGravity = false;
            _body.interpolation = RigidbodyInterpolation.None;
            _body.isKinematic = true;
        }

        private void Update()
        {
            if (!_armed)
                return;

            var blend = Time.deltaTime * 8f;
            var next = Vector3.Lerp(transform.position, _target, blend);
            var spin = Quaternion.Slerp(transform.rotation, _targetRotation, blend);
            var scale = Mathf.Lerp(transform.localScale.x, TargetScale, blend);
            transform.SetPositionAndRotation(next, spin);
            transform.localScale = Vector3.one * scale;
            if (_body != null && _body.isKinematic)
            {
                _body.position = next;
                _body.rotation = spin;
            }
        }
    }
}
