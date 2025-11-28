// FILE: SfxManager.cs
using System.Collections.Generic;
using UnityEngine;

namespace VRExhibitTemplate
{
    public class SfxManager : MonoBehaviour
    {
        public static SfxManager Instance { get; private set; }

        [Header("풀링")]
        public int poolSize = 10;

        [Header("기본 볼륨")]
        [Range(0f, 1f)] public float sfxVolume = 1f;

        private readonly List<AudioSource> _pool = new List<AudioSource>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                BuildPool();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void BuildPool()
        {
            for (int i = 0; i < Mathf.Max(1, poolSize); i++)
            {
                var go = new GameObject($"SFX_{i:00}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f; // 2D 기본
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 1f;
                src.maxDistance = 25f;
                _pool.Add(src);
            }
        }

        private AudioSource GetFree()
        {
            foreach (var s in _pool) if (!s.isPlaying) return s;
            return _pool[0];
        }

        public void PlaySFX(AudioClip clip, float volume = 1f, float pitchJitter = 0f)
        {
            if (clip == null) return;
            var s = GetFree();
            s.spatialBlend = 0f;
            s.pitch = pitchJitter > 0f ? Random.Range(1f - pitchJitter, 1f + pitchJitter) : 1f;
            s.volume = sfxVolume * Mathf.Clamp01(volume);
            s.PlayOneShot(clip);
        }

        public void PlaySFXAt(AudioClip clip, Vector3 worldPos, float volume = 1f, float spatialBlend = 1f, float pitchJitter = 0f)
        {
            if (clip == null) return;
            var s = GetFree();
            s.transform.position = worldPos;
            s.spatialBlend = Mathf.Clamp01(spatialBlend);
            s.pitch = pitchJitter > 0f ? Random.Range(1f - pitchJitter, 1f + pitchJitter) : 1f;
            s.volume = sfxVolume * Mathf.Clamp01(volume);
            s.PlayOneShot(clip);
        }
    }
}
