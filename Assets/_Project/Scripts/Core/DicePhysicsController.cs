using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Yahtzee.Core
{
    public sealed class DicePhysicsController : MonoBehaviour
    {
        private const float FaceOffset = 0.51f;
        public const float DiceWorldScale = 0.25f;
        public const float HeldDiceWorldScale = 0.168f;
        private const float MaxSimSeconds = 3.2f;
        private const float SimStep = 0.02f;
        private const float PlaybackStepCap = 0.02f;
        private const float RestLinearSqr = 0.008f;
        private const float RestAngularSqr = 0.04f;

        [SerializeField] private GameObject dicePrefab;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private Collider tableCollider;

        private static readonly Vector2[][] PipLayouts =
        {
            Array.Empty<Vector2>(),
            new[] { Vector2.zero },
            new[] { new Vector2(-1f, 1f), new Vector2(1f, -1f) },
            new[] { new Vector2(-1f, 1f), Vector2.zero, new Vector2(1f, -1f) },
            new[] { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f) },
            new[] { new Vector2(-1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-1f, -1f), new Vector2(1f, -1f) },
            new[] { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(-1f, -1f), new Vector2(1f, -1f) }
        };

        private static Material _dieBodyMaterial;
        private static Material _pipMaterial;
        private static DicePhysicsController _active;
        private readonly GameObject[] _slots = new GameObject[5];
        private readonly KeptDie[] _keptOrder = new KeptDie[5];
        private PhysicsMaterial _surface;
        private PhysicsMaterial _dieSurface;
        private Vector3 _landingPoint;
        private bool _hasLanding;
        private int _generation;
        private int _seenLayout;
        private GameBoardController _board;

        public static void EnsurePhysicsRaycaster()
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            var raycaster = camera.GetComponent<PhysicsRaycaster>();
            if (raycaster == null)
                raycaster = camera.gameObject.AddComponent<PhysicsRaycaster>();

            raycaster.eventMask = Physics.DefaultRaycastLayers;
        }

        public int[] GetSelectedDiceIndices()
        {
            var count = 0;
            for (var index = 0; index < _slots.Length; index++)
            {
                if (IsHeld(_slots[index]))
                    count++;
            }

            var selected = new int[count];
            var write = 0;
            for (var index = 0; index < _slots.Length; index++)
            {
                if (!IsHeld(_slots[index]))
                    continue;

                selected[write] = index;
                write++;
            }

            return selected;
        }

        public bool[] HeldMask()
        {
            var mask = new bool[_slots.Length];
            for (var index = 0; index < _slots.Length; index++)
                mask[index] = IsHeld(_slots[index]);

            return mask;
        }

        public void SetKeptDice(int[] keepIndices)
        {
            var keep = new bool[_slots.Length];
            if (keepIndices != null)
            {
                for (var index = 0; index < keepIndices.Length; index++)
                {
                    var slot = keepIndices[index];
                    if (slot >= 0 && slot < keep.Length)
                        keep[slot] = true;
                }
            }

            AssignShelfPoints();
            for (var index = 0; index < _slots.Length; index++)
            {
                var die = _slots[index];
                if (die == null)
                    continue;

                var selectable = die.GetComponent<DieSelectable>();
                if (selectable != null)
                    selectable.SetHeld(keep[index]);
            }
        }

        public void ClearTable()
        {
            _generation++;
            ClearDice();
        }

        public void ShowSettledDice(int[] values)
        {
            _generation++;
            ClearDice();
            if (values == null || values.Length == 0 || dicePrefab == null)
                return;

            DiceGameEngine.Validate(values);
            var restY = TableRestY();
            var origin = ThrowOrigin(restY);
            var right = spawnPoint != null ? spawnPoint.right : Vector3.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            right.Normalize();
            var spacing = DiceWorldScale * 1.35f;

            for (var index = 0; index < values.Length && index < _slots.Length; index++)
            {
                var lateral = (index - (values.Length - 1) * 0.5f) * spacing;
                var position = origin + right * lateral;
                position.y = restY;
                var die = Instantiate(dicePrefab, position, GetRotationForValue(values[index]));
                die.transform.localScale = Vector3.one * DiceWorldScale;
                ProceduralDiceVisual.EnsureOn(die);
                var body = die.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.useGravity = false;
                    body.interpolation = RigidbodyInterpolation.None;
                    body.isKinematic = true;
                }

                var selectable = die.GetComponent<DieSelectable>();
                if (selectable == null)
                    selectable = die.AddComponent<DieSelectable>();
                selectable.dieIndex = index;
                selectable.isSelected = false;
                selectable.Arm(position);
                ApplyDieSurface(die.GetComponent<BoxCollider>());
                EnsureFaceDigits(die);
                _slots[index] = die;
            }

            AssignShelfPoints();
        }

        public void PlaceThrowAbove(Vector3 tablePoint)
        {
            _landingPoint = tablePoint;
            _hasLanding = true;
            if (spawnPoint == null)
                return;

            spawnPoint.position = tablePoint + new Vector3(0f, 0.7f, -0.22f);
            var look = tablePoint - spawnPoint.position;
            if (look.sqrMagnitude < 0.0001f)
                look = Vector3.down;
            spawnPoint.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }

        private void Awake()
        {
            _active = this;
            EnsurePhysicsRaycaster();
            ApplyTableSurface();
            _board = GameBoardController.Ensure(tableCollider);
            DieSelectable.SelectionChanged += ArrangeKeptDice;
            DiceSkinApplier.ApplySaved();
        }

        public static void SetSkinColors(Color body, Color pips)
        {
            var smooth = BoardSkinConfig.Active.diceSmoothness;
            Paint(DieBodyMaterial(), body, smooth, 0.04f);
            Paint(PipMaterial(), pips, 0.35f, 0.02f);
            ProceduralDiceVisual.SetColors(body, pips);
        }

        private static void Paint(Material material, Color color, float smoothness, float metallic)
        {
            if (material == null)
                return;

            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
        }

        private void Update()
        {
            if (_board == null)
                return;

            var revision = _board.LayoutRevision;
            if (revision == _seenLayout)
                return;

            _seenLayout = revision;
            AssignShelfPoints();
        }

        public async Task RollDicePhysical(int[] finalValues)
        {
            DiceGameEngine.Validate(finalValues);
#if UNITY_EDITOR
            Debug.Log("Ожидаемые грани: " + string.Join(" ", finalValues));
#endif
            if (dicePrefab == null)
                throw new InvalidOperationException("Назначьте префаб кубика в DicePhysicsController.");
            if (spawnPoint == null)
                throw new InvalidOperationException("Назначьте точку броска в DicePhysicsController.");

            if (dicePrefab.GetComponent<Rigidbody>() == null || dicePrefab.GetComponent<BoxCollider>() == null)
                throw new InvalidOperationException("Префаб кубика должен содержать Rigidbody и BoxCollider.");

            var generation = ++_generation;
            ReleaseUnheld();
            ApplyTableSurface();

            var spacing = DiceWorldScale * 1.35f;
            var bodies = new Rigidbody[finalValues.Length];
            var spawns = new Vector3[finalValues.Length];
            var toss = BuildToss();

            for (var index = 0; index < finalValues.Length && index < _slots.Length; index++)
            {
                if (IsHeld(_slots[index]))
                    continue;

                if (_slots[index] != null)
                {
                    DestroyDie(_slots[index]);
                    _slots[index] = null;
                }

                var lateral = (index - (finalValues.Length - 1) * 0.5f) * spacing;
                var position = toss.Origin + toss.Across * lateral;
                position.y = toss.Height;
                spawns[index] = position;
                var launchRotation = UnityEngine.Random.rotation;
                var die = Instantiate(dicePrefab, position, launchRotation);
                die.transform.localScale = Vector3.one * DiceWorldScale;
                ProceduralDiceVisual.EnsureOn(die);

                var selectable = die.GetComponent<DieSelectable>();
                if (selectable == null)
                    selectable = die.AddComponent<DieSelectable>();
                selectable.dieIndex = index;
                selectable.isSelected = false;

                var rb = die.GetComponent<Rigidbody>();
                if (rb != null)
                {
                        rb.mass = 1f;
                        rb.linearDamping = 0.05f;
                        rb.angularDamping = 0.12f;
                        rb.isKinematic = false;
                    rb.useGravity = true;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.rotation = launchRotation;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
                    rb.WakeUp();
                }

                ApplyDieSurface(die.GetComponent<BoxCollider>());
                EnsureFaceDigits(die);
                if (rb != null)
                    ThrowDie(rb);

                bodies[index] = rb;
                _slots[index] = die;
            }

            var thrown = false;
            for (var index = 0; index < bodies.Length; index++)
            {
                if (bodies[index] != null)
                    thrown = true;
            }

            if (!thrown)
                return;

            var track = RecordThrow(bodies);
            var best = track;
            var bestPenalty = LandingPenalty(track, bodies);
            for (var attempt = 1; attempt < 8 && bestPenalty > 0; attempt++)
            {
                Relaunch(bodies, spawns);
                track = RecordThrow(bodies);
                var penalty = LandingPenalty(track, bodies);
                if (penalty >= bestPenalty)
                    continue;

                best = track;
                bestPenalty = penalty;
            }

            BakeLandedFaces(best, bodies, finalValues);
            PoseAtSample(bodies, best, 0f);
            await PlayThrow(bodies, best, generation);
        }

        private void Relaunch(Rigidbody[] bodies, Vector3[] spawns)
        {
            for (var index = 0; index < bodies.Length; index++)
            {
                var body = bodies[index];
                if (body == null)
                    continue;

                var spin = UnityEngine.Random.rotation;
                body.isKinematic = false;
                body.useGravity = true;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = spawns[index];
                body.rotation = spin;
                body.WakeUp();
                ThrowDie(body);
            }
        }

        private int LandingPenalty(ThrowTrack track, Rigidbody[] bodies)
        {
            if (track.Count <= 1)
                return 1000;

            var last = track.Count - 1;
            var restY = TableRestY();
            var minDistance = DiceWorldScale * 1.02f;
            var penalty = 0;
            for (var index = 0; index < bodies.Length; index++)
            {
                if (bodies[index] == null || track.Positions[index] == null)
                    continue;

                var point = track.Positions[index][last];
                if (point.y > restY + DiceWorldScale * 0.2f)
                    penalty += 30;

                if (UpFaceAlignment(track.Rotations[index][last]) < 0.92f)
                    penalty += 15;

                if (_board != null && !_board.KeepsDie(point, DiceWorldScale * 0.45f))
                    penalty += 200;

                for (var other = index + 1; other < bodies.Length; other++)
                {
                    if (bodies[other] == null || track.Positions[other] == null)
                        continue;

                    if (HorizontalDistance(point, track.Positions[other][last]) < minDistance)
                        penalty += 25;
                }

                for (var slot = 0; slot < _slots.Length; slot++)
                {
                    if (!IsHeld(_slots[slot]))
                        continue;

                    if (HorizontalDistance(point, _slots[slot].transform.position) < minDistance)
                        penalty += 25;
                }
            }

            return penalty;
        }

        private static float UpFaceAlignment(Quaternion rotation)
        {
            var best = -1f;
            for (var value = 1; value <= 6; value++)
            {
                var dot = Vector3.Dot(rotation * LocalFace(value), Vector3.up);
                if (dot > best)
                    best = dot;
            }

            return best;
        }

        private Vector3 ThrowOrigin(float height)
        {
            var origin = _hasLanding ? _landingPoint : spawnPoint != null ? spawnPoint.position : Vector3.zero;
            if (_board != null && _board.TryPitCenter(out var pit))
            {
                origin.x = pit.x;
                origin.z = pit.z;
            }

            origin.y = height;
            return origin;
        }

        private struct KeptDie
        {
            public int Value;
            public int Index;
            public DieSelectable Selectable;
        }

        private void AssignShelfPoints()
        {
            ArrangeKeptDice();
        }

        private void ArrangeKeptDice()
        {
            if (_board == null)
                return;

            var count = 0;
            for (var index = 0; index < _slots.Length; index++)
            {
                var die = _slots[index];
                if (die == null || !IsHeld(die))
                    continue;

                var selectable = die.GetComponent<DieSelectable>();
                if (selectable == null)
                    continue;

                _keptOrder[count] = new KeptDie
                {
                    Value = UpFaceValue(die.transform.rotation),
                    Index = index,
                    Selectable = selectable
                };
                count++;
            }

            for (var index = 1; index < count; index++)
            {
                var current = _keptOrder[index];
                var place = index;
                while (place > 0 && KeptBefore(current, _keptOrder[place - 1]))
                {
                    _keptOrder[place] = _keptOrder[place - 1];
                    place--;
                }

                _keptOrder[place] = current;
            }

            for (var slot = 0; slot < count; slot++)
            {
                if (!_board.TryKeepSlot(slot, out var point))
                    continue;

                // Slot Y is the raised pedestal surface; rest full-size die center on top of it.
                point.y += DiceWorldScale * 0.5f;
                _keptOrder[slot].Selectable.SetShelfPoint(point);
            }
        }

        private static bool KeptBefore(KeptDie left, KeptDie right)
        {
            if (left.Value != right.Value)
                return left.Value < right.Value;

            return left.Index < right.Index;
        }

        private float TableRestY()
        {
            return TableRestYForScale(DiceWorldScale);
        }

        private float TableRestYForScale(float scale)
        {
            return (tableCollider != null ? tableCollider.bounds.max.y : 0f) + scale * 0.5f;
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            var delta = left - right;
            delta.y = 0f;
            return delta.magnitude;
        }

        private ThrowTrack RecordThrow(Rigidbody[] bodies)
        {
            var step = SimStep;
            var maxSteps = Mathf.Clamp(Mathf.CeilToInt(MaxSimSeconds / step), 40, 200);
            var positions = new Vector3[bodies.Length][];
            var rotations = new Quaternion[bodies.Length][];
            for (var index = 0; index < bodies.Length; index++)
            {
                var body = bodies[index];
                if (body == null)
                    continue;

                positions[index] = new Vector3[maxSteps + 1];
                rotations[index] = new Quaternion[maxSteps + 1];
                positions[index][0] = body.position;
                rotations[index][0] = body.rotation;
            }

            var previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            var count = 1;
            var calmSteps = 0;
            try
            {
                Physics.SyncTransforms();
                for (var stepIndex = 0; stepIndex < maxSteps; stepIndex++)
                {
                    Physics.Simulate(step);
                    var resting = true;
                    var restY = TableRestY();
                    for (var index = 0; index < bodies.Length; index++)
                    {
                        var body = bodies[index];
                        if (body == null)
                            continue;

                        positions[index][count] = body.position;
                        rotations[index][count] = body.rotation;
                        if (!DiceRoller.IsSettled(body, Mathf.Sqrt(RestLinearSqr), Mathf.Sqrt(RestAngularSqr)))
                            resting = false;
                        if (body.position.y > restY + DiceWorldScale * 0.28f)
                            resting = false;
                    }

                    count++;
                    if (resting)
                    {
                        calmSteps++;
                        if (calmSteps >= 12)
                            break;
                    }
                    else
                    {
                        calmSteps = 0;
                    }
                }
            }
            finally
            {
                Physics.simulationMode = previousMode;
            }

            return new ThrowTrack
            {
                Positions = positions,
                Rotations = rotations,
                Count = count,
                Step = step
            };
        }

        private static void BakeLandedFaces(ThrowTrack track, Rigidbody[] bodies, int[] finalValues)
        {
            if (track.Count <= 0)
                return;

            var last = track.Count - 1;
            for (var index = 0; index < bodies.Length; index++)
            {
                if (bodies[index] == null || track.Rotations[index] == null)
                    continue;

                var desired = finalValues[index];
                var landed = UpFaceValue(track.Rotations[index][last]);
                var faceShift = Quaternion.FromToRotation(LocalFace(desired), LocalFace(landed));
                for (var sample = 0; sample < track.Count; sample++)
                    track.Rotations[index][sample] = track.Rotations[index][sample] * faceShift;
            }
        }

        private async Task PlayThrow(Rigidbody[] bodies, ThrowTrack track, int generation)
        {
            if (track.Count <= 1)
            {
                SettleSelectable(bodies, track, track.Count - 1);
                AssignShelfPoints();
                return;
            }

            var done = new TaskCompletionSource<bool>();
            StartCoroutine(PlayThrowRoutine(bodies, track, generation, done));
            await done.Task;
        }

        private IEnumerator PlayThrowRoutine(Rigidbody[] bodies, ThrowTrack track, int generation, TaskCompletionSource<bool> done)
        {
            var duration = (track.Count - 1) * track.Step;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                if (this == null || generation != _generation)
                {
                    done.TrySetResult(false);
                    yield break;
                }

                PoseAtSample(bodies, track, elapsed / track.Step);
                yield return null;
                elapsed += Mathf.Min(Time.deltaTime, PlaybackStepCap);
            }

            if (this != null && generation == _generation)
            {
                SettleSelectable(bodies, track, track.Count - 1);
                AssignShelfPoints();
            }

            done.TrySetResult(true);
        }

        private static void PoseAtSample(Rigidbody[] bodies, ThrowTrack track, float sample)
        {
            var last = track.Count - 1;
            if (last < 0)
                return;

            var clamped = Mathf.Clamp(sample, 0f, last);
            var index0 = Mathf.FloorToInt(clamped);
            var index1 = Mathf.Min(index0 + 1, last);
            var blend = clamped - index0;

            for (var index = 0; index < bodies.Length; index++)
            {
                var body = bodies[index];
                if (body == null || track.Positions[index] == null)
                    continue;

                if (!body.isKinematic || body.interpolation != RigidbodyInterpolation.None)
                {
                    body.interpolation = RigidbodyInterpolation.None;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.useGravity = false;
                    body.isKinematic = true;
                }

                body.position = Vector3.Lerp(track.Positions[index][index0], track.Positions[index][index1], blend);
                body.rotation = Quaternion.Slerp(track.Rotations[index][index0], track.Rotations[index][index1], blend);
            }
        }

        private static void SettleSelectable(Rigidbody[] bodies, ThrowTrack track, int sample)
        {
            if (track.Count <= 0)
                return;

            var indexSample = Mathf.Clamp(sample, 0, track.Count - 1);
            for (var index = 0; index < bodies.Length; index++)
            {
                var body = bodies[index];
                if (body == null || track.Positions[index] == null)
                    continue;

                var rest = track.Positions[index][indexSample];
                body.position = rest;
                body.rotation = track.Rotations[index][indexSample];
                var selectable = body.GetComponent<DieSelectable>();
                if (selectable != null)
                    selectable.Arm(rest);
            }
        }

        private static int UpFaceValue(Quaternion rotation)
        {
            return DiceRoller.ReadTopFace(rotation);
        }

        private struct ThrowTrack
        {
            public Vector3[][] Positions;
            public Quaternion[][] Rotations;
            public int Count;
            public float Step;
        }

        private static bool IsHeld(GameObject die)
        {
            if (die == null)
                return false;

            var selectable = die.GetComponent<DieSelectable>();
            return selectable != null && selectable.isSelected;
        }

        private void ReleaseUnheld()
        {
            for (var index = 0; index < _slots.Length; index++)
            {
                if (_slots[index] == null || IsHeld(_slots[index]))
                    continue;

                DestroyDie(_slots[index]);
                _slots[index] = null;
            }
        }

        private struct TossLine
        {
            public Vector3 Origin;
            public Vector3 Across;
            public float Height;
        }

        private TossLine BuildToss()
        {
            var height = TableRestY() + 0.62f;
            var across = Vector3.right;
            var origin = new Vector3(0f, height, 0.4f);
            if (_board != null && _board.TryPitArea(out var min, out var max))
            {
                var midX = (min.x + max.x) * 0.5f;
                origin = new Vector3(midX, height, max.z - 0.14f);
                across = Vector3.right;
            }
            else if (spawnPoint != null)
            {
                across = spawnPoint.right;
                across.y = 0f;
                if (across.sqrMagnitude < 0.0001f)
                    across = Vector3.right;
                across.Normalize();
                origin = spawnPoint.position;
                origin.y = height;
            }

            return new TossLine
            {
                Origin = origin,
                Across = across,
                Height = height
            };
        }

        private void ThrowDie(Rigidbody rb)
        {
            var config = YahtzeeConfig.Current;
            var target = rb.position;
            if (_board != null && _board.TryPitCenter(out var pit))
                target = pit;
            else
            {
                var tray = UnityEngine.Object.FindAnyObjectByType<ProceduralDiceTray>();
                if (tray != null && tray.TryGetCenter(out var trayCenter))
                    target = trayCenter;
            }

            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.12f;
            var speed = Mathf.Clamp(config.ThrowForce * 0.82f, 3.6f, 6.4f) * UnityEngine.Random.Range(0.88f, 1.12f);
            var torque = Mathf.Clamp(config.TorqueForce * 4.4f, 13f, 24f) * UnityEngine.Random.Range(0.85f, 1.15f);
            DiceRoller.ThrowToward(rb, target, speed, torque);
        }

        private void ApplyTableSurface()
        {
            if (tableCollider == null)
            {
                var table = GameObject.Find("Table");
                if (table != null)
                    tableCollider = table.GetComponent<Collider>();
            }

            ApplyPhysicMaterial(tableCollider);
        }

        private void ApplyPhysicMaterial(Collider collider)
        {
            if (collider == null)
                return;

            if (_surface == null)
            {
                _surface = new PhysicsMaterial("YahtzeeFelt")
                {
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                    frictionCombine = PhysicsMaterialCombine.Maximum
                };
            }

            _surface.bounciness = 0.14f;
            _surface.dynamicFriction = 0.9f;
            _surface.staticFriction = 0.92f;
            collider.sharedMaterial = _surface;
        }

        private void ApplyDieSurface(Collider collider)
        {
            if (collider == null)
                return;

            if (_dieSurface == null)
            {
                _dieSurface = new PhysicsMaterial("YahtzeeDie")
                {
                    bounceCombine = PhysicsMaterialCombine.Maximum,
                    frictionCombine = PhysicsMaterialCombine.Average
                };
            }

            _dieSurface.bounciness = 0.04f;
            _dieSurface.dynamicFriction = 0.42f;
            _dieSurface.staticFriction = 0.48f;
            collider.sharedMaterial = _dieSurface;
        }

        private void EnsureFaceDigits(GameObject die)
        {
            // ProceduralDiceVisual owns closed mesh + neon pips; legacy Face_* overlays break opacity/scale.
            if (die != null && die.GetComponent<ProceduralDiceVisual>() != null)
                return;

            var bodyRenderer = die.GetComponent<Renderer>();
            if (bodyRenderer != null)
                bodyRenderer.sharedMaterial = DieBodyMaterial();

            if (die.transform.Find("Face_1") != null)
                return;

            CreateFace(die.transform, "Face_1", 1, Vector3.up, Vector3.forward);
            CreateFace(die.transform, "Face_6", 6, Vector3.down, Vector3.back);
            CreateFace(die.transform, "Face_2", 2, Vector3.forward, Vector3.up);
            CreateFace(die.transform, "Face_5", 5, Vector3.back, Vector3.up);
            CreateFace(die.transform, "Face_3", 3, Vector3.right, Vector3.up);
            CreateFace(die.transform, "Face_4", 4, Vector3.left, Vector3.up);
        }

        private void CreateFace(Transform die, string name, int value, Vector3 localOutward, Vector3 localUp)
        {
            var face = new GameObject(name);
            face.transform.SetParent(die, false);
            face.transform.localPosition = localOutward * FaceOffset;
            face.transform.localRotation = Quaternion.LookRotation(-localOutward, localUp);

            var layout = PipLayouts[value];
            const float spread = 0.28f;
            const float diameter = 0.15f;
            for (var index = 0; index < layout.Length; index++)
            {
                var pip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pip.name = "Pip";
                var pipCollider = pip.GetComponent<Collider>();
                if (pipCollider != null)
                    DestroyImmediate(pipCollider);

                pip.transform.SetParent(face.transform, false);
                var spot = layout[index];
                pip.transform.localPosition = new Vector3(spot.x * spread, spot.y * spread, -0.004f);
                pip.transform.localScale = new Vector3(diameter, diameter, 0.01f);
                var pipRenderer = pip.GetComponent<Renderer>();
                pipRenderer.sharedMaterial = PipMaterial();
                pipRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pipRenderer.receiveShadows = false;
            }
        }

        private static Material DieBodyMaterial()
        {
            if (_dieBodyMaterial == null)
                _dieBodyMaterial = CreateLitMaterial(new Color(0.97f, 0.96f, 0.93f, 1f), 0.38f);

            return _dieBodyMaterial;
        }

        private static Material PipMaterial()
        {
            if (_pipMaterial == null)
                _pipMaterial = CreateUnlitMaterial(new Color(0.1f, 0.09f, 0.08f, 1f));

            return _pipMaterial;
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var material = new Material(shader);
            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            return material;
        }

        private static Material CreateLitMaterial(Color color, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var material = new Material(shader);
            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);

            return material;
        }

        private void OnDestroy()
        {
            DieSelectable.SelectionChanged -= ArrangeKeptDice;
            if (_active == this)
                _active = null;
            _generation++;
            ClearDice();
        }

        public static Quaternion Straighten(Quaternion rotation)
        {
            var upFace = UpFaceValue(rotation);
            var upright = Quaternion.FromToRotation(LocalFace(upFace), Vector3.up);
            var towardCamera = Vector3.back;
            var bestAxis = towardCamera;
            var bestDot = -2f;
            var opposite = 7 - upFace;
            for (var value = 1; value <= 6; value++)
            {
                if (value == upFace || value == opposite)
                    continue;

                var world = upright * LocalFace(value);
                world.y = 0f;
                if (world.sqrMagnitude < 0.0001f)
                    continue;

                world.Normalize();
                var dot = Vector3.Dot(world, towardCamera);
                if (dot <= bestDot)
                    continue;

                bestDot = dot;
                bestAxis = world;
            }

            return Quaternion.FromToRotation(bestAxis, towardCamera) * upright;
        }

        public static bool HasBoard => _active != null && _active._board != null;

        public static void LetDieCrossShelf(Collider die)
        {
            if (_active == null || _active._board == null)
                return;

            _active._board.LetDieCrossShelf(die);
        }

        public static Vector3 OpenPit(DieSelectable self, Vector3 home)
        {
            if (_active == null || self == null)
                return home;

            return _active.ChooseOpenPit(self, home);
        }

        private Vector3 ChooseOpenPit(DieSelectable self, Vector3 home)
        {
            home.y = self.PitRestHeight;
            if (PitIsClear(self, home))
                return home;

            if (_board == null || !_board.TryPitArea(out var min, out var max))
                return home;

            var pad = DiceWorldScale * 0.55f;
            var x0 = min.x + pad;
            var x1 = max.x - pad;
            var z0 = min.z + pad;
            var z1 = max.z - pad;
            if (x1 < x0 || z1 < z0)
                return home;

            var step = DiceWorldScale * 1.05f;
            var best = home;
            var bestScore = float.MaxValue;
            var found = false;
            for (var z = z0; z <= z1 + 0.001f; z += step)
            {
                for (var x = x0; x <= x1 + 0.001f; x += step)
                {
                    var candidate = new Vector3(x, home.y, z);
                    if (!PitIsClear(self, candidate))
                        continue;

                    var score = (candidate - home).sqrMagnitude;
                    if (score >= bestScore)
                        continue;

                    bestScore = score;
                    best = candidate;
                    found = true;
                }
            }

            if (found)
                return best;

            var room = 0f;
            for (var z = z0; z <= z1 + 0.001f; z += step)
            {
                for (var x = x0; x <= x1 + 0.001f; x += step)
                {
                    var candidate = new Vector3(x, home.y, z);
                    var clearance = PitClearance(self, candidate);
                    if (clearance <= room)
                        continue;

                    room = clearance;
                    best = candidate;
                }
            }

            return best;
        }

        private bool PitIsClear(DieSelectable self, Vector3 point)
        {
            var min = DiceWorldScale * 1.12f;
            return PitClearance(self, point) >= min;
        }

        private float PitClearance(DieSelectable self, Vector3 point)
        {
            var nearest = float.MaxValue;
            for (var index = 0; index < _slots.Length; index++)
            {
                var die = _slots[index];
                if (die == null)
                    continue;

                var selectable = die.GetComponent<DieSelectable>();
                if (selectable == null || selectable == self)
                    continue;

                var delta = selectable.ReservedPoint - point;
                delta.y = 0f;
                var distance = delta.magnitude;
                if (distance < nearest)
                    nearest = distance;
            }

            return nearest;
        }

        private static Quaternion GetRotationForValue(int value)
        {
            return Quaternion.FromToRotation(LocalFace(value), Vector3.up);
        }

        private static Vector3 LocalFace(int value)
        {
            if (value < 1 || value > 6)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Грань кубика должна быть от 1 до 6.");
            return DiceRoller.LocalFaceNormals[value];
        }

        private void ClearDice()
        {
            for (var index = 0; index < _slots.Length; index++)
            {
                if (_slots[index] == null)
                    continue;

                DestroyDie(_slots[index]);
                _slots[index] = null;
            }
        }

        private static void DestroyDie(GameObject die)
        {
            if (die == null)
                return;

            var body = die.GetComponent<Rigidbody>();
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.detectCollisions = false;
            }

            Destroy(die);
        }
    }
}
