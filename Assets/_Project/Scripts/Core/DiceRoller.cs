using UnityEngine;

namespace Yahtzee.Core
{
    /// <summary>
    /// Throw impulses + top-face readout via Vector3.Dot against Vector3.up.
    /// Integrates with <see cref="DicePhysicsController"/> / <see cref="ProceduralDiceTray"/>.
    /// </summary>
    public static class DiceRoller
    {
        public static readonly Vector3[] LocalFaceNormals =
        {
            Vector3.zero,
            Vector3.up,      // 1
            Vector3.forward, // 2
            Vector3.right,   // 3
            Vector3.left,    // 4
            Vector3.back,    // 5
            Vector3.down     // 6
        };

        public static void Throw(Rigidbody body, Vector3 force, Vector3 torque)
        {
            if (body == null)
                return;

            body.isKinematic = false;
            body.useGravity = true;
            body.WakeUp();
            body.AddForce(force, ForceMode.VelocityChange);
            body.AddTorque(torque, ForceMode.VelocityChange);
        }

        public static void ThrowToward(Rigidbody body, Vector3 worldTarget, float force, float torqueMagnitude)
        {
            if (body == null)
                return;

            var flat = worldTarget - body.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f)
                flat = Vector3.forward;
            flat.Normalize();

            var loft = Random.Range(0.55f, 0.95f);
            var sway = Vector3.Cross(Vector3.up, flat) * Random.Range(-0.28f, 0.28f);
            var direction = (flat + Vector3.up * loft + sway).normalized;
            var spin = Random.onUnitSphere;
            if (spin.sqrMagnitude < 0.0001f)
                spin = Vector3.right;

            Throw(body, direction * force, spin.normalized * torqueMagnitude);
        }

        public static bool IsSettled(Rigidbody body, float linearThreshold = 0.05f, float angularThreshold = 0.08f)
        {
            if (body == null)
                return true;

            if (body.IsSleeping())
                return true;

            return body.linearVelocity.sqrMagnitude <= linearThreshold * linearThreshold
                && body.angularVelocity.sqrMagnitude <= angularThreshold * angularThreshold;
        }

        public static int ReadTopFace(Transform die)
        {
            if (die == null)
                return 1;

            return ReadTopFace(die.rotation);
        }

        public static int ReadTopFace(Quaternion rotation)
        {
            var best = 1;
            var bestDot = -2f;
            for (var value = 1; value <= 6; value++)
            {
                var worldNormal = rotation * LocalFaceNormals[value];
                var dot = Vector3.Dot(worldNormal, Vector3.up);
                if (dot <= bestDot)
                    continue;

                bestDot = dot;
                best = value;
            }

            return best;
        }

        public static bool TryClampInsideTray(Rigidbody body, ProceduralDiceTray tray, float pad = 0.08f)
        {
            if (body == null || tray == null || !tray.TryGetInnerArea(out var min, out var max))
                return false;

            var p = body.position;
            var clamped = new Vector3(
                Mathf.Clamp(p.x, min.x + pad, max.x - pad),
                Mathf.Max(p.y, tray.FloorY + 0.05f),
                Mathf.Clamp(p.z, min.z + pad, max.z - pad));
            if ((clamped - p).sqrMagnitude < 0.00001f)
                return false;

            body.position = clamped;
            body.linearVelocity *= 0.4f;
            body.angularVelocity *= 0.4f;
            return true;
        }
    }
}
