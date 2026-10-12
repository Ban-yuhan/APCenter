using System.Collections;
using UnityEngine;
using APCenter.Core;
using APCenter.Player;

namespace APCenter.Facilities
{
    /// <summary>
    /// 발전기 오브젝트 + 발판 : 뇌우/정전 이벤트 발생 시 플레이어가 발판 위에 서 있으면 진행도 증가.
    /// Start()에서 발전기 시각 오브젝트와 진행도 텍스트를 코드로 동적 생성합니다.
    /// </summary>
    public class GeneratorStation : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float holdDuration = 3.0f;   // 한 번 작업 완료에 필요한 발판 서있는 시간 (초)
        [SerializeField] private Vector3 spawnPosition = new Vector3(-4f, 0f, -8f); // 맵 내 발전기 위치

        // 런타임 상태
        private float holdProgress = 0f;    // 현재 발판 누적 서있는 시간
        private bool isPlayerOnPad = false;
        private bool isWorking = false;     // 진행 중 여부 (중복 완료 방지)

        // 진행도 텍스트
        private TextMesh progressText;
        private TextMesh taskCountText;

        // 발판 Collider
        private BoxCollider padCollider;

        // 발전기 비주얼 오브젝트들
        private GameObject genBody;
        private GameObject genExhaust;

        private void Awake()
        {
            transform.position = spawnPosition;
        }

        private void Start()
        {
            BuildGenerator();
            BuildPad();
            BuildProgressUI();

            // 이벤트 구독
            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.OnEventStarted += OnEventStarted;
                EventSystem.Instance.OnEventEnded   += OnEventEnded;
                EventSystem.Instance.OnWeatherTaskUpdated += OnWeatherTaskUpdated;
            }

            // 초기에는 숨김
            SetGeneratorVisible(false);
        }

        private void OnDestroy()
        {
            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.OnEventStarted -= OnEventStarted;
                EventSystem.Instance.OnEventEnded   -= OnEventEnded;
                EventSystem.Instance.OnWeatherTaskUpdated -= OnWeatherTaskUpdated;
            }
        }

        // ─── 발전기 시각 오브젝트 생성 ───────────────────────────────────────────

        private void BuildGenerator()
        {
            // 몸체 (큰 회색 육면체)
            genBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
            genBody.name = "GenBody";
            genBody.transform.SetParent(transform);
            genBody.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            genBody.transform.localScale    = new Vector3(1.4f, 1.2f, 0.9f);
            SetColor(genBody, new Color(0.25f, 0.25f, 0.25f)); // 짙은 회색
            Destroy(genBody.GetComponent<Collider>()); // 자체 콜라이더 제거

            // 배기구 (좁고 긴 원기둥)
            genExhaust = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            genExhaust.name = "GenExhaust";
            genExhaust.transform.SetParent(transform);
            genExhaust.transform.localPosition = new Vector3(0.3f, 1.6f, 0f);
            genExhaust.transform.localScale    = new Vector3(0.18f, 0.4f, 0.18f);
            SetColor(genExhaust, new Color(0.15f, 0.15f, 0.15f));
            Destroy(genExhaust.GetComponent<Collider>());

            // 상단 패널 (노란색 계기판 표시용)
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "GenPanel";
            panel.transform.SetParent(transform);
            panel.transform.localPosition = new Vector3(0f, 1.25f, -0.46f);
            panel.transform.localScale    = new Vector3(0.8f, 0.3f, 0.02f);
            SetColor(panel, new Color(0.9f, 0.7f, 0.1f)); // 노란색 계기판
            Destroy(panel.GetComponent<Collider>());

            // 발전기 아이콘 텍스트 (⚡)
            var iconObj = new GameObject("GenIcon");
            iconObj.transform.SetParent(transform);
            iconObj.transform.localPosition = new Vector3(0f, 2.1f, 0f);
            var iconTM = iconObj.AddComponent<TextMesh>();
            iconTM.text = "⚡ 발전기";
            iconTM.fontSize = 24;
            iconTM.characterSize = 0.10f;
            iconTM.alignment = TextAlignment.Center;
            iconTM.anchor    = TextAnchor.MiddleCenter;
            iconTM.color     = Color.yellow;
        }

        private void BuildPad()
        {
            // 발판 (납작한 초록색 Cube)
            var padObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            padObj.name = "GeneratorPad";
            padObj.transform.SetParent(transform);
            padObj.transform.localPosition = new Vector3(0f, 0.05f, 1.4f);
            padObj.transform.localScale    = new Vector3(1.6f, 0.08f, 1.0f);
            SetColor(padObj, new Color(0.1f, 0.8f, 0.2f, 0.85f));

            // 기존 콜라이더는 isTrigger로 변환
            var col = padObj.GetComponent<BoxCollider>();
            col.isTrigger = true;
            padCollider = col;

            // 발판 위 "여기 서세요" 텍스트
            var hintObj = new GameObject("PadHint");
            hintObj.transform.SetParent(padObj.transform);
            hintObj.transform.localPosition = new Vector3(0f, 5.0f, 0f);
            var hintTM = hintObj.AddComponent<TextMesh>();
            hintTM.text = "▼ 발판";
            hintTM.fontSize = 20;
            hintTM.characterSize = 0.08f;
            hintTM.alignment = TextAlignment.Center;
            hintTM.anchor    = TextAnchor.MiddleCenter;
            hintTM.color     = Color.green;
        }

        private void BuildProgressUI()
        {
            // 진행도 % 텍스트 (발전기 정면 위)
            var progObj = new GameObject("ProgressText");
            progObj.transform.SetParent(transform);
            progObj.transform.localPosition = new Vector3(0f, 2.6f, -0.5f);
            progressText = progObj.AddComponent<TextMesh>();
            progressText.text = "";
            progressText.fontSize = 28;
            progressText.characterSize = 0.09f;
            progressText.alignment = TextAlignment.Center;
            progressText.anchor    = TextAnchor.MiddleCenter;
            progressText.color     = Color.cyan;

            // 완료 횟수 텍스트 (예: 0/3)
            var cntObj = new GameObject("TaskCountText");
            cntObj.transform.SetParent(transform);
            cntObj.transform.localPosition = new Vector3(0f, 3.2f, -0.5f);
            taskCountText = cntObj.AddComponent<TextMesh>();
            taskCountText.text = "";
            taskCountText.fontSize = 24;
            taskCountText.characterSize = 0.09f;
            taskCountText.alignment = TextAlignment.Center;
            taskCountText.anchor    = TextAnchor.MiddleCenter;
            taskCountText.color     = Color.white;
        }

        // ─── 이벤트 핸들러 ─────────────────────────────────────────────────────

        private void OnEventStarted(GameEventType type, string title, float duration)
        {
            if (type == GameEventType.BadWeather_Blackout)
            {
                SetGeneratorVisible(true);
                holdProgress = 0f;
                isWorking = false;
                UpdateProgressDisplay(0f, 0, 3);
            }
        }

        private void OnEventEnded(GameEventType type, bool isSuccess)
        {
            if (type == GameEventType.BadWeather_Blackout)
            {
                SetGeneratorVisible(false);
                holdProgress = 0f;
                if (progressText != null) progressText.text = "";
                if (taskCountText != null) taskCountText.text = "";
            }
        }

        private void OnWeatherTaskUpdated(int cur, int req)
        {
            // 이미 이벤트 끝나면 무시
            if (EventSystem.Instance == null ||
                EventSystem.Instance.ActiveEvent != GameEventType.BadWeather_Blackout) return;

            holdProgress = 0f; // 한 단계 완료 시 진행도 초기화
            isWorking = false;
            UpdateProgressDisplay(0f, cur, req);
        }

        // ─── 매 프레임 발판 체크 ───────────────────────────────────────────────

        private void Update()
        {
            if (EventSystem.Instance == null ||
                EventSystem.Instance.ActiveEvent != GameEventType.BadWeather_Blackout)
            {
                isPlayerOnPad = false;
                return;
            }

            // 플레이어가 발판 위에 있는지 직접 거리로 체크 (Trigger 대신)
            if (PlayerController.Instance != null)
            {
                var padWorldPos = transform.position + new Vector3(0f, 0f, 1.4f);
                float dist = Vector3.Distance(PlayerController.Instance.transform.position, padWorldPos);
                isPlayerOnPad = (dist < 1.4f);
            }

            if (isPlayerOnPad && !isWorking)
            {
                holdProgress += Time.deltaTime;
                float pct = Mathf.Clamp01(holdProgress / holdDuration) * 100f;
                int cur = EventSystem.Instance.WeatherTaskProgress;
                int req = EventSystem.Instance.WeatherTaskRequired;
                UpdateProgressDisplay(pct, cur, req);

                if (holdProgress >= holdDuration)
                {
                    isWorking = true; // 완료 처리 중 - OnWeatherTaskUpdated에서 리셋됨
                    EventSystem.Instance.AdvanceWeatherTask();
                    SoundManager.Instance?.PlaySuccess();
                }
            }
            else if (!isPlayerOnPad)
            {
                // 발판 벗어나면 진행도 천천히 감소 (빠르게 반감)
                if (holdProgress > 0f)
                {
                    holdProgress -= Time.deltaTime * 0.6f;
                    holdProgress = Mathf.Max(0f, holdProgress);
                    float pct = Mathf.Clamp01(holdProgress / holdDuration) * 100f;
                    int cur = EventSystem.Instance != null ? EventSystem.Instance.WeatherTaskProgress : 0;
                    int req = EventSystem.Instance != null ? EventSystem.Instance.WeatherTaskRequired : 3;
                    UpdateProgressDisplay(pct, cur, req);
                }
            }
        }

        // ─── 유틸리티 ─────────────────────────────────────────────────────────

        private void UpdateProgressDisplay(float pct, int cur, int req)
        {
            if (progressText != null)
                progressText.text = $"{pct:F0}%";

            if (taskCountText != null)
                taskCountText.text = $"[{cur}/{req}회 완료]";
        }

        private void SetGeneratorVisible(bool visible)
        {
            // 자신과 자식 전부 켜기/끄기
            foreach (var rend in GetComponentsInChildren<Renderer>())
                rend.enabled = visible;

            foreach (var tm in GetComponentsInChildren<TextMesh>())
                tm.gameObject.SetActive(visible);
        }

        private static void SetColor(GameObject go, Color color)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;
            var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            mat.color = color;
            rend.material = mat;
        }
    }
}
