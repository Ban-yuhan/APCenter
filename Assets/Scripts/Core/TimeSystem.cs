using System;
using UnityEngine;

namespace APCenter.Core
{
    public class TimeSystem : MonoBehaviour
    {
        public static TimeSystem Instance { get; private set; }

        [Header("Time Settings")]
        [Tooltip("Seconds in real time for 1 game hour (e.g., 10s = 240s for a full 24h day)")]
        [SerializeField] private float realSecondsPerGameHour = 8.0f; // 현실 8초 = 게임 1시간 (하루 약 3.2분)

        public float CurrentGameHour { get; private set; } = 8.0f; // 오전 8시 시작
        public int CurrentDay { get; private set; } = 1;
        public bool IsNight { get; private set; } = false;

        public event Action<int, int> OnTimeTick; // hour, minute
        public event Action<int> OnDayCompleted;  // day passed (00:00 midnight)
        public event Action<bool> OnNightChanged;

        [Header("Lighting")]
        [SerializeField] private Light directionalLight;

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
            if (directionalLight == null)
            {
                var sun = GameObject.Find("Directional Light");
                if (sun != null) directionalLight = sun.GetComponent<Light>();
            }

            LightingManager.EnsureLightingManagerExists();
        }

        private void Update()
        {
            float previousHour = CurrentGameHour;
            CurrentGameHour += (Time.deltaTime / realSecondsPerGameHour);

            // 자정 체크
            if (CurrentGameHour >= 24.0f)
            {
                CurrentGameHour -= 24.0f;
                CurrentDay++;
                OnDayCompleted?.Invoke(CurrentDay);
            }

            // 야간 체크 (22:00 ~ 06:00)
            bool nightNow = (CurrentGameHour >= 22.0f || CurrentGameHour < 6.0f);
            if (nightNow != IsNight)
            {
                IsNight = nightNow;
                OnNightChanged?.Invoke(IsNight);
                EconomyManager.Instance?.UpdateNightShift(IsNight);
            }

            int h = (int)CurrentGameHour;
            int m = (int)((CurrentGameHour - h) * 60f);
            OnTimeTick?.Invoke(h, m);

            if (LightingManager.Instance == null)
            {
                UpdateLighting();
            }
        }

        private void UpdateLighting()
        {
            if (directionalLight == null) return;

            float angle = (CurrentGameHour / 24f) * 360f - 90f;
            directionalLight.transform.rotation = Quaternion.Euler(new Vector3(angle, -30f, 0f));

            if (IsNight)
            {
                directionalLight.intensity = Mathf.Lerp(directionalLight.intensity, 0.1f, Time.deltaTime * 2f);
                directionalLight.color = Color.Lerp(directionalLight.color, new Color(0.2f, 0.3f, 0.5f), Time.deltaTime * 2f);
            }
            else
            {
                directionalLight.intensity = Mathf.Lerp(directionalLight.intensity, 1.1f, Time.deltaTime * 2f);
                directionalLight.color = Color.Lerp(directionalLight.color, new Color(1.0f, 0.96f, 0.9f), Time.deltaTime * 2f);
            }
        }

        public string GetFormattedTime()
        {
            int h = (int)CurrentGameHour;
            int m = (int)((CurrentGameHour - h) * 60f);
            return $"{h:D2}:{m:D2}";
        }
    }
}
