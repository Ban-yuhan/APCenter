using System;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.Player;

namespace APCenter.Facilities
{
    /// <summary>
    /// 4종 기상/정전 이벤트 전용 발판 (WeatherTaskZone):
    /// ① [정전] (BadWeather_Blackout): 발전기 발판 (이탈 시 감소) 100% 완료 미션
    /// ② [기상 - 비] (BadWeather_Rain): 입/출고 도크 옆 차수판 발판 (이탈 시 유지) 100% 완료 미션
    /// ③ [기상 - 태풍] (BadWeather_Typhoon): 창문 앞 창문보강 발판 (이탈 시 유지) 100% 완료 미션
    /// ④ [기상 - 눈] (BadWeather_Snow): 외부 통로 제설 발판 (이탈 시 감소) 100% 완료 미션
    /// </summary>
    public class WeatherTaskZone : InteractionZone
    {
        public static readonly List<WeatherTaskZone> AllWeatherZones = new List<WeatherTaskZone>();
        private static bool zonesInitialized = false;

        [Header("Mission Configuration")]
        public GameEventType targetWeatherEvent = GameEventType.BadWeather_Blackout;
        public bool decaysOnExit = true;              // true: 이탈 시 감소 (정전, 눈) / false: 이탈 시 유지 (비, 태풍)
        public float fillSpeedPerSecond = 28.0f;      // 발판 위 초당 진행도 증가량 (% / sec)
        public float decaySpeedPerSecond = 12.0f;     // 발판 이탈 시 초당 감소량 (% / sec)

        [SerializeField] private GameObject visualIndicator;
        private TextMesh zoneTitleLabel;
        private TextMesh progressLabel;
        private Renderer padRenderer;
        private Color basePadColor = new Color(0.2f, 0.8f, 0.9f);
        private bool customBuilt = false;

        public static void EnsureAllWeatherZonesExist()
        {
            if (zonesInitialized) return;
            zonesInitialized = true;

            // 기존 씬의 기본 WeatherTaskZone을 ① [정전: 발전기 발판]으로 설정
            var existing = FindObjectsByType<WeatherTaskZone>(FindObjectsSortMode.None);
            WeatherTaskZone blackoutZone = null;
            if (existing.Length > 0)
            {
                blackoutZone = existing[0];
            }
            else
            {
                GameObject go = new GameObject("WeatherZone_Blackout_Generator");
                blackoutZone = go.AddComponent<WeatherTaskZone>();
            }

            // ① [정전]: 발전기 발판 (이탈 시 감소) - 중앙 좌측 (-3f, 0.05f, -5.5f)
            blackoutZone.ConfigureWeatherZone(
                GameEventType.BadWeather_Blackout,
                decays: true,
                pos: new Vector3(-3f, 0.05f, -5.5f),
                padColor: new Color(0.95f, 0.78f, 0.15f)
            );

            // ② [기상 - 비]: 입/출고 도크 옆 차수판 발판 (이탈 시 유지) - 남쪽 출고 도크 좌측 옆 (-12.5f, 0.05f, -12.5f)
            CreateWeatherZoneIfMissing(
                "WeatherZone_Rain_FloodBarrier",
                GameEventType.BadWeather_Rain,
                decays: false,
                pos: new Vector3(-12.5f, 0.05f, -12.5f),
                padColor: new Color(0.20f, 0.60f, 0.98f)
            );

            // ③ [기상 - 태풍]: 창문 앞 창문보강 발판 (이탈 시 유지) - 동쪽 벽 창문 앞 (14.5f, 0.05f, -4.5f)
            CreateWeatherZoneIfMissing(
                "WeatherZone_Typhoon_WindowBoard",
                GameEventType.BadWeather_Typhoon,
                decays: false,
                pos: new Vector3(14.5f, 0.05f, -4.5f),
                padColor: new Color(0.25f, 0.85f, 0.55f)
            );

            // ④ [기상 - 눈]: 외부 통로 제설 발판 (이탈 시 감소) - 북쪽 입고 통로 옆 (2.5f, 0.05f, 13.5f)
            CreateWeatherZoneIfMissing(
                "WeatherZone_Snow_ClearPath",
                GameEventType.BadWeather_Snow,
                decays: true,
                pos: new Vector3(2.5f, 0.05f, 13.5f),
                padColor: new Color(0.85f, 0.94f, 1.0f)
            );
        }

        private static void CreateWeatherZoneIfMissing(string name, GameEventType type, bool decays, Vector3 pos, Color padColor)
        {
            foreach (var z in AllWeatherZones)
            {
                if (z != null && z.targetWeatherEvent == type) return;
            }

            GameObject obj = GameObject.Find(name);
            if (obj == null)
            {
                obj = new GameObject(name);
            }
            var zone = obj.GetComponent<WeatherTaskZone>() ?? obj.AddComponent<WeatherTaskZone>();
            zone.ConfigureWeatherZone(type, decays, pos, padColor);
        }

        private void Awake()
        {
            if (!AllWeatherZones.Contains(this))
            {
                AllWeatherZones.Add(this);
            }
            zoneType = ZoneType.WeatherTask;
        }

        private void Start()
        {
            EnsureAllWeatherZonesExist();

            if (!customBuilt)
            {
                ConfigureWeatherZone(targetWeatherEvent, decaysOnExit, transform.position, basePadColor);
            }

            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.OnEventStarted += HandleEventStarted;
                EventSystem.Instance.OnEventEnded += HandleEventEnded;
                EventSystem.Instance.OnEventProgressChanged += RefreshLabels;
            }

            RefreshLabels();
        }

        private void OnDestroy()
        {
            AllWeatherZones.Remove(this);
            if (AllWeatherZones.Count == 0)
            {
                zonesInitialized = false;
            }

            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.OnEventStarted -= HandleEventStarted;
                EventSystem.Instance.OnEventEnded -= HandleEventEnded;
                EventSystem.Instance.OnEventProgressChanged -= RefreshLabels;
            }
        }

        public void ConfigureWeatherZone(GameEventType eventType, bool decays, Vector3 pos, Color padColor)
        {
            targetWeatherEvent = eventType;
            decaysOnExit = decays;
            basePadColor = padColor;
            transform.position = pos;
            customBuilt = true;

            // 기존 씬의 구형 3DLabel 제거 또는 재구성
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("3DLabel_"))
                {
                    child.gameObject.SetActive(false);
                }
            }

            BuildFacilityPropAndPad();
            RefreshLabels();
        }

        private void BuildFacilityPropAndPad()
        {
            if (transform.Find("MissionPropRoot") != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");

            // 루트 발판 메쉬 및 콜라이더 설정
            transform.localScale = Vector3.one;
            var selfMesh = GetComponent<MeshRenderer>();
            if (selfMesh != null) selfMesh.enabled = false;

            var col = GetComponent<BoxCollider>();
            if (col == null) col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(3.0f, 2.0f, 3.0f);
            col.center = new Vector3(0f, 1.0f, 0f);
            col.isTrigger = true;

            GameObject propRoot = new GameObject("MissionPropRoot");
            propRoot.transform.SetParent(transform, false);
            propRoot.transform.localPosition = Vector3.zero;

            // 발판 바닥 패드
            GameObject padObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            padObj.name = "MissionPad";
            padObj.transform.SetParent(propRoot.transform, false);
            padObj.transform.localPosition = Vector3.zero;
            padObj.transform.localScale = new Vector3(2.8f, 0.1f, 2.6f);
            Destroy(padObj.GetComponent<Collider>());
            padRenderer = padObj.GetComponent<Renderer>();
            if (padRenderer != null)
            {
                Material mat = new Material(shader) { color = basePadColor };
                padRenderer.material = mat;
            }

            // 각 미션별 3D 시설물 오브젝트(발전기 / 차수판 / 창문 / 눈더미) 생성
            BuildSpecificPropVisual(propRoot.transform, shader);

            // 상단 타이틀 텍스트
            GameObject titleObj = new GameObject("ZoneTitleLabel");
            titleObj.transform.SetParent(propRoot.transform, false);
            titleObj.transform.localPosition = new Vector3(0f, 2.35f, 0f);
            zoneTitleLabel = titleObj.AddComponent<TextMesh>();
            zoneTitleLabel.fontSize = 45;
            zoneTitleLabel.characterSize = 0.08f;
            zoneTitleLabel.alignment = TextAlignment.Center;
            zoneTitleLabel.anchor = TextAnchor.MiddleCenter;
            zoneTitleLabel.color = Color.black;
            Billboard.Attach(titleObj);

            // 진행도 % 텍스트
            GameObject progObj = new GameObject("ZoneProgressLabel");
            progObj.transform.SetParent(propRoot.transform, false);
            progObj.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            progressLabel = progObj.AddComponent<TextMesh>();
            progressLabel.fontSize = 45;
            progressLabel.characterSize = 0.08f;
            progressLabel.alignment = TextAlignment.Center;
            progressLabel.anchor = TextAnchor.MiddleCenter;
            progressLabel.color = Color.black;
            Billboard.Attach(progObj);
        }

        private void BuildSpecificPropVisual(Transform parent, Shader shader)
        {
            if (targetWeatherEvent == GameEventType.BadWeather_Blackout)
            {
                // ① [정전]: 발전기 오브젝트 배치
                GameObject genBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
                genBody.name = "GeneratorBody";
                genBody.transform.SetParent(parent, false);
                genBody.transform.localPosition = new Vector3(0f, 0.65f, 1.5f);
                genBody.transform.localScale = new Vector3(1.5f, 1.2f, 0.9f);
                Destroy(genBody.GetComponent<Collider>());
                genBody.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.28f, 0.30f, 0.32f) };

                GameObject exhaust = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                exhaust.name = "GeneratorExhaust";
                exhaust.transform.SetParent(parent, false);
                exhaust.transform.localPosition = new Vector3(0.45f, 1.45f, 1.5f);
                exhaust.transform.localScale = new Vector3(0.22f, 0.4f, 0.22f);
                Destroy(exhaust.GetComponent<Collider>());
                exhaust.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.15f, 0.15f, 0.15f) };
            }
            else if (targetWeatherEvent == GameEventType.BadWeather_Rain)
            {
                // ② [기상 - 비]: 도크 옆 차수판 거치대
                for (int i = -1; i <= 1; i += 2)
                {
                    GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    post.transform.SetParent(parent, false);
                    post.transform.localPosition = new Vector3(i * 1.1f, 0.55f, -1.4f);
                    post.transform.localScale = new Vector3(0.2f, 1.1f, 0.2f);
                    Destroy(post.GetComponent<Collider>());
                    post.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.95f, 0.75f, 0.1f) };
                }

                GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = "FloodBarrierPlate";
                plate.transform.SetParent(parent, false);
                plate.transform.localPosition = new Vector3(0f, 0.45f, -1.4f);
                plate.transform.localScale = new Vector3(2.1f, 0.75f, 0.12f);
                Destroy(plate.GetComponent<Collider>());
                plate.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.18f, 0.55f, 0.92f) };
            }
            else if (targetWeatherEvent == GameEventType.BadWeather_Typhoon)
            {
                // ③ [기상 - 태풍]: 창문 및 보강 합판 프레임
                GameObject windowGlass = GameObject.CreatePrimitive(PrimitiveType.Cube);
                windowGlass.name = "WindowGlass";
                windowGlass.transform.SetParent(parent, false);
                windowGlass.transform.localPosition = new Vector3(1.5f, 1.2f, 0f);
                windowGlass.transform.localScale = new Vector3(0.15f, 1.6f, 2.2f);
                Destroy(windowGlass.GetComponent<Collider>());
                windowGlass.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.45f, 0.80f, 0.98f) };

                GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                board.name = "ReinforceBoard";
                board.transform.SetParent(parent, false);
                board.transform.localPosition = new Vector3(1.38f, 1.2f, 0f);
                board.transform.localScale = new Vector3(0.08f, 0.4f, 2.3f);
                Destroy(board.GetComponent<Collider>());
                board.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.68f, 0.45f, 0.22f) };
            }
            else if (targetWeatherEvent == GameEventType.BadWeather_Snow)
            {
                // ④ [기상 - 눈]: 외부 통로 제설 구역 눈더미
                for (int i = -1; i <= 1; i++)
                {
                    GameObject snowMound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    snowMound.name = $"SnowMound_{i}";
                    snowMound.transform.SetParent(parent, false);
                    snowMound.transform.localPosition = new Vector3(i * 0.7f, 0.25f, 1.35f);
                    snowMound.transform.localScale = new Vector3(0.85f, 0.45f, 0.75f);
                    Destroy(snowMound.GetComponent<Collider>());
                    snowMound.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.92f, 0.96f, 1.0f) };
                }
            }
        }

        private void Update()
        {
            if (EventSystem.Instance == null) return;

            bool isMyEventActive = (EventSystem.Instance.ActiveEvent == targetWeatherEvent);
            if (!isMyEventActive) return;

            PlayerController p = PlayerController.Instance;
            if (p == null) return;

            Vector3 diff = p.transform.position - transform.position;
            bool isPlayerOnPad = isPlayerInside || (Mathf.Abs(diff.x) <= 1.8f && Mathf.Abs(diff.z) <= 1.8f);

            if (isPlayerOnPad)
            {
                // 발판 위에 서 있으면 100%까지 진행도 증가
                EventSystem.Instance.AddWeatherProgressPercent(targetWeatherEvent, fillSpeedPerSecond * Time.deltaTime);
            }
            else if (decaysOnExit)
            {
                // ① 정전, ④ 눈은 발판 이탈 시 진행도 감소! (② 비, ③ 태풍은 이탈 시 유지)
                EventSystem.Instance.DecayWeatherProgressPercent(targetWeatherEvent, decaySpeedPerSecond * Time.deltaTime);
            }
        }

        private void LateUpdate()
        {
            if (Camera.main != null)
            {
                if (zoneTitleLabel != null) zoneTitleLabel.transform.rotation = Camera.main.transform.rotation;
                if (progressLabel != null) progressLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }

        private void HandleEventStarted(GameEventType type, string title, float duration)
        {
            if (visualIndicator != null) visualIndicator.SetActive(type == targetWeatherEvent);
            RefreshLabels();
        }

        private void HandleEventEnded(GameEventType type, bool isSuccess)
        {
            if (visualIndicator != null) visualIndicator.SetActive(false);
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (zoneTitleLabel == null || progressLabel == null) return;

            string titleText = "";
            string ruleTag = decaysOnExit ? "이탈 시 감소" : "이탈 시 유지";

            switch (targetWeatherEvent)
            {
                case GameEventType.BadWeather_Blackout:
                    titleText = $"⚡ [정전: 발전기 발판]\n({ruleTag})";
                    break;
                case GameEventType.BadWeather_Rain:
                    titleText = $"🌧️ [비: 도크 차수판 발판]\n({ruleTag})";
                    break;
                case GameEventType.BadWeather_Typhoon:
                    titleText = $"🌪️ [태풍: 창문보강 발판]\n({ruleTag})";
                    break;
                case GameEventType.BadWeather_Snow:
                    titleText = $"❄️ [눈: 외부 통로 제설 발판]\n({ruleTag})";
                    break;
            }

            zoneTitleLabel.text = titleText;
            zoneTitleLabel.color = Color.black;

            bool isActive = (EventSystem.Instance != null && EventSystem.Instance.ActiveEvent == targetWeatherEvent);
            if (isActive)
            {
                float pct = EventSystem.Instance.MissionProgressPercent;
                progressLabel.text = $"▶ 진행도: {pct:F0}% / 100% ◀";
            }
            else
            {
                progressLabel.text = "[대기 중]";
            }
            progressLabel.color = Color.black;
        }

        protected override void OnTick(PlayerController p)
        {
            // 연속 진행도(0~100%) 처리는 Update()에서 프레임 단위로 정밀 제어합니다.
        }
    }
}
