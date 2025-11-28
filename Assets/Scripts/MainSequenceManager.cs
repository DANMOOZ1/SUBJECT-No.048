// FILE: MainSequenceManager.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using Cysharp.Threading.Tasks;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using UnityEngine.Events;
using Oculus.Interaction;

namespace VRExhibitTemplate
{
    public class MainSequenceManager : MonoBehaviour
    {
        public static MainSequenceManager Instance { get; private set; }

        #region BGM 전환(크로스페이드)
        [Header("BGM")]
        [Tooltip("메인 씬에서 사용할 BGM. 비우면 기존 글로벌 BGM 유지")]
        public AudioClip mainBgm;
        [Range(0f, 1f)] public float mainBgmVolume = 1f;
        [Tooltip("인트로 BGM과 메인 BGM의 크로스페이드 시간(초)")]
        public float bgmCrossfadeSeconds = 1.5f;
        #endregion

        #region 페이드 옵션
        [Header("FADE IN")]
        public bool fadeInOnStart = true;
        public float fadeInDelay = 0.05f;
        [NonSerialized] public bool fadeOutOnExit = false;
        [Tooltip("페이드 인 완료 후 하이라이트 켜기")]
        public bool showOutlineAfterFadeIn = true;
        #endregion

        #region 디버그 모드 설정 (카메라 전환)
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
        #endregion

        #region 순서/인터랙션 정의
        [Header("INTERACTION OBJECTS")]
        public List<StepNode> nodes = new List<StepNode>(4);

        public enum InteractionMode { Touch, Grab }
        public enum StepType { Text, Image, Video, Sound }

        [Serializable]
        public class StepNode
        {
            [Header("ORDER")]
            [Tooltip("식별 대상 커스텀 네임 할당")]
            public string displayName = "Node";
            [Tooltip("낮은 숫자 → 먼저. 중복 없게 설정 권장")]
            public int orderIndex = 0;

            [Header("INTERACTION OBJECT")]
            public GameObject interactable;
            [Tooltip("하이라이트할 대상(비우면 interactable 사용)")]
            public GameObject highlightTarget;

            [Header("CONTROL MODE")]
            [Tooltip("Touch → WhenHover, Grab → WhenSelect 에 자동 바인딩")]
            public InteractionMode interactionMode = InteractionMode.Touch;

            [Header("ACTIVATE OBJECT TYPE")]
            public StepType stepType = StepType.Text;

            [Header("ACTIVATE OBJECT")]
            public GameObject textToActivate;
            public GameObject imageToActivate;
            [Tooltip("Text/Image 활성 유지 시간(초). 0이하면 즉시 다음 단계로")]
            public float activateDuration = 2f;
            public bool deactivateAfterDuration = false;

            [Header("VIDEO PLAYER")]
            public VideoPlayer videoPlayer;                   // 단일 VideoPlayer
            [Tooltip("비디오 단계에서 안내용으로 함께 사용할 수 있는 가이드 이미지 오브젝트(시작 시 항상 비활성화)")]
            public GameObject videoGuideImage;                // ★ 추가: 비디오 가이드 이미지
            [Tooltip("비디오 끝난 후 추가 대기 시간")]
            public float videoPostDelay = 0f;
            [Tooltip("비디오 재생 종료 후 VideoPlayer 오브젝트를 자동 비활성화")]
            public bool deactivateVideoOnEnd = false;

            [Header("사운드 연출")]
            public AudioClip soundClip;
            [Range(0f, 1f)] public float soundVolume = 1f;
            [Tooltip("사운드 끝난 후 추가 대기 시간")]
            public float soundPostDelay = 0f;

            [Header("다음 단계 유도(Outline & SFX) - 미설정 시 글로벌 기본 사용")]
            public Outline.Mode outlineMode = Outline.Mode.OutlineVisible;
            public Color outlineColor = new Color(0.2f, 1f, 1f, 1f);
            [Range(0f, 10f)] public float outlineWidth = 4f;
            public AudioClip guideSfx;
            [Range(0f, 1f)] public float guideSfxVolume = 1f;

            [Header("인터랙션 입력 SFX(터치/그랩 순간에 재생)")]
            public AudioClip interactionSfx;
            [Range(0f, 1f)] public float interactionSfxVolume = 1f;

            [Header("초기 비활성화 추가 목록(선택)")]
            [Tooltip("시작 시 함께 꺼둘 추가 오브젝트(사운드용 프리팹 등 필요시)")]
            public GameObject[] extraObjectsToDisableOnStart;

            public GameObject EffectiveHighlightTarget =>
                highlightTarget != null ? highlightTarget : interactable;

            // 자동 바인딩/해제 캐시
            [NonSerialized] public PointableUnityEventWrapper pointableWrapper;
            [NonSerialized] public UnityAction<PointerEvent> cachedListener;

            // 최초 1회만 발동/재입력 방지
            [NonSerialized] public bool triggered = false;
        }
        #endregion

        #region 글로벌 가이드(기본값)
        [Header("기본 가이드(Outline & SFX)")]
        public Outline.Mode defaultOutlineMode = Outline.Mode.OutlineVisible;
        public Color defaultOutlineColor = new Color(0.2f, 1f, 1f, 1f);
        [Range(0f, 10f)] public float defaultOutlineWidth = 4f;
        public AudioClip defaultGuideSfx;
        [Range(0f, 1f)] public float defaultGuideSfxVolume = 1f;
        #endregion

        #region 디버그
        [Header("디버그")]
        [Tooltip("체크 시 Enter로 ‘다음 단계’ 즉시 진행")]
        public bool debugAdvanceWithEnter = true;

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        private InputAction _editorNextAction;
#endif
        #endregion

        // state
        private List<StepNode> _ordered;
        private int _cursor = 0;
        private bool _waitingStep = false;
        private Coroutine _runningCo;
        private Outline _currentOutline;
        private bool _selfDestructing = false;

        [Header("다음 씬 이름 (모두 끝나면 이동)")]
        [NonSerialized] public string nextSceneName = SceneNames.Ending;
        [NonSerialized] public float autoProceedDelayAfterAll = 3f;

        [Header("라이프사이클")]
        [Tooltip("씬 전환 시 이 매니저를 즉시 파괴합니다.")]
        [NonSerialized] public bool destroyOnSceneChange = true;

        #region Unity lifecycle
        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) { Destroy(gameObject); return; }

            // 디버그 카메라 모드 스위치
            SetupCameraMode();

            if (nodes == null) nodes = new List<StepNode>();
            _ordered = nodes.Where(n => n != null).OrderBy(n => n.orderIndex).ToList();

            AutoBindPointables();
            InitializeAllNodeContentInactive();

            if (showOutlineAfterFadeIn) DisableCurrentOutline();
            else if (_ordered.Count > 0) HighlightCurrentNode();

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            if (debugAdvanceWithEnter)
            {
                _editorNextAction = new InputAction("EditorNext", InputActionType.Button, "<Keyboard>/enter");
                _editorNextAction.AddBinding("<Keyboard>/numpadEnter");
                _editorNextAction.performed += _ => ForceProceed();
                _editorNextAction.Enable();
            }
#endif
        }

        private async void Start()
        {
            StartCoroutine(CrossfadeToMainBgm());

            if (fadeInOnStart && FadeManager.Instance != null)
            {
                if (fadeInDelay > 0f) await UniTask.Delay(TimeSpan.FromSeconds(fadeInDelay));
                else await UniTask.Yield();
                await FadeManager.Instance.FadeInAsync();
            }

            if (showOutlineAfterFadeIn && _ordered.Count > 0)
                HighlightCurrentNode();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            AutoUnbindPointables();

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            if (_editorNextAction != null)
            {
                _editorNextAction.Disable();
                _editorNextAction.Dispose();
                _editorNextAction = null;
            }
#endif
            if (_currentOutline != null) _currentOutline.enabled = false;
        }
        #endregion

        #region 디버그 카메라 전환
        private void SetupCameraMode()
        {
            bool hmdPresent = false;
            if (autoUseMainCamIfNoHMD)
            {
                try { hmdPresent = OVRManager.isHmdPresent; } catch { hmdPresent = false; }
            }

            bool useMain = debugUseMainCamera || (autoUseMainCamIfNoHMD && !hmdPresent);

            if (ovrRigRoot == null)
            {
                var rig = FindObjectOfType<OVRCameraRig>(true);
                if (rig != null) ovrRigRoot = rig.gameObject;
            }
            var cam = debugMainCamera != null ? debugMainCamera : Camera.main;

            if (useMain)
            {
                if (ovrRigRoot != null) ovrRigRoot.SetActive(false);
                if (cam != null) cam.gameObject.SetActive(true);
                EnsureSingleAudioListener(cam);
            }
            else
            {
                if (ovrRigRoot != null) ovrRigRoot.SetActive(true);
                if (cam != null) cam.gameObject.SetActive(false);
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
                if (listeners.Length > 0) listeners[0].enabled = true;
            }
        }
        #endregion

        #region 초기 비활성화 로직
        private void InitializeAllNodeContentInactive()
        {
            foreach (var n in _ordered)
            {
                if (n == null) continue;

                if (n.textToActivate != null) n.textToActivate.SetActive(false);
                if (n.imageToActivate != null) n.imageToActivate.SetActive(false);

                // ★ 비디오 관련: VideoPlayer 오브젝트 비활성화
                if (n.videoPlayer != null)
                {
                    var vp = n.videoPlayer;
                    if (vp != null && vp.gameObject.activeSelf)
                        vp.gameObject.SetActive(false);
                }

                // ★ 추가: 비디오 가이드 이미지는 시작 시 항상 비활성화
                if (n.videoGuideImage != null && n.videoGuideImage.activeSelf)
                    n.videoGuideImage.SetActive(false);

                if (n.extraObjectsToDisableOnStart != null)
                {
                    foreach (var go in n.extraObjectsToDisableOnStart)
                    {
                        if (go != null && go.activeSelf)
                            go.SetActive(false);
                    }
                }
            }
        }
        #endregion

        #region Pointable 자동 바인딩 / 해제
        private void AutoBindPointables()
        {
            foreach (var node in _ordered)
            {
                if (node == null || node.interactable == null) continue;

                node.pointableWrapper = node.interactable.GetComponent<PointableUnityEventWrapper>();
                if (node.pointableWrapper == null)
                {
                    Debug.LogWarning($"[MainSequenceManager] '{node.displayName}'에 PointableUnityEventWrapper가 없습니다.");
                    continue;
                }

                node.cachedListener = (PointerEvent evt) =>
                {
                    if (node.triggered) return;
                    if (!(_cursor < _ordered.Count && _ordered[_cursor] == node)) return;

                    node.triggered = true;
                    DisableNodeInteraction(node);

                    PlayInteractionSfx(node);

                    if (_runningCo != null) StopCoroutine(_runningCo);
                    _runningCo = StartCoroutine(RunStep(node));
                };

                // Touch=WhenHover, Grab=WhenSelect
                if (node.interactionMode == InteractionMode.Touch)
                    node.pointableWrapper.WhenHover.AddListener(node.cachedListener);
                else
                    node.pointableWrapper.WhenSelect.AddListener(node.cachedListener);
            }
        }

        private void AutoUnbindPointables()
        {
            foreach (var node in _ordered)
            {
                if (node?.pointableWrapper == null || node.cachedListener == null) continue;

                if (node.interactionMode == InteractionMode.Touch)
                    node.pointableWrapper.WhenHover.RemoveListener(node.cachedListener);
                else
                    node.pointableWrapper.WhenSelect.RemoveListener(node.cachedListener);

                node.cachedListener = null;
                node.pointableWrapper = null;
            }
        }

        // 해당 노드가 다시 이벤트 못 타도록 즉시 봉인
        private void DisableNodeInteraction(StepNode node)
        {
            if (node == null) return;

            if (node.pointableWrapper != null && node.cachedListener != null)
            {
                if (node.interactionMode == InteractionMode.Touch)
                    node.pointableWrapper.WhenHover.RemoveListener(node.cachedListener);
                else
                    node.pointableWrapper.WhenSelect.RemoveListener(node.cachedListener);
            }

            if (node.pointableWrapper != null)
                node.pointableWrapper.enabled = false;

            node.cachedListener = null;

            // 콜라이더까지 막고 싶으면 아래 주석 해제
            // var col = node.interactable ? node.interactable.GetComponent<Collider>() : null;
            // if (col != null) col.enabled = false;
        }
        #endregion

        #region BGM Crossfade
        private IEnumerator CrossfadeToMainBgm()
        {
            if (mainBgm == null) yield break;

            var g = GameObject.Find("BGM_Global");
            if (g == null)
            {
                g = new GameObject("BGM_Global");
                DontDestroyOnLoad(g);
                var s = g.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = true; s.spatialBlend = 0f;
                s.clip = mainBgm; s.volume = mainBgmVolume; s.Play();
                yield break;
            }

            var from = g.GetComponent<AudioSource>();
            if (from == null)
            {
                from = g.AddComponent<AudioSource>();
                from.playOnAwake = false; from.loop = true; from.spatialBlend = 0f;
            }

            if (from.clip == mainBgm)
            {
                from.volume = mainBgmVolume;
                if (!from.isPlaying) from.Play();
                yield break;
            }

            var to = g.AddComponent<AudioSource>();
            to.playOnAwake = false; to.loop = true; to.spatialBlend = 0f;
            to.clip = mainBgm; to.volume = 0f; to.Play();

            float dur = Mathf.Max(0.01f, bgmCrossfadeSeconds);
            float t = 0f;
            float fromStart = from != null ? from.volume : 0f;

            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                if (from != null) from.volume = Mathf.Lerp(fromStart, 0f, k);
                to.volume = Mathf.Lerp(0f, mainBgmVolume, k);
                yield return null;
            }

            if (from != null)
            {
                from.Stop();
                Destroy(from);
            }
        }
        #endregion

        #region 외부 호출 API
        public void NotifyTouched(GameObject touched) => NotifyInteractedInternal(touched);
        public void NotifyGrabbed(GameObject grabbed) => NotifyInteractedInternal(grabbed);
        public void NotifyInteracted(GameObject go) => NotifyInteractedInternal(go);

        public void NotifyInteractedByIndex(int orderIndex)
        {
            var cur = _ordered.FirstOrDefault(n => n.orderIndex == orderIndex);
            if (cur == null) return;
            if (_ordered[_cursor] != cur) return;
            if (cur.triggered) return;

            cur.triggered = true;
            DisableNodeInteraction(cur);

            PlayInteractionSfx(cur);

            if (_runningCo != null) StopCoroutine(_runningCo);
            _runningCo = StartCoroutine(RunStep(cur));
        }

        // 엔터 디버그: 현재 단계가 수행 중이면 대기, 미시작이면 정상 트리거(딜레이/디액티브 포함)
        public void ForceProceed()
        {
            if (_waitingStep) return;

            if (_cursor < _ordered.Count)
            {
                var cur = _ordered[_cursor];
                if (cur.triggered) return;

                cur.triggered = true;
                DisableNodeInteraction(cur);
                PlayInteractionSfx(cur);

                if (_runningCo != null) StopCoroutine(_runningCo);
                _runningCo = StartCoroutine(RunStep(cur));
            }
            else
            {
                _ = FinishAndGotoNextSceneAsync();
            }
        }

        private void NotifyInteractedInternal(GameObject go)
        {
            if (_waitingStep || go == null) return;
            if (_cursor >= _ordered.Count) return;

            var cur = _ordered[_cursor];
            if (go != cur.interactable) return;
            if (cur.triggered) return;

            cur.triggered = true;
            DisableNodeInteraction(cur);

            PlayInteractionSfx(cur);

            if (_runningCo != null) StopCoroutine(_runningCo);
            _runningCo = StartCoroutine(RunStep(cur));
        }

        private void PlayInteractionSfx(StepNode cur)
        {
            if (cur.interactionSfx == null) return;

            if (SfxManager.Instance != null)
            {
                var pos = cur.interactable != null ? cur.interactable.transform.position : transform.position;
                SfxManager.Instance.PlaySFXAt(cur.interactionSfx, pos, cur.interactionSfxVolume, 1f, 0f);
            }
            else
            {
                var a = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
                a.spatialBlend = 0f;
                a.PlayOneShot(cur.interactionSfx, cur.interactionSfxVolume);
            }
        }
        #endregion

        #region 단계 실행
        private IEnumerator RunStep(StepNode step)
        {
            _waitingStep = true;
            DisableCurrentOutline();

            switch (step.stepType)
            {
                case StepType.Text:
                    if (step.textToActivate != null) step.textToActivate.SetActive(true);
                    {
                        float td = Mathf.Max(0f, step.activateDuration);
                        if (td > 0f) yield return new WaitForSeconds(td);
                        if (step.deactivateAfterDuration && step.textToActivate != null)
                            step.textToActivate.SetActive(false);
                    }
                    break;

                case StepType.Image:
                    if (step.imageToActivate != null) step.imageToActivate.SetActive(true);
                    {
                        float id = Mathf.Max(0f, step.activateDuration);
                        if (id > 0f) yield return new WaitForSeconds(id);
                        if (step.deactivateAfterDuration && step.imageToActivate != null)
                            step.imageToActivate.SetActive(false);
                    }
                    break;

                case StepType.Video:
                    EnsureVideoObjectActive(step.videoPlayer);
                    yield return PlayVideoAndWait(step.videoPlayer, step.videoPostDelay, step.deactivateVideoOnEnd);
                    break;

                case StepType.Sound:
                    yield return PlaySoundAndWait(step.soundClip, step.soundVolume, step.soundPostDelay, step.interactable);
                    break;
            }

            _waitingStep = false;
            ProceedNext();
        }

        private void EnsureVideoObjectActive(VideoPlayer player)
        {
            if (player == null) return;
            if (!player.gameObject.activeSelf) player.gameObject.SetActive(true);
        }

        private IEnumerator FastComplete(StepNode step)
        {
            switch (step.stepType)
            {
                case StepType.Text:
                    if (step.textToActivate != null) step.textToActivate.SetActive(true);
                    break;
                case StepType.Image:
                    if (step.imageToActivate != null) step.imageToActivate.SetActive(true);
                    break;
            }
            ProceedNext();
            yield break;
        }

        private IEnumerator PlayVideoAndWait(VideoPlayer player, float postDelay, bool deactivateOnEnd)
        {
            if (player == null) yield break;

            bool done = false;
            void OnLoopPointReached(VideoPlayer vp) { done = true; }

            player.loopPointReached += OnLoopPointReached;
            player.Play();

            while (!done)
                yield return null;

            if (postDelay > 0f) yield return new WaitForSeconds(postDelay);

            player.loopPointReached -= OnLoopPointReached;

            if (deactivateOnEnd && player.gameObject.activeSelf)
                player.gameObject.SetActive(false);
        }

        private IEnumerator PlaySoundAndWait(AudioClip clip, float vol, float postDelay, GameObject srcObj)
        {
            if (clip == null) yield break;

            float length = clip.length;

            if (SfxManager.Instance != null)
            {
                if (srcObj != null)
                    SfxManager.Instance.PlaySFXAt(clip, srcObj.transform.position, vol, 1f, 0f);
                else
                    SfxManager.Instance.PlaySFX(clip, vol, 0f);
            }
            else
            {
                var tmp = gameObject.AddComponent<AudioSource>();
                tmp.spatialBlend = 0f;
                tmp.PlayOneShot(clip, Mathf.Clamp01(vol));
                Destroy(tmp, length + 0.1f);
            }

            yield return new WaitForSeconds(length + Mathf.Max(0f, postDelay));
        }
        #endregion

        #region 진행/하이라이트/엔딩
        private void ProceedNext()
        {
            _cursor++;

            if (_cursor >= _ordered.Count)
            {
                _ = FinishAndGotoNextSceneAsync();
            }
            else
            {
                HighlightCurrentNode();
            }
        }

        private async UniTask FinishAndGotoNextSceneAsync()
        {
            DisableCurrentOutline();

            if (autoProceedDelayAfterAll > 0f)
                await UniTask.Delay(TimeSpan.FromSeconds(autoProceedDelayAfterAll));

            if (fadeOutOnExit && FadeManager.Instance != null)
                await FadeManager.Instance.FadeOutAsync();

            if (destroyOnSceneChange)
                SelfDestruct();

            if (SceneLoader.Instance != null)
                SceneLoader.Instance.LoadScene(nextSceneName);
            else
                SceneManager.LoadScene(nextSceneName);
        }

        private void HighlightCurrentNode()
        {
            DisableCurrentOutline();

            if (_cursor >= _ordered.Count) return;

            var cur = _ordered[_cursor];
            var target = cur.EffectiveHighlightTarget;
            if (target == null) return;

            var ol = target.GetComponent<Outline>();
            if (ol == null) ol = target.AddComponent<Outline>();

            ol.OutlineMode = cur.outlineMode;
            ol.OutlineColor = cur.outlineColor;
            ol.OutlineWidth = cur.outlineWidth;
            ol.enabled = true;

            _currentOutline = ol;

            var sfx = cur.guideSfx != null ? cur.guideSfx : defaultGuideSfx;
            float vol = cur.guideSfx != null ? cur.guideSfxVolume : defaultGuideSfxVolume;
            if (sfx != null)
            {
                if (SfxManager.Instance != null)
                    SfxManager.Instance.PlaySFX(sfx, vol, 0f);
                else
                {
                    var a = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
                    a.spatialBlend = 0f;
                    a.PlayOneShot(sfx, vol);
                }
            }
        }

        private void DisableCurrentOutline()
        {
            if (_currentOutline != null)
            {
                _currentOutline.enabled = false;
                _currentOutline = null;
            }
        }

        private void SelfDestruct()
        {
            if (_selfDestructing) return;
            _selfDestructing = true;

            if (_runningCo != null) { StopCoroutine(_runningCo); _runningCo = null; }
            _waitingStep = false;

            DisableCurrentOutline();

            if (Instance == this) Instance = null;
            Destroy(gameObject);
        }
        #endregion
    }
}
