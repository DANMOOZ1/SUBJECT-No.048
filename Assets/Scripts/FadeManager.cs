using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;

public class FadeManager : MonoBehaviour
{
    public static FadeManager Instance { get; private set; }

    [Header("Fade Sphere")]
    [Tooltip("씬에서 찾을 FadeSphere 오브젝트 이름")]
    [SerializeField] private string fadeSphereName = "FadeSphere";

    [Header("머터리얼 & 프로퍼티")]
    [SerializeField] private int materialIndex = 0;                 // 여러 매터리얼 중 사용할 인덱스
    [SerializeField] private string alphaProperty = "_Color";       // 기본으로 시도할 알파 프로퍼티 이름

    [Header("페이드 설정")]
    [SerializeField] private float fadeDuration = 5f;

    private Renderer targetRenderer;    // 현재 씬의 FadeSphere 렌더러
    private Material _material;         // 복제해 사용중인 머터리얼 인스턴스
    private string resolvedAlphaProperty; // 실제로 사용 가능한 프로퍼티 이름

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // 씬 변경을 감지해 FadeSphere 재할당
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // 현재 씬에 이미 FadeSphere가 있을 수 있으므로 초기 할당 시도
        TryAssignFadeSphereRenderer();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 씬이 로드될 때마다 FadeSphere 찾아서 설정
        TryAssignFadeSphereRenderer();
    }

    /// <summary>
    /// 외부에서 직접 Renderer를 지정하고 싶을 때 사용.
    /// 지정하면 내부에서 머터리얼을 인스턴스화하여 사용합니다.
    /// </summary>
    public void SetFadeSphereRenderer(Renderer renderer)
    {
        AssignRenderer(renderer);
    }

    private void TryAssignFadeSphereRenderer()
    {
        var go = GameObject.Find(fadeSphereName);
        if (go == null)
        {
            Debug.LogWarning($"[FadeManager] 씬에서 '{fadeSphereName}' 오브젝트를 찾지 못했습니다.");
            targetRenderer = null;
            _material = null;
            resolvedAlphaProperty = null;
            return;
        }

        var rend = go.GetComponent<Renderer>();
        if (rend == null)
        {
            Debug.LogWarning($"[FadeManager] '{fadeSphereName}' 오브젝트에 Renderer 컴포넌트가 없습니다.");
            targetRenderer = null;
            _material = null;
            resolvedAlphaProperty = null;
            return;
        }

        AssignRenderer(rend);
    }

    private void AssignRenderer(Renderer rend)
    {
        targetRenderer = rend;

        var mats = targetRenderer.materials;
        if (mats == null || mats.Length == 0)
        {
            Debug.LogWarning("[FadeManager] targetRenderer에 할당된 머터리얼이 없습니다.");
            _material = null;
            resolvedAlphaProperty = null;
            return;
        }

        // 인덱스 안전 처리
        int idx = Mathf.Clamp(materialIndex, 0, mats.Length - 1);

        // 기존에 생성한 인스턴스가 있으면 그대로 덮어쓰지 않고 새 인스턴스로 교체
        mats[idx] = Instantiate(mats[idx]);
        _material = mats[idx];
        targetRenderer.materials = mats;

        // 알파 프로퍼티 결정 (우선 사용자 지정, 없으면 _BaseColor, _Color 순으로 검사)
        if (_material.HasProperty(alphaProperty))
        {
            resolvedAlphaProperty = alphaProperty;
        }
        else if (_material.HasProperty("_BaseColor"))
        {
            resolvedAlphaProperty = "_BaseColor";
        }
        else if (_material.HasProperty("_Color"))
        {
            resolvedAlphaProperty = "_Color";
        }
        else
        {
            resolvedAlphaProperty = null;
            Debug.LogWarning("[FadeManager] 머터리얼에서 알파 조절 가능한 프로퍼티를 찾을 수 없습니다 (예: _BaseColor 또는 _Color).");
        }

        // 기본값: 완전 불투명(1)
        SetMaterialAlpha(1f);
    }

    public UniTask FadeInAsync() => FadeAsync(1f, 0f);
    public UniTask FadeOutAsync() => FadeAsync(0f, 1f);

    private async UniTask FadeAsync(float from, float to)
    {
        if (_material == null)
        {
            Debug.LogWarning("[FadeManager] Fade 시도 중인데 _material이 설정되어 있지 않습니다. Fade를 무시합니다.");
            return;
        }

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float alpha = Mathf.Lerp(from, to, t);
            SetMaterialAlpha(alpha);
            await UniTask.Yield();
        }

        SetMaterialAlpha(to);
    }

    private void SetMaterialAlpha(float alpha)
    {
        if (_material == null) return;

        if (!string.IsNullOrEmpty(resolvedAlphaProperty) && _material.HasProperty(resolvedAlphaProperty))
        {
            Color c = _material.GetColor(resolvedAlphaProperty);
            c.a = alpha;
            _material.SetColor(resolvedAlphaProperty, c);
        }
        else
        {
            // 안전 장치: 프로퍼티가 없으면 시도해보고 예외 발생 시 경고만 출력
            try
            {
                Color c = _material.color;
                c.a = alpha;
                _material.color = c;
            }
            catch (Exception)
            {
                Debug.LogWarning("[FadeManager] 머터리얼에 알파를 설정할 수 있는 프로퍼티가 없습니다.");
            }
        }
    }
}
