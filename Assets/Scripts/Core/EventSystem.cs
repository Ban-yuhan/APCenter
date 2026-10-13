using System;
using System.Collections;
using UnityEngine;
using APCenter.Facilities;
using APCenter.UI;

namespace APCenter.Core
{
    /// <summary>
    /// 180초(3분) 제한시간 이벤트 및 기업 대량주문 한정 입고, [앙팡데이] 수동 버프 관리 시스템.
    /// - 모든 동적/긴급 이벤트(정전, 기상악화 3종, 기업 대량주문) 제한시간: 180초(3분)
    /// - 기업 대량주문(CorporateOrder):
    ///   1) 발동 시 목표 지역(CorporateTargetDestination)과 목표 수량(TargetBulkCount = 30) 결정
    ///   2) 입고 트럭(InboundTruck)은 정확히 목표 수량(30개)만큼만 해당 지역 택배로 스폰하고,
    ///      30개가 모두 하차된 직후부터는 즉시 일반 3개 지역(서울/부산/대구) 랜덤 입고 루프로 복귀
    ///   3) 180초(3분) 내 목표 수량 출고 완료 시 성공 (파렛트 출고 시 DeliveryUnits +9 즉시 누적 합산)
    /// </summary>
    public class EventSystem : MonoBehaviour
    {
        public static EventSystem Instance { get; private set; }

        public const float StandardEventDuration = 180.0f; // 모든 긴급/기상/기업주문 이벤트 제한시간 180초(3분)

        public GameEventType ActiveEvent { get; private set; } = GameEventType.None;
        public float EventTimer { get; private set; } = 0f;
        public float EventDuration { get; private set; } = StandardEventDuration;

        [Header("Event Timing")]
        [SerializeField] private float timeBetweenEvents = 25f;
        private float nextEventCountdown = 12f;

        // 긴급 배송 박스 ID
        public int UrgentPackageId { get; private set; } = -1;

        // 기상/정전 미션 0% ~ 100% 진행도
        public float MissionProgressPercent { get; private set; } = 0f;

        // 하위 호환용 프로퍼티 (0~100 기준)
        public int WeatherTaskProgress => Mathf.RoundToInt(MissionProgressPercent);
        public int WeatherTaskRequired => 100;

        // ⑤ [기업 대량주문] 목표 지역, 목표 수량(30개), 누적 하차(스폰) 수량, 누적 출고 달성 수량
        public PackageDestination CorporateTargetDestination { get; private set; } = PackageDestination.Seoul;
        public int TargetBulkCount { get; private set; } = 30;
        public int SpawnedBulkCount { get; private set; } = 0;
        public int RemainingBulkSpawnCount => Mathf.Max(0, TargetBulkCount - SpawnedBulkCount);

        public int CorporateOrderCurrent { get; private set; } = 0;
        public int CorporateOrderTarget => TargetBulkCount;

        // 할인 플래그 (기업 대량주문 시 20% 업그레이드 할인)
        public bool IsUpgradeDiscountActive { get; private set; } = false;

        // [앙팡데이] 1분간 주문량 2배, 수익 2배 수동 버프 상태
        public bool IsEnfantDayActive { get; private set; } = false;
        public float EnfantDayTimer { get; private set; } = 0f;
        public const float EnfantDayDuration = 60.0f;

        public event Action<GameEventType, string, float> OnEventStarted; // type, title, duration
        public event Action<GameEventType, bool> OnEventEnded;             // type, isSuccess
        public event Action<int, int> OnWeatherTaskUpdated;                // cur, req
        public event Action OnEventProgressChanged;
        public event Action<bool, float> OnEnfantDayStateChanged;          // active, remainingTime

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
            WeatherTaskZone.EnsureAllWeatherZonesExist();
            TopHUDUI.EnsureTopHUDExists();
        }

        private void Update()
        {
            // 1. [앙팡데이] 60초 수동 버프 타이머 업데이트
            if (IsEnfantDayActive)
            {
                EnfantDayTimer -= Time.deltaTime;
                if (EnfantDayTimer <= 0f)
                {
                    EnfantDayTimer = 0f;
                    IsEnfantDayActive = false;
                    OnEnfantDayStateChanged?.Invoke(false, 0f);

                    if (ActiveEvent == GameEventType.FeverTime)
                    {
                        EndCurrentEvent(true, "🔥 [앙팡데이] 1분 버프가 종료되었습니다!");
                    }
                    else
                    {
                        UIManager.Instance?.ShowNotice("🔥 [앙팡데이] 1분간의 주문량·수익 2배 버프가 종료되었습니다!");
                    }
                }
            }

            // 2. 진행 중인 긴급/기상/기업주문 180초(3분) 카운트다운 업데이트
            if (ActiveEvent != GameEventType.None)
            {
                EventTimer -= Time.deltaTime;
                if (EventTimer <= 0f)
                {
                    EventTimer = 0f;
                    HandleEventTimeout();
                }
            }
            else
            {
                nextEventCountdown -= Time.deltaTime;
                if (nextEventCountdown <= 0f)
                {
                    nextEventCountdown = timeBetweenEvents;
                    TriggerRandomEvent();
                }
            }
        }

        private void HandleEventTimeout()
        {
            switch (ActiveEvent)
            {
                case GameEventType.BadWeather_Blackout:
                case GameEventType.BadWeather_Rain:
                case GameEventType.BadWeather_Typhoon:
                case GameEventType.BadWeather_Snow:
                    EndCurrentEvent(false, $"❌ [{GetActiveEventShortTitle()}] 3분(180초) 제한시간 초과! 시설 피해 발생 (-₩500, 평판 하락)");
                    EconomyManager.Instance?.SpendMoney(500);
                    ReputationSystem.Instance?.AddReputation(-4f);
                    break;

                case GameEventType.CorporateOrder:
                    EndCurrentEvent(false, $"❌ [기업 대량주문] 3분(180초) 내 목표 출고 수량({CorporateOrderCurrent}/{TargetBulkCount}) 미달성으로 실패했습니다!");
                    ReputationSystem.Instance?.AddReputation(-5f);
                    break;

                case GameEventType.UrgentDelivery:
                    EndCurrentEvent(false, "❌ [긴급 배송] 3분(180초) 제한시간 초과로 실패했습니다!");
                    ReputationSystem.Instance?.AddReputation(-6f);
                    break;

                default:
                    EndCurrentEvent(true, "이벤트가 종료되었습니다!");
                    break;
            }
        }

        public void TriggerRandomEvent()
        {
            if (ActiveEvent != GameEventType.None) return;

            // 5대 주요 미션 이벤트 중 랜덤 발생 (①정전, ②비, ③태풍, ④눈, ⑤기업 대량주문)
            GameEventType[] candidateEvents = new GameEventType[]
            {
                GameEventType.BadWeather_Blackout,
                GameEventType.BadWeather_Rain,
                GameEventType.BadWeather_Typhoon,
                GameEventType.BadWeather_Snow,
                GameEventType.CorporateOrder
            };

            int idx = UnityEngine.Random.Range(0, candidateEvents.Length);
            StartEvent(candidateEvents[idx]);
        }

        public void StartEvent(GameEventType type)
        {
            ActiveEvent = type;
            MissionProgressPercent = 0f;
            TargetBulkCount = 30;
            SpawnedBulkCount = 0;
            CorporateOrderCurrent = 0;
            IsUpgradeDiscountActive = false;

            // 모든 동적/긴급 이벤트는 제한시간 180초(3분) 카운트다운을 가짐
            float duration = StandardEventDuration;
            string title = "";

            switch (type)
            {
                case GameEventType.BadWeather_Blackout:
                    title = "⚡ [정전] 발전기 발판(이탈 시 감소)을 밟아 100% 복구하세요! (제한시간 03:00)";
                    SoundManager.Instance?.PlayAlert();
                    break;

                case GameEventType.BadWeather_Rain:
                    title = "🌧️ [기상 - 비] 입/출고 도크 옆 차수판 발판(이탈 시 유지) 100% 설치! (제한시간 03:00)";
                    SoundManager.Instance?.PlayAlert();
                    break;

                case GameEventType.BadWeather_Typhoon:
                    title = "🌪️ [기상 - 태풍] 창문 앞 창문보강 발판(이탈 시 유지) 100% 완료! (제한시간 03:00)";
                    SoundManager.Instance?.PlayAlert();
                    break;

                case GameEventType.BadWeather_Snow:
                    title = "❄️ [기상 - 눈] 외부 통로 제설 발판(이탈 시 감소) 100% 완료! (제한시간 03:00)";
                    SoundManager.Instance?.PlayAlert();
                    break;

                case GameEventType.CorporateOrder:
                    // 목표 지역(서울/부산/대구 중 하나)과 목표 수량(30개) 결정
                    CorporateTargetDestination = (PackageDestination)UnityEngine.Random.Range(1, 4);
                    TargetBulkCount = 30;
                    SpawnedBulkCount = 0;
                    CorporateOrderCurrent = 0;
                    IsUpgradeDiscountActive = true;

                    string city = GetDestinationKorName(CorporateTargetDestination);
                    title = $"🏢 [기업 대량주문: {city}] 3분 내 {TargetBulkCount}개 출고! (한정 {TargetBulkCount}개 입고 후 일반 물류 복귀)";
                    SoundManager.Instance?.PlayAlert();
                    break;

                case GameEventType.FeverTime:
                    duration = EnfantDayDuration;
                    title = "🔥 [앙팡데이] 1분간 주문량 2배, 출고 수익 2배 버프 적용!";
                    IsEnfantDayActive = true;
                    EnfantDayTimer = EnfantDayDuration;
                    SoundManager.Instance?.PlaySuccess();
                    break;

                case GameEventType.UrgentDelivery:
                    title = "⚡ [VIP 긴급배송] 황금 택배가 입고되었습니다! 3분 내 출고 필요!";
                    SoundManager.Instance?.PlayAlert();
                    break;
            }

            EventDuration = duration;
            EventTimer = duration;

            OnEventStarted?.Invoke(type, title, duration);
            OnEventProgressChanged?.Invoke();
        }

        /// <summary>
        /// 입고 트럭(InboundTruck)이 택배 1개를 하차(스폰)할 때 호출합니다.
        /// - 기업 대량주문 진행 중이고 아직 목표 수량(TargetBulkCount = 30개)만큼 스폰되지 않았다면:
        ///   SpawnedBulkCount를 1 증가시키고 목표 지역(CorporateTargetDestination)을 반환합니다.
        /// - 목표 수량(30개)이 모두 입고장에 쏟아진 직후부터는 false를 반환하여
        ///   즉시 평소처럼 3개 지역(서울/부산/대구) 골고루 섞여 입고되는 일반 루프로 복귀시킵니다.
        /// </summary>
        public bool TryConsumeCorporateBulkSpawn(out PackageDestination bulkDestination)
        {
            if (ActiveEvent == GameEventType.CorporateOrder && SpawnedBulkCount < TargetBulkCount)
            {
                SpawnedBulkCount++;
                bulkDestination = CorporateTargetDestination;
                OnEventProgressChanged?.Invoke();

                if (SpawnedBulkCount == TargetBulkCount)
                {
                    string city = GetDestinationKorName(CorporateTargetDestination);
                    UIManager.Instance?.ShowNotice(
                        $"📦 [기업 대량주문] {city}행 목표 물량 {TargetBulkCount}개 입고 완료! 이제 일반 3개 지역 혼합 입고로 복귀합니다.");
                }
                return true;
            }

            bulkDestination = PackageDestination.None;
            return false;
        }

        /// <summary>
        /// 우측 상단 TopHUD 아래의 상시 '앙팡데이' 수동 버튼을 눌렀을 때 호출됩니다.
        /// 1분(60초)간 주문량 2배, 수익 2배 버프를 즉시 적용합니다.
        /// </summary>
        public void TriggerEnfantDayManual()
        {
            IsEnfantDayActive = true;
            EnfantDayTimer = EnfantDayDuration;
            SoundManager.Instance?.PlaySuccess();
            OnEnfantDayStateChanged?.Invoke(true, EnfantDayTimer);

            if (ActiveEvent == GameEventType.None)
            {
                StartEvent(GameEventType.FeverTime);
            }
            else
            {
                OnEventProgressChanged?.Invoke();
            }

            UIManager.Instance?.ShowNotice("🔥 [앙팡데이 발동!] 1분(60초) 동안 주문량 2배, 출고 수익 2배 버프가 적용됩니다!");
        }

        /// <summary>
        /// 기상/정전 발판 위에 플레이어가 서 있을 때 진행도(0~100%)를 증가시킵니다.
        /// </summary>
        public void AddWeatherProgressPercent(GameEventType zoneEventType, float deltaPercent)
        {
            if (ActiveEvent != zoneEventType) return;
            if (deltaPercent <= 0f) return;

            MissionProgressPercent = Mathf.Clamp(MissionProgressPercent + deltaPercent, 0f, 100f);
            OnWeatherTaskUpdated?.Invoke(Mathf.RoundToInt(MissionProgressPercent), 100);
            OnEventProgressChanged?.Invoke();

            if (MissionProgressPercent >= 100f)
            {
                string shortName = GetActiveEventShortTitle();
                EndCurrentEvent(true, $"🎉 [{shortName}] 100% 복구 완료! (보너스 +₩1,000 & 평판 상승)");
                ReputationSystem.Instance?.BonusEventSuccess();
                EconomyManager.Instance?.AddMoney(1000);
                SoundManager.Instance?.PlaySuccess();
            }
        }

        /// <summary>
        /// 발판 이탈 시 진행도 감소 처리 (① [정전] 및 ④ [기상 - 눈] 전용).
        /// ② [기상 - 비], ③ [기상 - 태풍]은 이탈 시 유지되므로 감소하지 않습니다.
        /// </summary>
        public void DecayWeatherProgressPercent(GameEventType zoneEventType, float decayPercent)
        {
            if (ActiveEvent != zoneEventType) return;
            if (MissionProgressPercent <= 0f || MissionProgressPercent >= 100f) return;

            if (zoneEventType == GameEventType.BadWeather_Blackout || zoneEventType == GameEventType.BadWeather_Snow)
            {
                MissionProgressPercent = Mathf.Max(0f, MissionProgressPercent - decayPercent);
                OnWeatherTaskUpdated?.Invoke(Mathf.RoundToInt(MissionProgressPercent), 100);
                OnEventProgressChanged?.Invoke();
            }
        }

        public void AdvanceWeatherTask()
        {
            if (IsWeatherMissionEvent(ActiveEvent))
            {
                AddWeatherProgressPercent(ActiveEvent, 34f);
            }
        }

        /// <summary>
        /// ⑤ [기업 대량주문] 진행 중 택배/파렛트 출고 시 DeliveryUnits만큼 즉시 카운트합니다.
        /// (일반 택배 = +1, 합체 파렛트 PalletItem = +9 즉시 누적 합산!)
        /// </summary>
        public void AddCorporateOrderProgress(int deliveryUnits)
        {
            if (ActiveEvent != GameEventType.CorporateOrder) return;
            if (deliveryUnits <= 0) return;

            CorporateOrderCurrent = Mathf.Min(TargetBulkCount, CorporateOrderCurrent + deliveryUnits);
            OnEventProgressChanged?.Invoke();

            if (CorporateOrderCurrent >= TargetBulkCount)
            {
                string city = GetDestinationKorName(CorporateTargetDestination);
                EndCurrentEvent(true, $"🎉 [기업 대량주문 완료!] {city}행 ({CorporateOrderCurrent}/{TargetBulkCount}) 3분 내 출고 성공! 보너스 +₩2,500!");
                ReputationSystem.Instance?.BonusEventSuccess();
                EconomyManager.Instance?.AddMoney(2500);
                SoundManager.Instance?.PlaySuccess();
            }
        }

        public void AddCorporateOrderProgress(PackageDestination packageDest, int deliveryUnits)
        {
            if (ActiveEvent != GameEventType.CorporateOrder) return;
            // 목표 지역 택배/파렛트이거나 전체 대량주문 카운트 연동
            if (CorporateTargetDestination == PackageDestination.None || packageDest == CorporateTargetDestination)
            {
                AddCorporateOrderProgress(deliveryUnits);
            }
        }

        public void AddCorporateOrderProgress(APCenter.Delivery.PackageItem item)
        {
            if (item == null || ActiveEvent != GameEventType.CorporateOrder) return;
            int units = Mathf.Max(1, item.DeliveryUnits);
            AddCorporateOrderProgress(item.Destination, units);
        }

        public void CompleteUrgentDelivery()
        {
            if (ActiveEvent == GameEventType.UrgentDelivery)
            {
                EndCurrentEvent(true, "⚡ 긴급 배송 완료! 거액의 VIP 보너스를 획득했습니다!");
                ReputationSystem.Instance?.BonusEventSuccess();
                EconomyManager.Instance?.AddMoney(1500);
            }
        }

        public void EndCurrentEvent(bool isSuccess, string message = "")
        {
            if (ActiveEvent == GameEventType.None) return;

            GameEventType finished = ActiveEvent;
            ActiveEvent = GameEventType.None;
            IsUpgradeDiscountActive = false;

            if (!string.IsNullOrEmpty(message))
            {
                UIManager.Instance?.ShowNotice(message);
            }

            OnEventEnded?.Invoke(finished, isSuccess);
            OnEventProgressChanged?.Invoke();
        }

        public static bool IsWeatherMissionEvent(GameEventType type)
        {
            return type == GameEventType.BadWeather_Blackout ||
                   type == GameEventType.BadWeather_Rain ||
                   type == GameEventType.BadWeather_Typhoon ||
                   type == GameEventType.BadWeather_Snow;
        }

        public static string GetDestinationKorName(PackageDestination dest)
        {
            switch (dest)
            {
                case PackageDestination.Seoul: return "서울";
                case PackageDestination.Busan: return "부산";
                case PackageDestination.Daegu: return "대구";
                default:                       return "서울";
            }
        }

        /// <summary>
        /// 남은 시간을 "03:00" (MM:SS) 형식으로 반환합니다.
        /// </summary>
        public string GetFormattedRemainingTime()
        {
            int totalSec = Mathf.CeilToInt(Mathf.Max(0f, EventTimer));
            int minutes = totalSec / 60;
            int seconds = totalSec % 60;
            return $"{minutes:D2}:{seconds:D2}";
        }

        public string GetActiveEventShortTitle()
        {
            switch (ActiveEvent)
            {
                case GameEventType.BadWeather_Blackout: return "⚡ [정전: 발전기]";
                case GameEventType.BadWeather_Rain:     return "🌧️ [기상 - 비: 차수판]";
                case GameEventType.BadWeather_Typhoon:  return "🌪️ [기상 - 태풍: 창문보강]";
                case GameEventType.BadWeather_Snow:     return "❄️ [기상 - 눈: 통로제설]";
                case GameEventType.CorporateOrder:      return $"🏢 [기업 대량주문: {GetDestinationKorName(CorporateTargetDestination)}]";
                case GameEventType.FeverTime:           return "🔥 [앙팡데이 버프]";
                case GameEventType.UrgentDelivery:      return "⚡ [VIP 긴급배송]";
                default:                                return "이벤트 없음";
            }
        }

        public string GetActiveEventProgressString()
        {
            switch (ActiveEvent)
            {
                case GameEventType.BadWeather_Blackout:
                case GameEventType.BadWeather_Snow:
                    return $"{MissionProgressPercent:F0}% (이탈 시 감소)";
                case GameEventType.BadWeather_Rain:
                case GameEventType.BadWeather_Typhoon:
                    return $"{MissionProgressPercent:F0}% (이탈 시 유지)";
                case GameEventType.CorporateOrder:
                    return $"{CorporateOrderCurrent}/{TargetBulkCount} (입고 {SpawnedBulkCount}/{TargetBulkCount})";
                case GameEventType.FeverTime:
                    return "물량·수익 x2";
                case GameEventType.UrgentDelivery:
                    return "출고 대기";
                default:
                    return "-";
            }
        }

        public float GetActiveEventProgressRatio()
        {
            if (IsWeatherMissionEvent(ActiveEvent))
            {
                return Mathf.Clamp01(MissionProgressPercent / 100f);
            }
            if (ActiveEvent == GameEventType.CorporateOrder)
            {
                return Mathf.Clamp01((float)CorporateOrderCurrent / Mathf.Max(1, TargetBulkCount));
            }
            if (ActiveEvent != GameEventType.None && EventDuration > 0f)
            {
                return Mathf.Clamp01(EventTimer / EventDuration);
            }
            return 0f;
        }

        public float GetVolumeMultiplier()
        {
            float mult = 1.0f;
            if (IsEnfantDayActive || ActiveEvent == GameEventType.FeverTime) mult *= 2.0f;
            // 기업 대량주문 중 아직 목표 수량(30개)이 다 입고되지 않았을 때만 입고 물량 가속
            if (ActiveEvent == GameEventType.CorporateOrder && SpawnedBulkCount < TargetBulkCount) mult *= 1.8f;
            return mult;
        }

        public float GetRevenueMultiplier()
        {
            if (IsEnfantDayActive || ActiveEvent == GameEventType.FeverTime) return 2.0f;
            return 1.0f;
        }
    }
}
