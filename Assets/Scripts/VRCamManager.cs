using System.Collections;
using UnityEngine;

public class VRCamManager : MonoBehaviour
{
    public static VRCamManager Instance { get; private set; }

    [Header("카메라 세팅")]
    public OVRCameraRig ovrRig; // 자식에 있는 OVRCameraRig (필수)

    [Header("스폰/리센터 대상")]
    public Transform spawnPoint; // 인스펙터 할당 권장 (없으면 "SpawnPoint"로 탐색)
    [Tooltip("시작 시 SpawnPoint로 텔레포트(위치 보정) + 스폰포인트 방향으로 회전 정렬")]
    public bool recenterOnStart = true;
    [Tooltip("스폰 직후 대기 시간(초): 리그/트래킹 초기화 대기용")]
    public float startInitDelay = 0.2f;

    [Header("리센터 옵션")]
    [Tooltip("true: 헤드(센터아이)를 SpawnPoint에 정확히 일치 / false: 루트를 SpawnPoint 위치로 스냅")]
    public bool matchHeadToSpawn = true;
    [Tooltip("리센터 시 수직(Y)까지 맞출지 여부 (false면 수평 XZ만 정렬)")]
    public bool includeVertical = true;
    [Tooltip("CharacterController가 있으면 잠깐 껐다가 이동(충돌 방지)")]
    public bool temporarilyDisableCharacterController = true;

    [Header("이동")]
    public float moveSpeed = 1f;
    [Tooltip("이동 입력 최소 임계값")]
    public float moveDeadZone = 0.1f;

    [Header("걸음 효과")]
    public float bobbingSpeed = 5f;
    public float bobbingAmount = 0.08f;

    // 내부 상태
    private bool _isMoving;
    private float _bobTimer;
    private float _currentBobOffset; // 누적 오프셋(드리프트 방지)

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }

        if (ovrRig == null)
            ovrRig = GetComponentInChildren<OVRCameraRig>(true);
    }

    private void Start()
    {
        if (recenterOnStart)
            StartCoroutine(SpawnAndRecenterRoutine());
    }

    private void Update()
    {
        if (ovrRig == null || ovrRig.centerEyeAnchor == null) return;

        HandleMove();
        HandleHeadBobbing();
    }

    // ─────────────────────────────────────────────────────────────
    // 스폰 & 리센터 (스폰포인트로 위치 + 회전(Yaw) 정렬)
    private IEnumerator SpawnAndRecenterRoutine()
    {
        // SpawnPoint 자동 탐색
        if (spawnPoint == null)
        {
            var spGo = GameObject.Find("SpawnPoint");
            if (spGo != null) spawnPoint = spGo.transform;
        }

        // 리그/트래킹 초기화 대기
        if (startInitDelay > 0f) yield return new WaitForSeconds(startInitDelay);
        else yield return null;

        if (spawnPoint == null)
        {
            Debug.LogWarning("[VRCamManager] SpawnPoint 미할당(또는 미발견). 초기 위치 유지.");
            yield break;
        }

        RecenterTo(spawnPoint);
    }

    // ─────────────────────────────────────────────────────────────
    // 런타임에서도 호출 가능한 "스폰포인트로 위치+Yaw 리센터"
    public void RecenterTo(Transform target)
    {
        if (target == null)
        {
            Debug.LogWarning("[VRCamManager] RecenterTo()에 null 타겟.");
            return;
        }
        if (ovrRig == null || ovrRig.centerEyeAnchor == null)
        {
            Debug.LogWarning("[VRCamManager] OVRCameraRig/centerEyeAnchor가 없습니다.");
            return;
        }

        var head = ovrRig.centerEyeAnchor;

        if (matchHeadToSpawn)
        {
            // 1) 회전(Yaw): 헤드의 현재 Yaw를 스폰포인트 Yaw로 맞춘다 (루트 회전)
            float headYaw = head.eulerAngles.y;
            float targetYaw = target.eulerAngles.y;
            float deltaYaw = Mathf.DeltaAngle(headYaw, targetYaw);
            SafeRotateRootYaw(deltaYaw);

            // 2) 위치: 헤드 위치를 스폰포인트 위치에 정확히 일치 (XZ만/XYZ)
            Vector3 headPos = head.position;
            Vector3 goalPos = target.position;
            if (!includeVertical) goalPos.y = headPos.y;

            Vector3 offset = goalPos - headPos;
            SafeMoveRootPosition(transform.position + offset);
        }
        else
        {
            // 루트를 스폰포인트로 스냅 (Yaw는 스폰포인트 Yaw로 세팅)
            Vector3 newRootPos = transform.position;
            if (includeVertical)
                newRootPos = target.position;
            else
                newRootPos = new Vector3(target.position.x, transform.position.y, target.position.z);

            SafeSetRootPositionAndYaw(newRootPos, target.eulerAngles.y);
        }

        // 보빙 상태 초기화
        _bobTimer = 0f;
        ApplyBobOffset(0f, true);
    }

    // ─────────────────────────────────────────────────────────────
    // 루트 이동/회전(캐릭터컨트롤러 충돌 방지)
    private void SafeMoveRootPosition(Vector3 newPos)
    {
        var cc = temporarilyDisableCharacterController ? GetComponent<CharacterController>() : null;
        bool reenable = false;
        if (cc != null && cc.enabled) { cc.enabled = false; reenable = true; }

        transform.position = newPos;

        if (reenable) cc.enabled = true;
    }

    private void SafeRotateRootYaw(float deltaYaw)
    {
        if (Mathf.Approximately(deltaYaw, 0f)) return;

        var cc = temporarilyDisableCharacterController ? GetComponent<CharacterController>() : null;
        bool reenable = false;
        if (cc != null && cc.enabled) { cc.enabled = false; reenable = true; }

        transform.Rotate(0f, deltaYaw, 0f, Space.World);

        if (reenable) cc.enabled = true;
    }

    private void SafeSetRootPositionAndYaw(Vector3 newPos, float targetYaw)
    {
        var cc = temporarilyDisableCharacterController ? GetComponent<CharacterController>() : null;
        bool reenable = false;
        if (cc != null && cc.enabled) { cc.enabled = false; reenable = true; }

        transform.position = newPos;
        var e = transform.eulerAngles; e.y = targetYaw; transform.eulerAngles = e;

        if (reenable) cc.enabled = true;
    }

    // ─────────────────────────────────────────────────────────────
    // 이동: 이 오브젝트(transform) 직접 이동 (헤드 Yaw 기준)
    private void HandleMove()
    {
        Vector2 mov2d = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        Vector3 move = new Vector3(mov2d.x, 0f, mov2d.y);

        // 데드존
        if (move.sqrMagnitude < moveDeadZone * moveDeadZone)
        {
            _isMoving = false;
            return;
        }

        Transform head = ovrRig.centerEyeAnchor;
        float yaw = head.eulerAngles.y;
        Vector3 worldDir = Quaternion.Euler(0f, yaw, 0f) * move.normalized;

        transform.position += worldDir * (moveSpeed * Time.deltaTime);
        _isMoving = true;
    }

    // ─────────────────────────────────────────────────────────────
    // 헤드 보빙(드리프트 없는 방식: 이전 오프셋을 되돌리고 새 오프셋 적용)
    private void HandleHeadBobbing()
    {
        float targetOffset = 0f;

        if (_isMoving)
        {
            _bobTimer += Time.deltaTime * bobbingSpeed;
            targetOffset = Mathf.Sin(_bobTimer) * bobbingAmount;
        }
        else
        {
            _bobTimer = 0f;
            targetOffset = Mathf.MoveTowards(_currentBobOffset, 0f, Time.deltaTime * bobbingAmount * bobbingSpeed);
        }

        ApplyBobOffset(targetOffset, false);
    }

    private void ApplyBobOffset(float newOffset, bool forceSet)
    {
        float delta = forceSet ? -_currentBobOffset + newOffset : (newOffset - _currentBobOffset);
        transform.position += Vector3.up * delta; // 루트 자체에 소량 오프셋
        _currentBobOffset = newOffset;
    }
}
