using System.Collections.Generic;
using UnityEngine;

namespace Yahtzee.Audio
{
    public sealed class GameAudio : MonoBehaviour
    {
        private const string SfxKey = "yahtzee.settings.sfx";
        private const string MusicKey = "yahtzee.settings.music";

        public static GameAudio Instance { get; private set; }

        private AudioSource _sfx;
        private AudioSource _music;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

        public static bool SfxEnabled
        {
            get => PlayerPrefs.GetInt(SfxKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(SfxKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static bool MusicEnabled
        {
            get => PlayerPrefs.GetInt(MusicKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(MusicKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Ensure().ApplyMusicState();
            }
        }

        public static GameAudio Ensure()
        {
            if (Instance != null)
                return Instance;

            var existing = FindAnyObjectByType<GameAudio>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var host = new GameObject("GameAudio");
            DontDestroyOnLoad(host);
            return host.AddComponent<GameAudio>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f;
            _music = gameObject.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = true;
            _music.spatialBlend = 0f;
            _music.volume = 0.12f;
            ApplyMusicState();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public static void PlayUiClick()
        {
            Ensure().PlayBuilt("ui_click", () => BuildTone(740f, 0.04f, false, 0.55f), 0.18f);
        }

        public static void PlayDiceRoll()
        {
            Ensure().PlayBuilt("dice_clack", BuildDiceClacks, 0.34f);
        }

        public static void PlayScoreWrite()
        {
            Ensure().PlayBuilt("score", () => BuildTone(480f, 0.07f, false, 0.7f), 0.22f);
        }

        public static void PlayYahtzee()
        {
            Ensure().PlayBuilt("yahtzee", () => BuildChord(new[] { 523f, 659f, 784f }, 0.28f), 0.3f);
        }

        public static void PlayVictory()
        {
            Ensure().PlayBuilt("victory", () => BuildChord(new[] { 392f, 523f, 659f, 784f }, 0.42f), 0.28f);
        }

        public static void PlayDefeat()
        {
            Ensure().PlayBuilt("defeat", () => BuildTone(196f, 0.28f, true, 0.65f), 0.2f);
        }

        private void ApplyMusicState()
        {
            if (_music == null)
                return;

            if (!MusicEnabled)
            {
                _music.Stop();
                return;
            }

            if (_music.clip == null)
                _music.clip = BuildAmbientLoop();

            if (!_music.isPlaying)
                _music.Play();
        }

        private void PlayBuilt(string key, System.Func<AudioClip> builder, float volume)
        {
            if (!SfxEnabled || _sfx == null)
                return;

            if (!_clips.TryGetValue(key, out var clip) || clip == null)
            {
                clip = builder();
                _clips[key] = clip;
            }

            _sfx.PlayOneShot(clip, volume);
        }

        private static AudioClip BuildAmbientLoop()
        {
            const int rate = 22050;
            const float seconds = 6f;
            var samples = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)rate;
                var pad = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.22f
                          + Mathf.Sin(2f * Mathf.PI * 82.5f * t) * 0.12f
                          + Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.08f;
                var breathe = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * (t / seconds));
                data[i] = pad * breathe * 0.35f;
            }

            var clip = AudioClip.Create("ambient", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildDiceClacks()
        {
            const int rate = 22050;
            const float seconds = 0.28f;
            var samples = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
            var data = new float[samples];
            var hits = new[] { 0.00f, 0.045f, 0.09f, 0.14f, 0.185f };
            var tones = new[] { 920f, 780f, 1040f, 860f, 720f };

            for (var h = 0; h < hits.Length; h++)
            {
                var start = Mathf.RoundToInt(hits[h] * rate);
                var length = Mathf.RoundToInt(0.028f * rate);
                var hz = tones[h];
                for (var i = 0; i < length; i++)
                {
                    var index = start + i;
                    if (index >= samples)
                        break;

                    var t = i / (float)rate;
                    var env = Mathf.Exp(-i / (rate * 0.012f));
                    var tick = Mathf.Sin(2f * Mathf.PI * hz * t) * env * 0.55f;
                    var grain = (Mathf.PerlinNoise(i * 0.37f, h * 12.7f) * 2f - 1f) * env * 0.18f;
                    data[index] += tick + grain;
                }
            }

            for (var i = 0; i < samples; i++)
                data[i] = Mathf.Clamp(data[i], -1f, 1f);

            var clip = AudioClip.Create("dice_clack", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildTone(float hz, float seconds, bool fall, float gain)
        {
            var rate = 22050;
            var samples = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)rate;
                var freq = fall ? hz * (1f - 0.45f * (i / (float)samples)) : hz;
                var envelope = Mathf.Exp(-i / (rate * seconds * 0.45f));
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * gain;
            }

            var clip = AudioClip.Create("tone", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildChord(float[] hz, float seconds)
        {
            var rate = 22050;
            var samples = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)rate;
                var envelope = Mathf.Clamp01(1.15f - i / (float)samples);
                var sample = 0f;
                for (var n = 0; n < hz.Length; n++)
                    sample += Mathf.Sin(2f * Mathf.PI * hz[n] * t);
                data[i] = sample / hz.Length * envelope * 0.7f;
            }

            var clip = AudioClip.Create("chord", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
