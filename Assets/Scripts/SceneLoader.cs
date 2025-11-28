using Cysharp.Threading.Tasks;
// using Oculus.Interaction;   // ← 안 쓰면 제거
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    [SerializeField] private bool autoFadeInAfterLoad = false;

    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); }
    }

    public void LoadScene(string sceneName)
    {
        _ = TransitionAsync(sceneName);
    }

    public UniTask LoadSceneAsync(string sceneName)
    {
        return TransitionAsync(sceneName);
    }

    private async UniTask TransitionAsync(string sceneName)
    {
        // 1) 현재 씬에서 페이드 아웃 (알파 0 → 1)
        if (FadeManager.Instance != null)
            await FadeManager.Instance.FadeOutAsync();

        // 2) 씬 로드
        await SceneManager
            .LoadSceneAsync(sceneName)
            .ToUniTask(cancellationToken: this.GetCancellationTokenOnDestroy());

        // FadeManager가 새 씬의 FadeSphere를 바인딩할 시간을 한 프레임 줌
        await UniTask.Yield();

        // 3) 옵션: 새 씬에서 페이드 인 (알파 1 → 0)
        if (autoFadeInAfterLoad && FadeManager.Instance != null)
            await FadeManager.Instance.FadeInAsync();
    }
}
