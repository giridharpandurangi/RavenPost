using Jotunn.Managers;
using UnityEngine;

namespace RavenPost
{
    /// <summary>
    /// Purely visual: a bird model takes off from the sending perch, or lands on the receiving one.
    /// Built from a vanilla bird prefab with all game logic stripped, so it never creates world data.
    /// </summary>
    internal class RavenFlight : MonoBehaviour
    {
        public const float LandingSeconds = 6f;

        private const float TakeOffSeconds = 12f;
        private const float CruiseSpeed = 5f;
        private const float ClimbSpeed = 1.6f;
        private const float CircleSeconds = 3f;
        private const float FadeSeconds = 1.5f;
        private const float LandingDistance = 30f;
        private const float LandingHeight = 14f;
        private const float PerchedSeconds = 1.5f;

        // First one found is used. Names are checked at load and logged.
        private static readonly string[] s_candidatePrefabs = { "Crow", "Raven", "Seagal" };

        private static GameObject s_template;
        private static bool s_warned;

        /// <summary>Stripped, inactive bird model; null if no bird prefab exists.</summary>
        public static GameObject Template
        {
            get
            {
                Prepare();
                return s_template;
            }
        }

        private enum Mode { TakeOff, Land }

        private Mode _mode;
        private Vector3 _origin;
        private Vector3 _target;
        private Vector3 _heading;
        private float _duration;
        private float _age;
        private Vector3 _baseScale;

        public static void Prepare()
        {
            if (s_template)
            {
                return;
            }

            GameObject source = null;
            foreach (var name in s_candidatePrefabs)
            {
                source = PrefabManager.Instance.GetPrefab(name);
                if (source)
                {
                    Plugin.Log.LogInfo($"Raven visual uses prefab '{name}'");
                    break;
                }
            }
            if (!source)
            {
                if (!s_warned)
                {
                    s_warned = true;
                    Plugin.Log.LogWarning("No bird prefab found; ravens will be invisible.");
                }
                return;
            }

            // Instantiate under an inactive parent so none of the prefab's Awake() methods run.
            var holder = new GameObject("RavenPost_Templates");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            s_template = Object.Instantiate(source, holder.transform);
            s_template.name = "RavenPost_Raven";

            foreach (var behaviour in s_template.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Object.DestroyImmediate(behaviour);
            }
            foreach (var collider in s_template.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }
            foreach (var body in s_template.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.DestroyImmediate(body);
            }
        }

        private static RavenFlight Spawn(Vector3 position, Vector3 facing)
        {
            if (!s_template)
            {
                Prepare();
                if (!s_template)
                {
                    return null;
                }
            }
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.01f)
            {
                facing = Vector3.forward;
            }
            var bird = Object.Instantiate(s_template, position, Quaternion.LookRotation(facing.normalized));
            bird.SetActive(true);
            var flight = bird.AddComponent<RavenFlight>();
            flight._baseScale = bird.transform.localScale;
            return flight;
        }

        /// <summary>Circles up above the perch, then glides off toward the destination and fades out.</summary>
        public static void Launch(Vector3 from, Vector3 to)
        {
            var heading = to - from;
            heading.y = 0f;
            heading = heading.sqrMagnitude < 0.01f ? Vector3.forward : heading.normalized;

            var flight = Spawn(from, heading);
            if (!flight)
            {
                return;
            }
            flight._mode = Mode.TakeOff;
            flight._origin = from;
            flight._heading = heading;
            flight._duration = TakeOffSeconds;
        }

        /// <summary>Glides in from the sender's direction and touches down on the perch after <paramref name="seconds"/>.</summary>
        public static void Land(Vector3 perch, Vector3 senderPos, float seconds)
        {
            var fromDir = senderPos - perch;
            fromDir.y = 0f;
            if (senderPos == Vector3.zero || fromDir.sqrMagnitude < 0.01f)
            {
                fromDir = Random.insideUnitSphere;
                fromDir.y = 0f;
            }
            fromDir = fromDir.sqrMagnitude < 0.01f ? Vector3.back : fromDir.normalized;

            var start = perch + fromDir * LandingDistance + Vector3.up * LandingHeight;
            var flight = Spawn(start, -fromDir);
            if (!flight)
            {
                return;
            }
            flight._mode = Mode.Land;
            flight._origin = start;
            flight._target = perch;
            flight._heading = -fromDir;
            flight._duration = Mathf.Max(1f, seconds);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_mode == Mode.TakeOff)
            {
                UpdateTakeOff();
            }
            else
            {
                UpdateLanding();
            }
        }

        private void UpdateTakeOff()
        {
            Vector3 pos;
            Vector3 facing;
            if (_age < CircleSeconds)
            {
                // Spiral up over the perch once.
                float t = _age / CircleSeconds;
                float angle = t * Mathf.PI * 2f;
                float radius = Mathf.Lerp(0.5f, 3f, t);
                var offset = Quaternion.LookRotation(_heading) * new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle)) * radius;
                pos = _origin + offset + Vector3.up * (_age * ClimbSpeed * 1.5f);
                facing = Quaternion.LookRotation(_heading) * new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            else
            {
                // Then glide away toward the destination, still climbing gently.
                float cruise = _age - CircleSeconds;
                var circleEnd = _origin + Quaternion.LookRotation(_heading) * new Vector3(0f, 0f, -3f)
                                + Vector3.up * (CircleSeconds * ClimbSpeed * 1.5f);
                pos = circleEnd + _heading * (cruise * CruiseSpeed) + Vector3.up * (cruise * ClimbSpeed);
                facing = _heading;
            }

            transform.position = pos;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing), Time.deltaTime * 6f);
            FadeAndExpire(_duration);
        }

        private void UpdateLanding()
        {
            float t = Mathf.Clamp01(_age / _duration);
            // Ease out: fast at first, gentle as it reaches the perch.
            float eased = 1f - (1f - t) * (1f - t);
            var pos = Vector3.Lerp(_origin, _target, eased);
            pos.y += Mathf.Sin(t * Mathf.PI) * 2f; // slight arc
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(_heading);

            FadeAndExpire(_duration + PerchedSeconds);
        }

        private void FadeAndExpire(float lifetime)
        {
            float left = lifetime - _age;
            if (left <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            if (left < FadeSeconds)
            {
                transform.localScale = _baseScale * (left / FadeSeconds);
            }
        }
    }
}
