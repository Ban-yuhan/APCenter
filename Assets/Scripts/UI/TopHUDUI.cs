using System;
using UnityEngine;
using UnityEngine.UI;
using APCenter.Core;

namespace APCenter.UI
{
    /// <summary>
    /// TopHUD 보조 UI 컨트롤러 (TopHUDUI.cs):
    /// 1. 이벤트 발생 시 왼쪽 상단 TopHUD 아래에 작게 [이벤트명 / 남은 시간(초) / 진행도] UI 표시
    ///    - ① [정전]: 발전기 발판 (이탈 시 감소) 0%~100%
    ///    - ② [기상 - 비]: 도크 옆 차수판 발판 (이탈 시 유지) 0%~100%
    ///    - ③ [기상 - 태풍]: 창문 앞 창문보강 발판 (이탈 시 유지) 0%~100%
    ///    - ④ [기상 - 눈]: 외부 통로 제설 발판 (이탈 시 감소) 0%~100%
    ///    - ⑤ [기업 대량주문]: 목표 수량 (0/30) 표시 (파렛트 출고 시 +9 즉시 카운트)
    /// 2. 우측 상단 TopHUD 아래에 상시 '앙팡데이' 수동 버튼 배치 (클릭 시 1분간 주문량 2배, 수익 2배 버프 적용)
    /// </summary>
    public class TopHUDUI : MonoBehaviour
    {
        public static TopHUDUI Instance { get; private set; }

        [Header("Left-Top Compact Event Widget (Under TopHUD)")]
        [SerializeField] private GameObject compactEventPanel;
        [SerializeField] private Text compactEventText;
        [SerializeField] private Image compactProgressFill;

        [Header("Right-Top Always-On Enfant Day Button (Under TopHUD)")]
        [SerializeField] private Button enfantDayButton;
        [SerializeField] private Text enfantDayButtonText;
        [SerializeField] private Image enfantDayButtonBg;

        public static void EnsureTopHUDExists()
        {
            if (Instance != null) return;

            var existing = FindAnyObjectByType<TopHUDUI>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) return;

            TopHUDUI hud = canvas.gameObject.GetComponent<TopHUDUI>();
            if (hud == null)
            {
                hud = canvas.gameObject.AddComponent<TopHUDUI>();
            }
            hud.InitializeSubHUD(canvas.transform);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            Canvas canvas = GetComponentInParent<Canvas>() ?? FindAnyObjectByType<Canvas>();
            if (canvas != null)
            {
                InitializeSubHUD(canvas.transform);
            }

            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.OnEventStarted += HandleEventStarted;
                EventSystem.Instance.OnEventEnded += HandleEventEnded;
                EventSystem.Instance.OnEventProgressChanged += RefreshEventWidget;
                EventSystem.Instance.OnEnfantDayStateChanged += HandleEnfantDayChanged;
            }

            RefreshEventWidget();
            RefreshEnfantDayButton();
        }

        private void OnDestroy()
        {
            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.OnEventStarted -= HandleEventStarted;
                EventSystem.Instance.OnEventEnded -= HandleEventEnded;
                EventSystem.Instance.OnEventProgressChanged -= RefreshEventWidget;
                EventSystem.Instance.OnEnfantDayStateChanged -= HandleEnfantDayChanged;
            }
        }

        public void InitializeSubHUD(Transform canvasTransform)
        {
            Font defaultFont = null;
            try { defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }

            // 기존 중앙 EventBanner가 있으면 숨김 처리 (왼쪽 상단 컴팩트 위젯으로 대체)
            Transform oldBanner = canvasTransform.Find("EventBanner");
            if (oldBanner != null)
            {
                oldBanner.gameObject.SetActive(false);
            }

            // 1. 왼쪽 상단 TopHUD 아래: 컴팩트 [이벤트명 / 남은 시간(초) / 진행도] 패널
            if (compactEventPanel == null)
            {
                Transform existingPanel = canvasTransform.Find("LeftTop_CompactEventHUD");
                if (existingPanel != null)
                {
                    compactEventPanel = existingPanel.gameObject;
                    compactEventText = existingPanel.Find("EventInfoText")?.GetComponent<Text>();
                    compactProgressFill = existingPanel.Find("ProgressBarBg/ProgressBarFill")?.GetComponent<Image>();
                }
                else
                {
                    compactEventPanel = new GameObject("LeftTop_CompactEventHUD");
                    compactEventPanel.transform.SetParent(canvasTransform, false);

                    RectTransform panelRect = compactEventPanel.AddComponent<RectTransform>();
                    panelRect.anchorMin = new Vector2(0f, 1f);
                    panelRect.anchorMax = new Vector2(0f, 1f);
                    panelRect.pivot = new Vector2(0f, 1f);
                    panelRect.anchoredPosition = new Vector2(18f, -108f); // 왼쪽 상단 TopHUD 바로 아래
                    panelRect.sizeDelta = new Vector2(540f, 62f);

                    Image bgImg = compactEventPanel.AddComponent<Image>();
                    bgImg.color = new Color(0.10f, 0.13f, 0.18f, 0.93f);

                    // 텍스트: [이벤트명 / 남은 시간(초) / 진행도]
                    GameObject txtObj = new GameObject("EventInfoText");
                    txtObj.transform.SetParent(compactEventPanel.transform, false);
                    RectTransform txtRect = txtObj.AddComponent<RectTransform>();
                    txtRect.anchorMin = new Vector2(0f, 0.25f);
                    txtRect.anchorMax = new Vector2(1f, 1f);
                    txtRect.offsetMin = new Vector2(12f, 2f);
                    txtRect.offsetMax = new Vector2(-12f, -4f);

                    compactEventText = txtObj.AddComponent<Text>();
                    if (defaultFont != null) compactEventText.font = defaultFont;
                    compactEventText.fontSize = 17;
                    compactEventText.alignment = TextAnchor.MiddleLeft;
                    compactEventText.color = Color.white;
                    compactEventText.text = "[이벤트 대기 중]";

                    // 하단 미니 진행도 바 배경
                    GameObject barBgObj = new GameObject("ProgressBarBg");
                    barBgObj.transform.SetParent(compactEventPanel.transform, false);
                    RectTransform barBgRect = barBgObj.AddComponent<RectTransform>();
                    barBgRect.anchorMin = new Vector2(0f, 0f);
                    barBgRect.anchorMax = new Vector2(1f, 0f);
                    barBgRect.pivot = new Vector2(0.5f, 0f);
                    barBgRect.anchoredPosition = new Vector2(0f, 6f);
                    barBgRect.sizeDelta = new Vector2(-24f, 10f);
                    Image barBgImg = barBgObj.AddComponent<Image>();
                    barBgImg.color = new Color(0.22f, 0.25f, 0.30f, 1f);

                    // 하단 미니 진행도 바 Fill
                    GameObject barFillObj = new GameObject("ProgressBarFill");
                    barFillObj.transform.SetParent(barBgObj.transform, false);
                    RectTransform barFillRect = barFillObj.AddComponent<RectTransform>();
                    barFillRect.anchorMin = Vector2.zero;
                    barFillRect.anchorMax = Vector2.one;
                    barFillRect.offsetMin = Vector2.zero;
                    barFillRect.offsetMax = Vector2.zero;

                    compactProgressFill = barFillObj.AddComponent<Image>();
                    compactProgressFill.color = new Color(0.2f, 0.85f, 0.45f, 1f);
                    compactProgressFill.type = Image.Type.Filled;
                    compactProgressFill.fillMethod = Image.FillMethod.Horizontal;
                    compactProgressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
                    compactProgressFill.fillAmount = 0f;
                }
            }

            // 2. 우측 상단 TopHUD 아래: 상시 '앙팡데이' 수동 버튼
            if (enfantDayButton == null)
            {
                Transform existingBtn = canvasTransform.Find("RightTop_EnfantDayButton");
                if (existingBtn != null)
                {
                    enfantDayButton = existingBtn.GetComponent<Button>();
                    enfantDayButtonBg = existingBtn.GetComponent<Image>();
                    enfantDayButtonText = existingBtn.GetComponentInChildren<Text>();
                }
                else
                {
                    GameObject btnObj = new GameObject("RightTop_EnfantDayButton");
                    btnObj.transform.SetParent(canvasTransform, false);

                    RectTransform btnRect = btnObj.AddComponent<RectTransform>();
                    btnRect.anchorMin = new Vector2(1f, 1f);
                    btnRect.anchorMax = new Vector2(1f, 1f);
                    btnRect.pivot = new Vector2(1f, 1f);
                    btnRect.anchoredPosition = new Vector2(-18f, -108f); // 우측 상단 TopHUD 바로 아래
                    btnRect.sizeDelta = new Vector2(250f, 56f);

                    enfantDayButtonBg = btnObj.AddComponent<Image>();
                    enfantDayButtonBg.color = new Color(0.92f, 0.32f, 0.18f, 0.96f);

                    enfantDayButton = btnObj.AddComponent<Button>();
                    ColorBlock cb = enfantDayButton.colors;
                    cb.highlightedColor = new Color(1f, 0.45f, 0.25f, 1f);
                    cb.pressedColor = new Color(0.75f, 0.22f, 0.12f, 1f);
                    enfantDayButton.colors = cb;

                    GameObject lblObj = new GameObject("Label");
                    lblObj.transform.SetParent(btnObj.transform, false);
                    RectTransform lblRect = lblObj.AddComponent<RectTransform>();
                    lblRect.anchorMin = Vector2.zero;
                    lblRect.anchorMax = Vector2.one;
                    lblRect.offsetMin = new Vector2(6f, 2f);
                    lblRect.offsetMax = new Vector2(-6f, -2f);

                    enfantDayButtonText = lblObj.AddComponent<Text>();
                    if (defaultFont != null) enfantDayButtonText.font = defaultFont;
                    enfantDayButtonText.fontSize = 16;
                    enfantDayButtonText.alignment = TextAnchor.MiddleCenter;
                    enfantDayButtonText.color = Color.white;
                    enfantDayButtonText.text = "🔥 [앙팡데이] 발동\n(1분간 주문량·수익 2배)";
                }

                if (enfantDayButton != null)
                {
                    enfantDayButton.onClick.RemoveListener(OnEnfantDayButtonClicked);
                    enfantDayButton.onClick.AddListener(OnEnfantDayButtonClicked);
                }
            }
        }

        private void Update()
        {
            RefreshEventWidget();
            RefreshEnfantDayButton();
        }

        private void OnEnfantDayButtonClicked()
        {
            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.TriggerEnfantDayManual();
                RefreshEnfantDayButton();
                RefreshEventWidget();
            }
        }

        private void HandleEventStarted(GameEventType type, string title, float duration)
        {
            RefreshEventWidget();
        }

        private void HandleEventEnded(GameEventType type, bool isSuccess)
        {
            RefreshEventWidget();
        }

        private void HandleEnfantDayChanged(bool active, float remaining)
        {
            RefreshEnfantDayButton();
        }

        private void RefreshEventWidget()
        {
            if (compactEventPanel == null || EventSystem.Instance == null) return;

            var es = EventSystem.Instance;
            if (es.ActiveEvent == GameEventType.None)
            {
                if (compactEventPanel.activeSelf)
                {
                    compactEventPanel.SetActive(false);
                }
                return;
            }

            if (!compactEventPanel.activeSelf)
            {
                compactEventPanel.SetActive(true);
            }

            string eventName = es.GetActiveEventShortTitle();
            string formattedTime = es.GetFormattedRemainingTime();
            string progressStr = es.GetActiveEventProgressString();

            if (compactEventText != null)
            {
                compactEventText.text = $"{eventName} | 남은 시간: {formattedTime} | 진행도: <color=#ffeaa7>{progressStr}</color>";
            }

            if (compactProgressFill != null)
            {
                compactProgressFill.fillAmount = es.GetActiveEventProgressRatio();
            }
        }

        private void RefreshEnfantDayButton()
        {
            if (enfantDayButton == null || enfantDayButtonText == null || EventSystem.Instance == null) return;

            var es = EventSystem.Instance;
            if (es.IsEnfantDayActive)
            {
                int sec = Mathf.CeilToInt(es.EnfantDayTimer);
                enfantDayButtonText.text = $"🔥 [앙팡데이 적용중: {sec}초]\n(주문량 2배 · 수익 2배)";
                if (enfantDayButtonBg != null)
                {
                    enfantDayButtonBg.color = new Color(0.95f, 0.55f, 0.12f, 0.98f);
                }
            }
            else
            {
                enfantDayButtonText.text = "🔥 [앙팡데이] 수동 발동\n(1분간 주문량 2배 · 수익 2배)";
                if (enfantDayButtonBg != null)
                {
                    enfantDayButtonBg.color = new Color(0.92f, 0.32f, 0.18f, 0.96f);
                }
            }
        }
    }
}
