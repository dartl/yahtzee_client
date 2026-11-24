using System.Collections;
using UnityEngine;

namespace Yahtzee.UI
{
    public static class UiMotion
    {
        public static void Punch(Transform target, float scale = 1.08f, float seconds = 0.14f)
        {
            if (target == null)
                return;

            var runner = target.GetComponent<UiMotionRunner>();
            if (runner == null)
                runner = target.gameObject.AddComponent<UiMotionRunner>();

            runner.PlayPunch(scale, seconds);
        }

        public static void PopIn(Transform target, float seconds = 0.22f)
        {
            if (target == null)
                return;

            var runner = target.GetComponent<UiMotionRunner>();
            if (runner == null)
                runner = target.gameObject.AddComponent<UiMotionRunner>();

            runner.PlayPop(seconds);
        }

        private sealed class UiMotionRunner : MonoBehaviour
        {
            private Coroutine _active;

            public void PlayPunch(float scale, float seconds)
            {
                if (_active != null)
                    StopCoroutine(_active);
                _active = StartCoroutine(PunchRoutine(transform, scale, seconds));
            }

            public void PlayPop(float seconds)
            {
                if (_active != null)
                    StopCoroutine(_active);
                transform.localScale = Vector3.one * 0.86f;
                _active = StartCoroutine(PopRoutine(transform, seconds));
            }

            private static IEnumerator PunchRoutine(Transform target, float scale, float seconds)
            {
                var half = Mathf.Max(0.02f, seconds * 0.5f);
                var elapsed = 0f;
                while (elapsed < half)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(elapsed / half);
                    var value = Mathf.Lerp(1f, scale, t);
                    target.localScale = new Vector3(value, value, 1f);
                    yield return null;
                }

                elapsed = 0f;
                while (elapsed < half)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(elapsed / half);
                    var value = Mathf.Lerp(scale, 1f, t);
                    target.localScale = new Vector3(value, value, 1f);
                    yield return null;
                }

                target.localScale = Vector3.one;
            }

            private static IEnumerator PopRoutine(Transform target, float seconds)
            {
                var elapsed = 0f;
                while (elapsed < seconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var t = Mathf.Clamp01(elapsed / seconds);
                    var eased = 1f - Mathf.Pow(1f - t, 3f);
                    var value = Mathf.Lerp(0.86f, 1f, eased);
                    target.localScale = new Vector3(value, value, 1f);
                    yield return null;
                }

                target.localScale = Vector3.one;
            }
        }
    }
}
