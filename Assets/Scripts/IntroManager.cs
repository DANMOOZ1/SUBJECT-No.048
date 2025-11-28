// FILE: IntroManager.cs
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if TMP_PRESENT || UNITY_2019_4_OR_NEWER
using TMPro;
#endif

namespace VRExhibitTemplate
{
    public class IntroManager : MonoBehaviour
    {
        [Header("SKYBOX")]
        public Material skyboxMaterialOverride;

#if TMP_PRESENT || UNITY_2019_4_OR_NEWER
        [Header("TITLE")]
        public TMP_Text titleTMP;
#endif

        [Header("BGM")]
        public AudioClip bgmClip;
        [Range(0f, 1f)] public float bgmVolume = 1f;
        [Tooltip("BGM을 다음 씬까지 유지(DontDestroyOnLoad)할지 여부")]
        public bool persistBgmAcrossScenes = true;
        [Tooltip("이미 글로벌 BGM이 있으면 교체할지 유지할지")]
        public bool replaceExistingBgm = true;
        [NonSerialized] public AudioSource bgmSource;

        [Header("NEXT SCENE")]
        [NonSerialized] public string nextSceneName = SceneNames.Main;

        [Header("FADE IN")]
        public bool fadeInOnStart = true;
        public float fadeInDelay = 0.05f;

        // ─────────────────────────────────────────────────────────────
        [Header("START BUTTON")]
        public GameObject highlightTarget;
        public bool addOutlineIfMissing = true;
        public Color outlineColor = new Color(0.2f, 1f, 1f, 1f);
        [Range(0f, 10f)] public float outlineWidth = 4f;
        public Outline.Mode outlineMode = Outline.Mode.OutlineVisible;

        [Header("START BUTTON SOUND")]
        public AudioClip highlightSfx; [Range(0f, 1f)] public float highlightSfxVolume = 1f;
        public AudioClip startSfx; [Range(0f, 1f)] public float startSfxVolume = 1f;

        // ─────────────────────────────────────────────────────────────
        [Header("DEBUG MODE(Camera Chanage)")]
        [Tooltip("체크 시 OVR 리그 대신 메인 카메라를 활성화해서 시작")]
        public bool debugUseMainCamera = false;
        [Tooltip("OVR 리그 루트(비워두면 자동 탐색)")]
        public GameObject ovrRigRoot;
        [Tooltip("디버그용 메인 카메라(비워두면 Camera.main 사용)")]
        public Camera debugMainCamera;
        [Tooltip("HMD가 없으면 자동으로 메인 카메라 사용")]
        public bool autoUseMainCamIfNoHMD = true;
        [Tooltip("오디오 리스너 중복 방지(활성 카메라에만 AudioListener 유지)")]
        public bool ensureSingleAudioListener = true;

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        [Header("ENTER PLAY")]
        [Tooltip("에디터(Play Mode)에서 Enter로 OnStart 실행")]
        public bool enableEditorEnterToStart = true;

        private InputAction _editorStartAction;
        private bool _editorStartedGuard = false;   // 중복 실행 방지
#endif

        private Material _generatedSkyboxMat;
        private static AudioSource s_globalBGM;

        void Awake()
        {
            // (1) 카메라 모드 스위치
            SetupCameraMode();

            // (2) 나머지 초기화
            ApplySkyboxIfProvided();
            SetupAndPlayBGM();
            DisableOutlineIfAny();

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            SetupEditorEnterHotkey();
#endif
        }

        async void Start()
        {
            // 씬 시작 페이드 인
            if (fadeInOnStart && FadeManager.Instance != null)
            {
                if (fadeInDelay > 0f) await UniTask.Delay(TimeSpan.FromSeconds(fadeInDelay));
                else await UniTask.Yield();
                await FadeManager.Instance.FadeInAsync();
            }
            else
            {
                await UniTask.Yield();
            }

            // 페이드 완료 → 버튼 하이라이트 & 안내 SFX
            ActivateHighlight();
            PlayGuideSfxIfAny();
        }

        // ─────────────────────────────────────────────────────────────
        // Camera mode (OVR ↔ Main) 스위치
        private void SetupCameraMode()
        {
            // 자동 OVR/HMD 감지
            bool hmdPresent = false;
            if (autoUseMainCamIfNoHMD)
            {
                try { hmdPresent = OVRManager.isHmdPresent; } catch { hmdPresent = false; }
            }

            bool useMain = debugUseMainCamera || (autoUseMainCamIfNoHMD && !hmdPresent);

            // 대상 객체 찾기
            if (ovrRigRoot == null)
            {
                var rig = FindObjectOfType<OVRCameraRig>(true);
                if (rig != null) ovrRigRoot = rig.gameObject;
            }
            var cam = debugMainCamera != null ? debugMainCamera : Camera.main;

            // 활성화/비활성화
            if (useMain)
            {
                if (ovrRigRoot != null) ovrRigRoot.SetActive(false);
                if (cam != null) cam.gameObject.SetActive(true);
                EnsureSingleAudioListener(cam);
            }
            else
            {
                if (ovrRigRoot != null) ovrRigRoot.SetActive(true);
                // 메인 카메라는 켜져 있으면 끄는 편이 안전
                if (cam != null) cam.gameObject.SetActive(false);
                // OVR 쪽 AudioListener만 남기기
                var ovrListener = ovrRigRoot != null ? ovrRigRoot.GetComponentInChildren<AudioListener>(true) : null;
                EnsureSingleAudioListener(ovrListener != null ? ovrListener.GetComponent<Camera>() : null);
            }
        }

        private void EnsureSingleAudioListener(Camera preferredCamera)
        {
            if (!ensureSingleAudioListener) return;

            var listeners = FindObjectsOfType<AudioListener>(true);
            foreach (var al in listeners) al.enabled = false;

            if (preferredCamera != null)
            {
                var al = preferredCamera.GetComponent<AudioListener>();
                if (al == null) al = preferredCamera.gameObject.AddComponent<AudioListener>();
                al.enabled = true;
            }
            else
            {
                // fallback: 첫 AudioListener만 활성
                if (listeners.Length > 0) listeners[0].enabled = true;
            }
        }

        // ─────────────────────────────────────────────────────────────
        private void ApplySkyboxIfProvided()
        {
            if (skyboxMaterialOverride != null)
            {
                RenderSettings.skybox = skyboxMaterialOverride;
                return;
            }
        }

        private void SetupAndPlayBGM()
        {
            if (bgmClip == null) return;

            if (persistBgmAcrossScenes)
            {
                if (s_globalBGM == null)
                {
                    var go = new GameObject("BGM_Global");
                    s_globalBGM = go.AddComponent<AudioSource>();
                    s_globalBGM.playOnAwake = false;
                    s_globalBGM.loop = true;
                    s_globalBGM.spatialBlend = 0f;
                    DontDestroyOnLoad(go);
                }

                if (replaceExistingBgm || !s_globalBGM.isPlaying)
                {
                    s_globalBGM.clip = bgmClip;
                    s_globalBGM.volume = bgmVolume;
                    s_globalBGM.Play();
                }
            }
            else
            {
                var source = bgmSource;
                if (source == null)
                {
                    source = FindObjectOfType<AudioSource>();
                    if (source == null)
                    {
                        var go = new GameObject("BGM (Intro)");
                        source = go.AddComponent<AudioSource>();
                        source.playOnAwake = false;
                        source.loop = true;
                        source.spatialBlend = 0f;
                    }
                    bgmSource = source;
                }
                source.clip = bgmClip;
                source.volume = bgmVolume;
                if (!source.isPlaying) source.Play();
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Highlight / SFX helpers
        private void DisableOutlineIfAny()
        {
            if (highlightTarget == null) return;
            var outline = highlightTarget.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
        }

        private void ActivateHighlight()
        {
            if (highlightTarget == null) return;

            var outline = highlightTarget.GetComponent<Outline>();
            if (outline == null && addOutlineIfMissing)
                outline = highlightTarget.AddComponent<Outline>();

            if (outline != null)
            {
                outline.OutlineMode = outlineMode;
                outline.OutlineColor = outlineColor;
                outline.OutlineWidth = outlineWidth;
                outline.enabled = true;
            }
        }

        private void DeactivateHighlight()
        {
            if (highlightTarget == null) return;
            var outline = highlightTarget.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
        }

        private void PlayGuideSfxIfAny()
        {
            if (highlightSfx == null) return;

            if (VRExhibitTemplate.SfxManager.Instance != null)
                VRExhibitTemplate.SfxManager.Instance.PlaySFX(highlightSfx, highlightSfxVolume, 0f);
            else
            {
                var src = gameObject.GetComponent<AudioSource>();
                if (src == null) src = gameObject.AddComponent<AudioSource>();
                src.spatialBlend = 0f;
                src.PlayOneShot(highlightSfx, highlightSfxVolume);
            }
        }

        private void PlayClickSfxIfAny()
        {
            if (startSfx == null) return;

            if (VRExhibitTemplate.SfxManager.Instance != null)
                VRExhibitTemplate.SfxManager.Instance.PlaySFX(startSfx, startSfxVolume, 0f);
            else
            {
                var src = gameObject.GetComponent<AudioSource>();
                if (src == null) src = gameObject.AddComponent<AudioSource>();
                src.spatialBlend = 0f;
                src.PlayOneShot(startSfx, startSfxVolume);
            }
        }

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        private void SetupEditorEnterHotkey()
        {
            if (!enableEditorEnterToStart) return;

            _editorStartAction = new InputAction("EditorStart", InputActionType.Button, "<Keyboard>/enter");
            _editorStartAction.AddBinding("<Keyboard>/numpadEnter");
            _editorStartAction.performed += OnEditorStartPerformed;
            _editorStartAction.Enable();
        }

        private void OnEditorStartPerformed(InputAction.CallbackContext ctx)
        {
            if (_editorStartedGuard) return; // 한 번만
            _editorStartedGuard = true;
            OnStart();
        }
#endif

        // ─────────────────────────────────────────────────────────────
        public void OnStart()
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            _editorStartedGuard = true;
#endif
            DeactivateHighlight();
            PlayClickSfxIfAny();

            if (SceneLoader.Instance != null)
                SceneLoader.Instance.LoadScene(nextSceneName);
            else
                SceneManager.LoadScene(nextSceneName);
        }


        void OnDestroy()
        {
            if (_generatedSkyboxMat != null) Destroy(_generatedSkyboxMat);

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            if (_editorStartAction != null)
            {
                _editorStartAction.performed -= OnEditorStartPerformed;
                _editorStartAction.Disable();
                _editorStartAction.Dispose();
                _editorStartAction = null;
            }
#endif
        }


        public static void StopGlobalBgm()
        {
            if (s_globalBGM == null) return;
            s_globalBGM.Stop();
        }
    }
}
