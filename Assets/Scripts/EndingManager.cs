// FILE: EndingManager.cs
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VRExhibitTemplate
{
    public class EndingManager : MonoBehaviour
    {
        [Header("SKYBOX")]
        public Material skyboxMaterialOverride;

        [Header("BGM")]
        public AudioClip bgmClip;
        [Range(0f, 1f)] public float bgmVolume = 1f;
        [Tooltip("BGM을 다음 씬까지 유지(DontDestroyOnLoad)할지 여부")]
        public bool persistBgmAcrossScenes = true;
        [Tooltip("이미 글로벌 BGM이 있으면 교체할지 유지할지")]
        public bool replaceExistingBgm = true;
        [NonSerialized] public AudioSource bgmSource; // 로컬 BGM 용

        [Header("FADE IN")]
        public bool fadeInOnStart = true;
        public float fadeInDelay = 0.05f;
        [NonSerialized]
        public bool fadeOutOnExit = false;

        [Header("SCENE DELAY TIME")]
        [Tooltip("엔딩 씬에서 머무를 시간(초)")]
        public float staySeconds = 10f;
        [Tooltip("종료 후 이동할 씬 이름")]
        [NonSerialized] public string nextSceneName = SceneNames.Intro;

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        [Header("ENTER PLAY")]
        [Tooltip("에디터(Play Mode)에서 Enter로 종료(인트로로 이동)")]
        public bool enableEditorEnterToExit = true;

        private InputAction _editorExitAction;
#endif

        private static AudioSource s_globalBGM;
        private bool _exitingGuard = false;

        private void Awake()
        {
            ApplySkyboxIfProvided();
            SetupAndPlayBGM();

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            SetupEditorHotkey();
#endif
        }

        private async void Start()
        {
            // (옵션) 페이드 인
            if (fadeInOnStart && FadeManager.Instance != null)
            {
                if (fadeInDelay > 0f) await UniTask.Delay(TimeSpan.FromSeconds(fadeInDelay));
                else await UniTask.Yield();
                await FadeManager.Instance.FadeInAsync();
            }

            // 지정 시간 대기 후 자동 종료
            if (staySeconds > 0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(staySeconds));
                await ExitToIntroAsync();
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 외부(UI 버튼 등)에서 직접 호출 가능
        public void OnExit()
        {
            _ = ExitToIntroAsync();
        }

        private async UniTask ExitToIntroAsync()
        {
            if (_exitingGuard) return;
            _exitingGuard = true;

            if (fadeOutOnExit && FadeManager.Instance != null)
                await FadeManager.Instance.FadeOutAsync();

            if (SceneLoader.Instance != null)
                SceneLoader.Instance.LoadScene(nextSceneName);
            else
                SceneManager.LoadScene(nextSceneName);
        }

        // ─────────────────────────────────────────────────────────────
        private void ApplySkyboxIfProvided()
        {
            if (skyboxMaterialOverride != null)
                RenderSettings.skybox = skyboxMaterialOverride;
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
                        var go = new GameObject("BGM (Ending)");
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

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        private void SetupEditorHotkey()
        {
            if (!enableEditorEnterToExit) return;

            _editorExitAction = new InputAction("EditorExit", InputActionType.Button, "<Keyboard>/enter");
            _editorExitAction.AddBinding("<Keyboard>/numpadEnter");
            _editorExitAction.performed += OnEditorExitPerformed; // ✅ 메서드로 연결
            _editorExitAction.Enable();
        }

        // ✅ 콜백 시그니처는 CallbackContext, 내부에서 UniTask fire-and-forget 호출
        private void OnEditorExitPerformed(InputAction.CallbackContext ctx)
        {
            _ = ExitToIntroAsync();
        }
#endif

        private void OnDestroy()
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            if (_editorExitAction != null)
            {
                _editorExitAction.performed -= OnEditorExitPerformed; // ✅ 해제
                _editorExitAction.Disable();
                _editorExitAction.Dispose();
                _editorExitAction = null;
            }
#endif
        }

        // (선택) 다른 씬에서 BGM 끄고 싶을 때 호출
        public static void StopGlobalBgm()
        {
            if (s_globalBGM == null) return;
            s_globalBGM.Stop();
        }
    }
}
