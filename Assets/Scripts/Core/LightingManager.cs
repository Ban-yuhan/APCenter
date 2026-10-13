using System;
using System.Collections.Generic;
using UnityEngine;

namespace APCenter.Core
{
    /// <summary>
    /// 주야간 및 정전 조명 연출 시스템 (LightingManager.cs):
    /// 1. 실외 조명(Directional Light)과 실내 천장 조명(Point Light 리스트)을 분리 제어합니다.
    /// 2. 밤이 되어도 실내 조명은 항상 밝기를 유지하고 실외만 어두워집니다.
    /// 3. [정전 이벤트](BadWeather_Blackout) 발생 시 실내 조명을 끄고 어두운 붉은색 비상등(Emergency Light)으로 변경하며,
    ///    발전기 수리(100%) 완료 시 원래 밝기로 복구합니다.
    /// </summary>
    public class LightingManager : MonoBehaviour
    {
        public static LightingManager Instance { get; private set; }

        [Header("Outdoor Lighting (Directional Light)")]
        [SerializeField] private Light outdoorDirectionalLight;
        [SerializeField] private float dayOutdoorIntensity = 1.15f;
        [SerializeField] private float nightOutdoorIntensity = 0.08f; // 밤에는 실외가 확연히 어두워짐
        [SerializeField] private Color dayOutdoorColor = new Color(1.0f, 0.96f, 0.90f);
        [SerializeField] private Color nightOutdoorColor = new Color(0.16f, 0.22f, 0.38f);

        [Header("Indoor Ceiling Lights (Point Light List)")]
        [SerializeField] private List<Light> indoorCeilingLights = new List<Light>();
        [SerializeField] private float indoorNormalIntensity = 2.4f;
        [SerializeField] private float indoorLightRange = 19.0f;
        [SerializeField] private Color indoorNormalColor = new Color(1.0f, 0.97f, 0.92f);

        [Header("Emergency Lights (Blackout Event)")]
        [SerializeField] private List<Light> emergencyLights = new List<Light>();
        [SerializeField] private float emergencyBaseIntensity = 1.15f;
        [SerializeField] private Color emergencyRedColor = new Color(0.85f, 0.10f, 0.10f);

        public bool IsBlackoutActive { get; private set; } = false;

        public static void EnsureLightingManagerExists()
        {
            if (Instance != null) return;

            var existing = FindAnyObjectByType<LightingManager>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            GameObject mgrObj = GameObject.Find("Managers");
            if (mgrObj == null)
            {
                mgrObj = new GameObject("LightingManager");
            }
            Instance = mgrObj.AddComponent<LightingManager>();
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
            SetupOutdoorLight();
            SetupIndoorAndEmergencyLights();

            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.OnEventStarted += HandleEventStarted;
                EventSystem.Instance.OnEventEnded += HandleEventEnded;
            }

            // 시작 시 현재 정전 상태 동기화
            bool blackoutNow = (EventSystem.Instance != null && EventSystem.Instance.ActiveEvent == GameEventType.BadWeather_Blackout);
            SetBlackoutMode(blackoutNow);
        }

        private void OnDestroy()
        {
            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.OnEventStarted -= HandleEventStarted;
                EventSystem.Instance.OnEventEnded -= HandleEventEnded;
            }
        }

        private void SetupOutdoorLight()
        {
            if (outdoorDirectionalLight == null)
            {
                var sun = GameObject.Find("Directional Light");
                if (sun != null)
                {
                    outdoorDirectionalLight = sun.GetComponent<Light>();
                }
                else
                {
                    var allLights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                    foreach (var l in allLights)
                    {
                        if (l.type == LightType.Directional)
                        {
                            outdoorDirectionalLight = l;
                            break;
                        }
                    }
                }
            }
        }

        private void SetupIndoorAndEmergencyLights()
        {
            // 이미 생성된 실내 조명 루트가 있는지 확인
            Transform lightRoot = transform.Find("IndoorCeilingLightsRoot");
            if (lightRoot == null)
            {
                GameObject rootObj = new GameObject("IndoorCeilingLightsRoot");
                rootObj.transform.SetParent(transform, false);
                lightRoot = rootObj.transform;
            }

            if (indoorCeilingLights.Count == 0)
            {
                // 창고 내부(36x36) 천장에 3x3 격자로 9개의 실내 천장 Point Light 배치
                // 실외 도로(Z = ±17~20)는 비추지 않고 실내만 항상 밝게 유지되도록 위치 및 범위 설정
                float[] xs = { -10f, 0f, 10f };
                float[] zs = { -9f, 0f, 9f };

                for (int ix = 0; ix < xs.Length; ix++)
                {
                    for (int iz = 0; iz < zs.Length; iz++)
                    {
                        Vector3 ceilingPos = new Vector3(xs[ix], 6.0f, zs[iz]);

                        // 1) 일반 실내 천장 조명 (Point Light)
                        GameObject ptObj = new GameObject($"IndoorCeilingLight_{ix}_{iz}");
                        ptObj.transform.SetParent(lightRoot, false);
                        ptObj.transform.position = ceilingPos;

                        Light ptLight = ptObj.AddComponent<Light>();
                        ptLight.type = LightType.Point;
                        ptLight.color = indoorNormalColor;
                        ptLight.intensity = indoorNormalIntensity;
                        ptLight.range = indoorLightRange;
                        ptLight.shadows = LightShadows.None;
                        indoorCeilingLights.Add(ptLight);

                        // 2) 정전 시 작동하는 어두운 붉은색 비상등 (Emergency Light)
                        GameObject emObj = new GameObject($"EmergencyRedLight_{ix}_{iz}");
                        emObj.transform.SetParent(lightRoot, false);
                        emObj.transform.position = ceilingPos + new Vector3(0f, -0.3f, 0f);

                        Light emLight = emObj.AddComponent<Light>();
                        emLight.type = LightType.Point;
                        emLight.color = emergencyRedColor;
                        emLight.intensity = emergencyBaseIntensity;
                        emLight.range = indoorLightRange * 0.95f;
                        emLight.shadows = LightShadows.None;
                        emLight.enabled = false;
                        emergencyLights.Add(emLight);
                    }
                }
            }
        }

        private void HandleEventStarted(GameEventType type, string title, float duration)
        {
            if (type == GameEventType.BadWeather_Blackout)
            {
                SetBlackoutMode(true);
            }
        }

        private void HandleEventEnded(GameEventType type, bool isSuccess)
        {
            if (type == GameEventType.BadWeather_Blackout)
            {
                // 발전기 수리(100%) 완료 또는 정전 종료 시 원래 밝기로 복구!
                SetBlackoutMode(false);
            }
        }

        /// <summary>
        /// 정전 모드 전환:
        /// - true(정전 발생): 실내 천장 조명을 끄고 어두운 붉은색 비상등(Emergency Light)을 켭니다.
        /// - false(발전기 100% 수리 완료): 비상등을 끄고 실내 천장 조명을 원래 밝기로 복구합니다.
        /// </summary>
        public void SetBlackoutMode(bool isBlackout)
        {
            IsBlackoutActive = isBlackout;

            for (int i = 0; i < indoorCeilingLights.Count; i++)
            {
                if (indoorCeilingLights[i] != null)
                {
                    indoorCeilingLights[i].enabled = !isBlackout;
                    if (!isBlackout)
                    {
                        indoorCeilingLights[i].color = indoorNormalColor;
                        indoorCeilingLights[i].intensity = indoorNormalIntensity;
                    }
                }
            }

            for (int i = 0; i < emergencyLights.Count; i++)
            {
                if (emergencyLights[i] != null)
                {
                    emergencyLights[i].enabled = isBlackout;
                    if (isBlackout)
                    {
                        emergencyLights[i].color = emergencyRedColor;
                        emergencyLights[i].intensity = emergencyBaseIntensity;
                    }
                }
            }
        }

        private void Update()
        {
            // EventSystem 상태와 동기화 안전장치
            if (EventSystem.Instance != null)
            {
                bool shouldBeBlackout = (EventSystem.Instance.ActiveEvent == GameEventType.BadWeather_Blackout);
                if (shouldBeBlackout != IsBlackoutActive)
                {
                    SetBlackoutMode(shouldBeBlackout);
                }
            }

            UpdateOutdoorLighting();
            UpdateIndoorAndEmergencyLighting();
        }

        private void UpdateOutdoorLighting()
        {
            if (outdoorDirectionalLight == null) return;

            float currentHour = (TimeSystem.Instance != null) ? TimeSystem.Instance.CurrentGameHour : 12f;
            bool isNight = (TimeSystem.Instance != null) && TimeSystem.Instance.IsNight;

            // 태양 회전 각도
            float angle = (currentHour / 24f) * 360f - 90f;
            outdoorDirectionalLight.transform.rotation = Quaternion.Euler(new Vector3(angle, -30f, 0f));

            // 실외 조명은 밤이 되면 어두워지고(nightOutdoorIntensity = 0.08f), 정전 시에도 실외가 어둡게 연출됨
            float targetOuterIntensity = (isNight || IsBlackoutActive) ? nightOutdoorIntensity : dayOutdoorIntensity;
            Color targetOuterColor = isNight ? nightOutdoorColor : dayOutdoorColor;
            if (IsBlackoutActive)
            {
                targetOuterColor = new Color(0.25f, 0.12f, 0.15f);
            }

            outdoorDirectionalLight.intensity = Mathf.Lerp(outdoorDirectionalLight.intensity, targetOuterIntensity, Time.deltaTime * 2.5f);
            outdoorDirectionalLight.color = Color.Lerp(outdoorDirectionalLight.color, targetOuterColor, Time.deltaTime * 2.5f);

            // 환경광(Ambient Light) 제어: 밤이나 정전 시 실외 배경이 어두워지도록 설정
            Color targetAmbient = IsBlackoutActive
                ? new Color(0.18f, 0.05f, 0.05f)
                : (isNight ? new Color(0.09f, 0.11f, 0.18f) : new Color(0.55f, 0.58f, 0.62f));
            RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, targetAmbient, Time.deltaTime * 2.5f);
        }

        private void UpdateIndoorAndEmergencyLighting()
        {
            if (IsBlackoutActive)
            {
                // 정전 중: 어두운 붉은색 비상등이 완만하게 맥동(Pulse)하며 경각심 부여
                float pulse = 0.85f + Mathf.Sin(Time.unscaledTime * 3.5f) * 0.25f;
                for (int i = 0; i < emergencyLights.Count; i++)
                {
                    if (emergencyLights[i] != null)
                    {
                        emergencyLights[i].intensity = emergencyBaseIntensity * pulse;
                    }
                }
            }
            else
            {
                // 평상시: 밤이 되어도 실내 천장 조명은 항상 일정한 밝기를 유지!
                for (int i = 0; i < indoorCeilingLights.Count; i++)
                {
                    if (indoorCeilingLights[i] != null)
                    {
                        if (!indoorCeilingLights[i].enabled) indoorCeilingLights[i].enabled = true;
                        indoorCeilingLights[i].intensity = indoorNormalIntensity;
                        indoorCeilingLights[i].color = indoorNormalColor;
                    }
                }
            }
        }
    }
}
