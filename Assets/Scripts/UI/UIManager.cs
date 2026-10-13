using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Facilities;
using APCenter.Player;
using APCenter.Worker;

namespace APCenter.UI
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Top HUD Elements")]
        [SerializeField] private Text moneyText;
        [SerializeField] private Text netProfitText;
        [SerializeField] private Text timeText;
        [SerializeField] private Text reputationText;
        [SerializeField] private Text facilityTierText;

        [Header("Event Banner")]
        [SerializeField] private GameObject eventBannerPanel;
        [SerializeField] private Text eventBannerText;
        [SerializeField] private Text eventTimerText;

        [Header("Outbound Truck Action")]
        [SerializeField] private Button truckDepartButton;
        [SerializeField] private Text truckDepartButtonText;
        [SerializeField] private Image truckLoadFillImage;

        [Header("Command Bar Buttons")]
        [SerializeField] private Button openWorkerPanelButton;
        [SerializeField] private Button openUpgradePanelButton;

        [Header("Worker Management Panel")]
        [SerializeField] private GameObject workerPanel;
        [SerializeField] private Text workerListText;
        [SerializeField] private Button closeWorkerPanelButton;
        [SerializeField] private Button hireNormalWorkerButton;
        [SerializeField] private Button hireTempWorkerButton;
        [SerializeField] private Button reassignAllNextRoleButton; // 순환 재배치 편의 버튼

        [Header("Upgrade Panel")]
        [SerializeField] private GameObject upgradePanel;
        [SerializeField] private Text upgradeListText;
        [SerializeField] private Button closeUpgradePanelButton;
        [SerializeField] private Button upgradeSpeedButton;
        [SerializeField] private Button upgradeCapacityButton;
        [SerializeField] private Button unlockPalletButton;
        [SerializeField] private Button upgradeTierButton;

        [Header("Popup Notice / Receipts")]
        [SerializeField] private GameObject noticePopupPanel;
        [SerializeField] private Text noticePopupText;
        [SerializeField] private Button closeNoticeButton;

        private float popupTimer = 0f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            TopHUDUI.EnsureTopHUDExists();
            LightingManager.EnsureLightingManagerExists();
            RecruitCardUI.EnsureRecruitSystemExists();
            PalletStation.EnsurePalletAndForkliftZonesExist();
            Billboard.StandardizeAllSceneWorldTexts();
            BindEvents();
            CloseAllPanels();
            UpdateTopHUD();
        }

        private void BindEvents()
        {
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnMoneyChanged += HandleMoneyChanged;
                EconomyManager.Instance.OnProfitStatsUpdated += HandleProfitStatsUpdated;
            }

            if (TimeSystem.Instance != null)
            {
                TimeSystem.Instance.OnTimeTick += HandleTimeTick;
            }

            if (ReputationSystem.Instance != null)
            {
                ReputationSystem.Instance.OnReputationChanged += HandleReputationChanged;
            }

            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.OnEventStarted += HandleEventStarted;
                EventSystem.Instance.OnEventEnded += HandleEventEnded;
                EventSystem.Instance.OnWeatherTaskUpdated += HandleWeatherTaskUpdated;
            }

            if (WorkerManager.Instance != null)
            {
                WorkerManager.Instance.OnDailyPromotionReport += ShowNotice;
            }

            if (truckDepartButton != null)
            {
                truckDepartButton.onClick.AddListener(OnTruckDepartClicked);
            }

            if (openWorkerPanelButton != null) openWorkerPanelButton.onClick.AddListener(OpenWorkerPanel);
            if (openUpgradePanelButton != null) openUpgradePanelButton.onClick.AddListener(OpenUpgradePanel);
            if (closeWorkerPanelButton != null) closeWorkerPanelButton.onClick.AddListener(CloseAllPanels);
            if (closeUpgradePanelButton != null) closeUpgradePanelButton.onClick.AddListener(CloseAllPanels);
            if (closeNoticeButton != null) closeNoticeButton.onClick.AddListener(CloseNotice);

            if (hireNormalWorkerButton != null) hireNormalWorkerButton.onClick.AddListener(OnHireNormalWorker);
            if (hireTempWorkerButton != null) hireTempWorkerButton.onClick.AddListener(OnHireTempWorker);
            if (reassignAllNextRoleButton != null) reassignAllNextRoleButton.onClick.AddListener(OnCycleWorkerRoles);

            if (upgradeSpeedButton != null) upgradeSpeedButton.onClick.AddListener(OnUpgradeSpeed);
            if (upgradeCapacityButton != null) upgradeCapacityButton.onClick.AddListener(OnUpgradeCapacity);
            if (unlockPalletButton != null)
            {
                unlockPalletButton.onClick.AddListener(OnUnlockPallet);
                var btnTxt = unlockPalletButton.GetComponentInChildren<Text>();
                if (btnTxt != null) btnTxt.text = "지게차 시스템 해금";
            }
            if (upgradeTierButton != null) upgradeTierButton.onClick.AddListener(OnUpgradeTier);
        }

        private void Update()
        {
            UpdateTruckHUD();

            if (EventSystem.Instance != null && EventSystem.Instance.ActiveEvent != GameEventType.None)
            {
                if (eventTimerText != null)
                {
                    eventTimerText.text = $"남은 시간: {EventSystem.Instance.GetFormattedRemainingTime()}";
                }
            }

            if (noticePopupPanel != null && noticePopupPanel.activeSelf && popupTimer > 0f)
            {
                popupTimer -= Time.deltaTime;
                if (popupTimer <= 0f) CloseNotice();
            }
        }

        private void UpdateTopHUD()
        {
            if (EconomyManager.Instance != null)
            {
                HandleMoneyChanged(EconomyManager.Instance.CurrentMoney);
            }

            if (ReputationSystem.Instance != null)
            {
                HandleReputationChanged(ReputationSystem.Instance.CurrentReputation);
            }

            if (GameManager.Instance != null && facilityTierText != null)
            {
                string tierName = GameManager.Instance.CurrentTier == FacilityTier.SmallHub ? "Tier 1: 작은 창고"
                                : GameManager.Instance.CurrentTier == FacilityTier.MediumCenter ? "Tier 2: 중형 물류센터"
                                : "Tier 3: 메가 메가허브";
                facilityTierText.text = $"🏢 {tierName}";
            }
        }

        private void UpdateTruckHUD()
        {
            if (OutboundTruck.AllTrucks.Count == 0) return;

            var seoulTruck = OutboundTruck.GetTruckForDestination(PackageDestination.Seoul);
            var busanTruck = OutboundTruck.GetTruckForDestination(PackageDestination.Busan);
            var daeguTruck = OutboundTruck.GetTruckForDestination(PackageDestination.Daegu);

            bool anyReady = false;
            float maxRatio = 0f;
            foreach (var t in OutboundTruck.AllTrucks)
            {
                if (t == null) continue;
                if (t.IsReadyToDepart) anyReady = true;
                if (t.LoadRatio > maxRatio) maxRatio = t.LoadRatio;
            }

            if (truckLoadFillImage != null)
            {
                truckLoadFillImage.fillAmount = maxRatio;
            }

            if (truckDepartButton != null)
            {
                truckDepartButton.interactable = anyReady;
            }

            if (truckDepartButtonText != null)
            {
                string sStr = seoulTruck != null ? $"{seoulTruck.LoadedSlotCount}/{seoulTruck.MaxCapacity}" : "0/10";
                string bStr = busanTruck != null ? $"{busanTruck.LoadedSlotCount}/{busanTruck.MaxCapacity}" : "0/10";
                string dStr = daeguTruck != null ? $"{daeguTruck.LoadedSlotCount}/{daeguTruck.MaxCapacity}" : "0/10";

                if (anyReady)
                {
                    truckDepartButtonText.text = $"🚚 출발 가능! [서울:{sStr} | 부산:{bStr} | 대구:{dStr}]";
                }
                else
                {
                    truckDepartButtonText.text = $"⏳ 도크 적재중 [서울:{sStr} | 부산:{bStr} | 대구:{dStr}] (5칸↑ 출발)";
                }
            }
        }

        private void HandleMoneyChanged(long money)
        {
            if (moneyText != null)
            {
                moneyText.text = $"₩ {money:N0}";
            }
        }

        private void HandleProfitStatsUpdated(float rev, float labor, float upkeep, float net)
        {
            if (netProfitText != null)
            {
                string sign = net >= 0 ? "+" : "";
                string colorTag = net >= 0 ? "<color=#4cd137>" : "<color=#e84118>";
                netProfitText.text = $"순이익: {colorTag}{sign}₩{net:N0}/분</color> (매출 ₩{rev:N0} - 인건 ₩{labor:N0} - 유지 ₩{upkeep:N0})";
            }
        }

        private void HandleTimeTick(int hour, int minute)
        {
            if (timeText != null && TimeSystem.Instance != null)
            {
                string icon = TimeSystem.Instance.IsNight ? "🌙 [야간 +50%]" : "☀️ [주간]";
                timeText.text = $"{icon} {hour:D2}:{minute:D2} (Day {TimeSystem.Instance.CurrentDay})";
            }
        }

        private void HandleReputationChanged(float rep)
        {
            if (reputationText != null)
            {
                int stars = ReputationSystem.Instance != null ? ReputationSystem.Instance.GetStarRating() : 3;
                string starStr = new string('★', stars) + new string('☆', 5 - stars);
                float mult = ReputationSystem.Instance != null ? ReputationSystem.Instance.GetPriceMultiplier() : 1.0f;
                reputationText.text = $"평판: {starStr} ({rep:F0}%) [단가 x{mult:F2}]";
            }
        }

        private void HandleEventStarted(GameEventType type, string title, float duration)
        {
            if (TopHUDUI.Instance != null)
            {
                if (eventBannerPanel != null) eventBannerPanel.SetActive(false);
                return;
            }
            if (eventBannerPanel != null) eventBannerPanel.SetActive(true);
            if (eventBannerText != null) eventBannerText.text = title;
        }

        private void HandleEventEnded(GameEventType type, bool isSuccess)
        {
            if (eventBannerPanel != null) eventBannerPanel.SetActive(false);
        }

        private void HandleWeatherTaskUpdated(int cur, int req)
        {
            if (TopHUDUI.Instance == null && eventBannerText != null)
            {
                eventBannerText.text = $"🛠️ 기상 악화 복구 작업 진행 중: ({cur}/{req}%)";
            }
        }

        public void ShowNotice(string message)
        {
            if (noticePopupPanel != null) noticePopupPanel.SetActive(true);
            if (noticePopupText != null) noticePopupText.text = message;
            popupTimer = 5.0f;
        }

        public void CloseNotice()
        {
            if (noticePopupPanel != null) noticePopupPanel.SetActive(false);
        }

        public void OpenWorkerPanel()
        {
            CloseAllPanels();
            RecruitCardUI.EnsureRecruitSystemExists();
            if (RecruitCardUI.Instance != null)
            {
                RecruitCardUI.Instance.OpenRecruitCards();
                return;
            }

            if (workerPanel != null)
            {
                workerPanel.SetActive(true);
                RefreshWorkerListUI();
            }
        }

        public void OpenUpgradePanel()
        {
            CloseAllPanels();
            if (upgradePanel != null)
            {
                upgradePanel.SetActive(true);
                RefreshUpgradeListUI();
            }
        }

        public void CloseAllPanels()
        {
            if (workerPanel != null) workerPanel.SetActive(false);
            if (upgradePanel != null) upgradePanel.SetActive(false);
        }

        private void RefreshWorkerListUI()
        {
            if (workerListText == null || WorkerManager.Instance == null) return;

            StringBuilder sb = new StringBuilder();
            var workers = WorkerManager.Instance.ActiveWorkers;
            int maxCap = GameManager.Instance != null ? GameManager.Instance.maxWorkersAllowed : 3;

            sb.AppendLine($"[ 알바 현황: {workers.Count} / {maxCap} 명 ]\n");

            if (workers.Count == 0)
            {
                sb.AppendLine("고용된 알바가 없습니다. 아래 버튼으로 고용하세요.");
            }
            else
            {
                for (int i = 0; i < workers.Count; i++)
                {
                    var w = workers[i];
                    if (w == null || w.Data == null) continue;

                    string warn = w.Data.isMismatched ? $"<color=#e84118>⚠️[부적합! 탈주위험 {w.Data.desertionGraceTimer:F0}s]</color>" : "<color=#4cd137>✓[적합]</color>";
                    string typeTag = (w.Data.workerType == WorkerType.Temporary) ? "⚡대타" : "정규";

                    sb.AppendLine($"#{i + 1} {w.Data.workerName} ({typeTag}) | 랭크: {w.Data.rank} | 시급: ₩{w.Data.GetHourlyWage():F0}");
                    sb.AppendLine($"   주특기: {w.Data.specialtyRole} ➔ 현재배치: {w.Data.currentRole}  {warn}");
                }
            }

            workerListText.text = sb.ToString();
        }

        private void RefreshUpgradeListUI()
        {
            if (upgradeListText == null || GameManager.Instance == null) return;

            StringBuilder sb = new StringBuilder();
            var gm = GameManager.Instance;

            sb.AppendLine("=== 시설 및 장비 업그레이드 ===");
            sb.AppendLine($"1. 이동 속도: Lv.{gm.playerMoveSpeedLevel} (비용: ₩{gm.GetUpgradeSpeedCost():N0})");
            sb.AppendLine($"2. 스택 확장 (+1/Lv): Lv.{gm.playerStackCapacityLevel} [최대 {gm.GetPlayerMaxStack()}개] (비용: ₩{gm.GetUpgradeCapacityCost():N0})");
            sb.AppendLine($"3. 지게차 시스템 해금: {(gm.hasForklift ? "해금 완료 (지게차 탑승 및 파렛트 운송 가능)" : $"미해금 (비용: ₩{gm.GetPalletCost():N0})")}");
            sb.AppendLine($"4. 시설 성장: {gm.CurrentTier} ➔ 다음 단계 (비용: ₩{gm.GetTierUpgradeCost():N0})");

            upgradeListText.text = sb.ToString();
        }

        private void OnHireNormalWorker()
        {
            if (RecruitCardUI.Instance != null)
            {
                CloseAllPanels();
                RecruitCardUI.Instance.OpenRecruitCards();
                return;
            }

            WorkerRole randomSpecialty = (WorkerRole)UnityEngine.Random.Range(0, 5);
            var w = WorkerManager.Instance?.HireWorker(randomSpecialty, WorkerType.Normal);
            if (w == null)
            {
                ShowNotice("자금이 부족하거나 최대 인원 한도에 도달했습니다!");
            }
            RefreshWorkerListUI();
        }

        private void OnHireTempWorker()
        {
            var w = WorkerManager.Instance?.HireWorker(WorkerRole.Load, WorkerType.Temporary);
            if (w == null)
            {
                ShowNotice("자금이 부족하거나 최대 인원 한도에 도달했습니다!");
            }
            RefreshWorkerListUI();
        }

        private void OnCycleWorkerRoles()
        {
            // 모든 알바의 역할을 다음 역할로 순환 변경 (재배치 지원)
            if (WorkerManager.Instance == null) return;

            foreach (var w in WorkerManager.Instance.ActiveWorkers)
            {
                if (w != null && w.Data != null)
                {
                    int next = ((int)w.Data.currentRole + 1) % 5;
                    WorkerManager.Instance.ReassignRole(w, (WorkerRole)next);
                }
            }
            RefreshWorkerListUI();
        }

        private void OnUpgradeSpeed()
        {
            if (GameManager.Instance.UpgradePlayerSpeed())
            {
                ShowNotice("⚡ 이동 속도가 업그레이드되었습니다!");
            }
            else
            {
                ShowNotice("골드가 부족합니다!");
            }
            RefreshUpgradeListUI();
        }

        private void OnUpgradeCapacity()
        {
            if (GameManager.Instance.UpgradePlayerCapacity())
            {
                ShowNotice($"📦 스택 확장이 완료되었습니다! (최대 적재량: {GameManager.Instance.GetPlayerMaxStack()}개)");
            }
            else
            {
                ShowNotice("골드가 부족합니다!");
            }
            RefreshUpgradeListUI();
        }

        private void OnUnlockPallet()
        {
            if (GameManager.Instance.UnlockForkliftSystem())
            {
                ShowNotice("🚜 지게차 시스템이 해금되었습니다! 이제 북동쪽 지게차 구역에서 탑승할 수 있습니다!");
            }
            else
            {
                ShowNotice("골드가 부족하거나 이미 해금되었습니다!");
            }
            RefreshUpgradeListUI();
        }

        private void OnUpgradeTier()
        {
            if (GameManager.Instance.UpgradeFacilityTier())
            {
                ShowNotice($"🎉 물류센터가 [{GameManager.Instance.CurrentTier}] 로 성장했습니다!");
                UpdateTopHUD();
            }
            else
            {
                ShowNotice("골드가 부족하거나 이미 최고 등급입니다!");
            }
            RefreshUpgradeListUI();
        }

        private void OnTruckDepartClicked()
        {
            foreach (var truck in OutboundTruck.AllTrucks)
            {
                if (truck != null && truck.IsReadyToDepart)
                {
                    truck.TryDepart();
                }
            }
        }
    }
}
