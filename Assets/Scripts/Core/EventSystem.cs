using System;
using System.Collections;
using UnityEngine;

namespace APCenter.Core
{
    public class EventSystem : MonoBehaviour
    {
        public static EventSystem Instance { get; private set; }

        public GameEventType ActiveEvent { get; private set; } = GameEventType.None;
        public float EventTimer { get; private set; } = 0f;
        public float EventDuration { get; private set; } = 0f;

        [Header("Event Timing")]
        [SerializeField] private float timeBetweenEvents = 60f; // 60초마다 랜덤 이벤트 발생 시도
        private float nextEventCountdown = 45f;

        // 긴급 배송 박스 ID
        public int UrgentPackageId { get; private set; } = -1;

        // 기상 악화 미니 태스크 진행도
        public int WeatherTaskProgress { get; private set; } = 0;
        public int WeatherTaskRequired { get; private set; } = 3;

        // 할인 플래그 (기업 대량주문 시 20% 업그레이드 할인)
        public bool IsUpgradeDiscountActive { get; private set; } = false;

        public event Action<GameEventType, string, float> OnEventStarted; // type, title, duration
        public event Action<GameEventType, bool> OnEventEnded;             // type, isSuccess
        public event Action<int, int> OnWeatherTaskUpdated;                // cur, req

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            if (ActiveEvent != GameEventType.None)
            {
                EventTimer -= Time.deltaTime;
                if (EventTimer <= 0f)
                {
                    // 시간 초과 처리
                    if (ActiveEvent == GameEventType.UrgentDelivery)
                    {
                        // 긴급배송 실패
                        EndCurrentEvent(false, "긴급 배송 시간 초과로 실패했습니다!");
                        ReputationSystem.Instance?.AddReputation(-6f);
                    }
                    else if (ActiveEvent == GameEventType.BadWeather_Snow ||
                             ActiveEvent == GameEventType.BadWeather_Rain ||
                             ActiveEvent == GameEventType.BadWeather_Blackout)
                    {
                        // 기상 악화 처리 미완료
                        EndCurrentEvent(false, "기상 악화 대응 지연으로 시설 피해가 발생했습니다!");
                        EconomyManager.Instance?.SpendMoney(500);
                        ReputationSystem.Instance?.AddReputation(-4f);
                    }
                    else
                    {
                        // 피버타임 등 일반 만료
                        EndCurrentEvent(true, "이벤트가 성공적으로 종료되었습니다!");
                    }
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

        public void TriggerRandomEvent()
        {
            if (ActiveEvent != GameEventType.None) return;

            // 랜덤 이벤트 선정
            GameEventType[] candidateEvents = new GameEventType[]
            {
                GameEventType.FeverTime,
                GameEventType.UrgentDelivery,
                GameEventType.BadWeather_Snow,
                GameEventType.BadWeather_Rain,
                GameEventType.BadWeather_Blackout,
                GameEventType.CorporateOrder
            };

            int idx = UnityEngine.Random.Range(0, candidateEvents.Length);
            StartEvent(candidateEvents[idx]);
        }

        public void StartEvent(GameEventType type)
        {
            ActiveEvent = type;
            WeatherTaskProgress = 0;
            WeatherTaskRequired = 3;
            IsUpgradeDiscountActive = false;

            string title = "";
            float duration = 45f;

            switch (type)
            {
                case GameEventType.FeverTime:
                    title = "🔥 [피버타임: 앙팡데이] 물량 및 배송 수익 2배 폭증!";
                    duration = 40f;
                    SoundManager.Instance?.PlaySuccess();
                    break;

                case GameEventType.UrgentDelivery:
                    title = "⚡ [VIP 긴급배송] 황금 택배가 입고되었습니다! 50초 내 출고 필요!";
                    duration = 50f;
                    SoundManager.Instance?.PlayAlert();
                    break;

                case GameEventType.BadWeather_Snow:
                    title = "❄️ [기상악화: 폭설] 입구 제설 작업을 3회 완료하세요! (위험수당 +30%)";
                    duration = 45f;
                    SoundManager.Instance?.PlayAlert();
                    break;

                case GameEventType.BadWeather_Rain:
                    title = "🌧️ [기상악화: 집중호우] 입출고 도크 차수판을 3개 설치하세요! (위험수당 +30%)";
                    duration = 45f;
                    SoundManager.Instance?.PlayAlert();
                    break;

                case GameEventType.BadWeather_Blackout:
                    title = "⚡ [기상악화: 뇌우 정전] 비상 발전기를 3회 조작해 전력을 복구하세요!";
                    duration = 40f;
                    SoundManager.Instance?.PlayAlert();
                    break;

                case GameEventType.CorporateOrder:
                    title = "🏢 [기업 대량주문] 서울행 기업 대량 물량 일괄 입고! 업그레이드 20% 할인!";
                    duration = 60f;
                    IsUpgradeDiscountActive = true;
                    SoundManager.Instance?.PlaySuccess();
                    break;
            }

            EventDuration = duration;
            EventTimer = duration;

            OnEventStarted?.Invoke(type, title, duration);
        }

        public void AdvanceWeatherTask()
        {
            if (ActiveEvent == GameEventType.BadWeather_Snow ||
                ActiveEvent == GameEventType.BadWeather_Rain ||
                ActiveEvent == GameEventType.BadWeather_Blackout)
            {
                WeatherTaskProgress++;
                SoundManager.Instance?.PlayClick();
                OnWeatherTaskUpdated?.Invoke(WeatherTaskProgress, WeatherTaskRequired);

                if (WeatherTaskProgress >= WeatherTaskRequired)
                {
                    EndCurrentEvent(true, "기상 악화 작업을 무사히 마쳤습니다! (보너스 평판 + 골드)");
                    ReputationSystem.Instance?.BonusEventSuccess();
                    EconomyManager.Instance?.AddMoney(800);
                }
            }
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
            OnEventEnded?.Invoke(finished, isSuccess);
        }

        public float GetVolumeMultiplier()
        {
            if (ActiveEvent == GameEventType.FeverTime) return 2.0f;
            if (ActiveEvent == GameEventType.CorporateOrder) return 1.8f;
            return 1.0f;
        }

        public float GetRevenueMultiplier()
        {
            if (ActiveEvent == GameEventType.FeverTime) return 2.0f;
            return 1.0f;
        }
    }
}
