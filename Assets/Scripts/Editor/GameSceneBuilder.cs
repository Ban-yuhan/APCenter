using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Facilities;
using APCenter.Player;
using APCenter.Worker;
using APCenter.UI;

namespace APCenter.Editor
{
    public static class GameSceneBuilder
    {
        [MenuItem("APCenter/Build Main Scene")]
        public static void GenerateMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. 머티리얼 준비
            Material floorMat = CreateColoredMaterial("Mat_Floor", new Color(0.28f, 0.32f, 0.38f));
            Material roadMat = CreateColoredMaterial("Mat_Road", new Color(0.18f, 0.20f, 0.22f));
            Material wallMat = CreateColoredMaterial("Mat_Wall", new Color(0.85f, 0.88f, 0.92f));
            Material yellowLineMat = CreateColoredMaterial("Mat_YellowLine", new Color(0.95f, 0.80f, 0.15f));
            Material redMat = CreateColoredMaterial("Mat_Red", new Color(0.92f, 0.25f, 0.25f));
            Material blueMat = CreateColoredMaterial("Mat_Blue", new Color(0.18f, 0.49f, 0.96f));
            Material greenMat = CreateColoredMaterial("Mat_Green", new Color(0.22f, 0.78f, 0.35f));
            Material orangeMat = CreateColoredMaterial("Mat_Orange", new Color(0.95f, 0.55f, 0.15f));
            Material purpleMat = CreateColoredMaterial("Mat_Purple", new Color(0.65f, 0.32f, 0.88f));

            // 2. 조명 & 시간 시스템
            GameObject dirLightObj = new GameObject("Directional Light");
            Light lightComp = dirLightObj.AddComponent<Light>();
            lightComp.type = LightType.Directional;
            lightComp.intensity = 1.1f;
            lightComp.color = new Color(1.0f, 0.96f, 0.9f);
            lightComp.shadows = LightShadows.Soft;
            dirLightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 3. 환경 (창고 바닥, 도로, 벽, 도크)
            GameObject envRoot = new GameObject("Environment");

            // 센터 메인 바닥 (36 x 36)
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "WarehouseFloor";
            floor.transform.SetParent(envRoot.transform);
            floor.transform.position = new Vector3(0f, 0f, 0f);
            floor.transform.localScale = new Vector3(3.6f, 1f, 3.6f);
            floor.GetComponent<Renderer>().material = floorMat;

            // 외부 도로 (입출고 트럭 이동용 도로)
            GameObject roadNorth = GameObject.CreatePrimitive(PrimitiveType.Plane);
            roadNorth.name = "InboundRoad";
            roadNorth.transform.SetParent(envRoot.transform);
            roadNorth.transform.position = new Vector3(0f, -0.01f, 17f);
            roadNorth.transform.localScale = new Vector3(6f, 1f, 0.8f);
            roadNorth.GetComponent<Renderer>().material = roadMat;

            GameObject roadSouth = GameObject.CreatePrimitive(PrimitiveType.Plane);
            roadSouth.name = "OutboundRoad";
            roadSouth.transform.SetParent(envRoot.transform);
            roadSouth.transform.position = new Vector3(0f, -0.01f, -17f);
            roadSouth.transform.localScale = new Vector3(6f, 1f, 0.8f);
            roadSouth.GetComponent<Renderer>().material = roadMat;

            // 창고 벽체들
            CreateWall(envRoot.transform, new Vector3(0f, 2.5f, 18f), new Vector3(36f, 5f, 0.5f), wallMat);
            CreateWall(envRoot.transform, new Vector3(0f, 2.5f, -18f), new Vector3(36f, 5f, 0.5f), wallMat);
            CreateWall(envRoot.transform, new Vector3(-18f, 2.5f, 0f), new Vector3(0.5f, 5f, 36f), wallMat);
            CreateWall(envRoot.transform, new Vector3(18f, 2.5f, 0f), new Vector3(0.5f, 5f, 36f), wallMat);

            // 도크 셔터 도어 (입고문)
            GameObject dockDoor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dockDoor.name = "InboundShutterDoor";
            dockDoor.transform.SetParent(envRoot.transform);
            dockDoor.transform.position = new Vector3(-8f, 2.2f, 17.8f);
            dockDoor.transform.localScale = new Vector3(5f, 4.4f, 0.3f);
            dockDoor.GetComponent<Renderer>().material = yellowLineMat;

            // 4. 입고 구역 & 입고 트럭
            GameObject inboundArea = new GameObject("InboundArea");
            inboundArea.transform.position = new Vector3(-8f, 0f, 12f);

            // 언로딩 패드 (미분류 박스 떨어지는 곳)
            GameObject unloadPad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            unloadPad.name = "UnloadPad";
            unloadPad.transform.SetParent(inboundArea.transform);
            unloadPad.transform.position = new Vector3(-8f, 0.05f, 12f);
            unloadPad.transform.localScale = new Vector3(6f, 0.1f, 6f);
            unloadPad.GetComponent<Renderer>().material = yellowLineMat;
            var unloadZone = unloadPad.AddComponent<InteractionZone>();
            unloadZone.zoneType = ZoneType.UnloadPickup;
            unloadPad.GetComponent<Collider>().isTrigger = true;

            // 입고 트럭
            GameObject inTruckObj = CreateTruckModel("InboundTruck", new Color(0.9f, 0.3f, 0.2f));
            inTruckObj.transform.position = new Vector3(-8f, 0f, 17f);
            var inTruck = inTruckObj.AddComponent<InboundTruck>();
            inTruck.dockDoor = dockDoor.transform;
            inTruck.unloadPad = unloadPad.transform;

            // 5. 3개 목적지 분류대 (빨강 Seoul, 파랑 Busan, 초록 Daegu)
            GameObject sortingArea = new GameObject("SortingArea");

            // 서울대 (Red)
            var stationRed = CreateSortingStation("Station_Seoul_Red", new Vector3(-7f, 0f, 2f), PackageDestination.Seoul, redMat, sortingArea.transform);
            // 부산대 (Blue)
            var stationBlue = CreateSortingStation("Station_Busan_Blue", new Vector3(0f, 0f, 2f), PackageDestination.Busan, blueMat, sortingArea.transform);
            // 대구대 (Green)
            var stationGreen = CreateSortingStation("Station_Daegu_Green", new Vector3(7f, 0f, 2f), PackageDestination.Daegu, greenMat, sortingArea.transform);

            // 5-1. 분류 구역 우측 파렛트 작업대 (PalletStation)
            GameObject palletStationObj = new GameObject("PalletStation_Workbench");
            palletStationObj.transform.SetParent(sortingArea.transform);
            var palletStation = palletStationObj.AddComponent<PalletStation>();
            palletStation.stationPosition = new Vector3(13f, 0f, 2f);

            // 6. 반품 및 검수 구역 (Return Station)
            GameObject returnObj = new GameObject("ReturnStationArea");
            returnObj.transform.position = new Vector3(-12f, 0f, -4f);

            GameObject returnPad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            returnPad.name = "ReturnInspectionPad";
            returnPad.transform.SetParent(returnObj.transform);
            returnPad.transform.position = new Vector3(-12f, 0.05f, -4f);
            returnPad.transform.localScale = new Vector3(4f, 0.1f, 4f);
            returnPad.GetComponent<Renderer>().material = purpleMat;
            returnPad.GetComponent<Collider>().isTrigger = true;

            var returnStation = returnPad.AddComponent<ReturnStation>();
            returnStation.zoneType = ZoneType.ReturnStation;

            // 폐기장 쓰레기통
            GameObject scrapBin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            scrapBin.name = "ScrapBin";
            scrapBin.transform.SetParent(returnObj.transform);
            scrapBin.transform.position = new Vector3(-14.5f, 0.7f, -4f);
            scrapBin.transform.localScale = new Vector3(1.2f, 0.7f, 1.2f);
            scrapBin.GetComponent<Renderer>().material = CreateColoredMaterial("Mat_DarkGray", Color.black);
            SetPrivateField(returnStation, "scrapBin", scrapBin.transform);

            // 정상 재입고 패드
            GameObject reinboundPad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            reinboundPad.name = "ReinboundPad";
            reinboundPad.transform.SetParent(returnObj.transform);
            reinboundPad.transform.position = new Vector3(-12f, 0.06f, -1.5f);
            reinboundPad.transform.localScale = new Vector3(2f, 0.1f, 2f);
            reinboundPad.GetComponent<Renderer>().material = yellowLineMat;
            SetPrivateField(returnStation, "reinboundPad", reinboundPad.transform);

            // 라벨 텍스트
            Create3DLabel("반품/검수구역\n[오분류/불량]", new Vector3(-12f, 2.0f, -4f), returnObj.transform, Color.black);

            // 7. 남쪽 출고 도크 & [서울, 부산, 대구] 전용 차량 3대 및 LoadingZone 발판 3개
            GameObject outboundArea = new GameObject("OutboundArea");
            outboundArea.transform.position = new Vector3(0f, 0f, -12f);

            PackageDestination[] dockDests = { PackageDestination.Seoul, PackageDestination.Busan, PackageDestination.Daegu };
            float[] dockXs = { -7f, 0f, 7f };
            OutboundTruck outTruck = null;

            for (int i = 0; i < 3; i++)
            {
                Color truckColor = PackageItem.GetDestinationColor(dockDests[i]);
                GameObject tObj = CreateTruckModel($"OutboundTruck_{dockDests[i]}", truckColor);
                tObj.transform.SetParent(outboundArea.transform);
                var tComp = tObj.AddComponent<OutboundTruck>();
                tComp.ConfigureDock(dockDests[i], new Vector3(dockXs[i], 0f, -16f));
                if (i == 0) outTruck = tComp;

                GameObject lzObj = new GameObject($"LoadingZone_{dockDests[i]}");
                lzObj.transform.SetParent(outboundArea.transform);
                var lzComp = lzObj.AddComponent<LoadingZone>();
                lzComp.InitializeZone(dockDests[i], tComp, new Vector3(dockXs[i], 0.05f, -11.2f));
            }

            //// 8. 기상 악화 복구 존 (Weather Task Zone)
            //GameObject weatherZoneObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            //weatherZoneObj.name = "WeatherTaskZone";
            //weatherZoneObj.transform.position = new Vector3(0f, 0.05f, -6f);
            //weatherZoneObj.transform.localScale = new Vector3(3f, 0.1f, 3f);
            //weatherZoneObj.GetComponent<Renderer>().material = CreateColoredMaterial("Mat_Cyan", new Color(0.2f, 0.8f, 0.9f));
            //weatherZoneObj.GetComponent<Collider>().isTrigger = true;
            //var weatherZone = weatherZoneObj.AddComponent<WeatherTaskZone>();
            //weatherZone.zoneType = ZoneType.WeatherTask;
            //Create3DLabel("기상악화 대응 존\n[제설/차수판/발전기]", new Vector3(0f, 1.8f, -6f), weatherZoneObj.transform, Color.cyan);

            // 9. 플레이어 캐릭터 생성
            GameObject playerObj = CreatePlayerCharacter(new Vector3(0f, 0f, -2f));

            // 10. 메인 카메라 & 추적
            GameObject cameraObj = new GameObject("Main Camera");
            cameraObj.tag = "MainCamera";
            Camera cam = cameraObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 55;
            cameraObj.AddComponent<AudioListener>();
            var camCtrl = cameraObj.AddComponent<CameraController>();
            SetPrivateField(camCtrl, "target", playerObj.transform);

            // 11. 관리자 싱글톤 객체들
            GameObject managersObj = new GameObject("Managers");
            managersObj.AddComponent<SoundManager>();
            managersObj.AddComponent<EconomyManager>();
            var timeSys = managersObj.AddComponent<TimeSystem>();
            SetPrivateField(timeSys, "directionalLight", lightComp);
            managersObj.AddComponent<ReputationSystem>();
            managersObj.AddComponent<APCenter.Core.EventSystem>();
            managersObj.AddComponent<GameManager>();

            var workerMgr = managersObj.AddComponent<WorkerManager>();

            // WorkerController 캐싱 정보 바인딩
            WorkerController.InboundPadTrans = unloadPad.transform;
            WorkerController.StationRed = stationRed;
            WorkerController.StationBlue = stationBlue;
            WorkerController.StationGreen = stationGreen;
            WorkerController.ReturnFacility = returnStation;
            WorkerController.OutTruck = outTruck;

            // 12. UGUI Canvas 생성
            CreateGameUI(managersObj);

            // 씬 저장
            if (!Directory.Exists("Assets/Scenes")) Directory.CreateDirectory("Assets/Scenes");
            string scenePath = "Assets/Scenes/MainGame.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[GameSceneBuilder] Scene generated and saved to {scenePath}");

            // EditorBuildSettings에 등록
            EditorBuildSettings.scenes = new EditorBuildSettingsScene[]
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
        }

        private static Material CreateColoredMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            Material mat = new Material(shader);
            mat.name = name;
            mat.color = color;

            string dir = "Assets/Materials";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string assetPath = $"{dir}/{name}.mat";
            AssetDatabase.CreateAsset(mat, assetPath);
            return mat;
        }

        private static void CreateWall(Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.SetParent(parent);
            wall.transform.position = pos;
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().material = mat;
        }

        private static SortingStation CreateSortingStation(string name, Vector3 pos, PackageDestination dest, Material mat, Transform parent)
        {
            GameObject stationRoot = new GameObject(name);
            stationRoot.transform.SetParent(parent);
            stationRoot.transform.position = pos;

            // 분류 패드
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "StationPad";
            pad.transform.SetParent(stationRoot.transform);
            pad.transform.localPosition = Vector3.zero;
            pad.transform.localScale = new Vector3(3.2f, 0.1f, 3.2f);
            pad.GetComponent<Renderer>().material = mat;
            pad.GetComponent<Collider>().isTrigger = true;

            var station = pad.AddComponent<SortingStation>();
            station.targetDestination = dest;
            station.zoneType = (dest == PackageDestination.Seoul) ? ZoneType.SortingRed
                             : (dest == PackageDestination.Busan) ? ZoneType.SortingBlue
                             : ZoneType.SortingGreen;

            // 라벨 기둥 & 텍스트
            string destName = (dest == PackageDestination.Seoul) ? "서울/수도권 [RED]"
                            : (dest == PackageDestination.Busan) ? "부산/영남 [BLUE]"
                            : "대구/충청 [GREEN]";

            Create3DLabel(destName, pos + new Vector3(0f, 2.2f, 0f), stationRoot.transform, mat.color);

            return station;
        }

        private static GameObject CreateTruckModel(string name, Color color)
        {
            GameObject truck = new GameObject(name);

            // 캐빈 (앞부분)
            GameObject cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = "Cabin";
            cabin.transform.SetParent(truck.transform);
            cabin.transform.localPosition = new Vector3(0f, 1.2f, 2.2f);
            cabin.transform.localScale = new Vector3(2.4f, 2.2f, 1.8f);
            Material cabinMat = CreateColoredMaterial($"Mat_{name}_Cabin", color);
            cabin.GetComponent<Renderer>().material = cabinMat;

            // 화물 컨테이너 (뒷부분)
            GameObject container = GameObject.CreatePrimitive(PrimitiveType.Cube);
            container.name = "Container";
            container.transform.SetParent(truck.transform);
            container.transform.localPosition = new Vector3(0f, 1.6f, -1.2f);
            container.transform.localScale = new Vector3(2.8f, 2.8f, 5.0f);
            Material contMat = CreateColoredMaterial($"Mat_{name}_Cont", new Color(0.9f, 0.9f, 0.92f));
            container.GetComponent<Renderer>().material = contMat;

            // 바퀴 4개
            for (int i = 0; i < 4; i++)
            {
                GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = $"Wheel_{i}";
                wheel.transform.SetParent(truck.transform);
                float x = (i % 2 == 0) ? -1.4f : 1.4f;
                float z = (i < 2) ? 1.8f : -2.0f;
                wheel.transform.localPosition = new Vector3(x, 0.45f, z);
                wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
                wheel.transform.localScale = new Vector3(0.9f, 0.3f, 0.9f);
                wheel.GetComponent<Renderer>().material = CreateColoredMaterial("Mat_Wheel", Color.black);
            }

            return truck;
        }

        private static GameObject CreatePlayerCharacter(Vector3 pos)
        {
            GameObject root = new GameObject("Player");
            root.transform.position = pos;

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.45f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform);
            visual.transform.localPosition = Vector3.zero;

            // 몸체 (Capsule)
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(visual.transform);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            Material playerMat = CreateColoredMaterial("Mat_Player", new Color(0.15f, 0.65f, 0.95f));
            body.GetComponent<Renderer>().material = playerMat;

            // 안전 조끼 (노란 띠)
            var vest = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            vest.name = "Vest";
            vest.transform.SetParent(visual.transform);
            vest.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            vest.transform.localScale = new Vector3(0.85f, 0.3f, 0.85f);
            UnityEngine.Object.DestroyImmediate(vest.GetComponent<Collider>());
            vest.GetComponent<Renderer>().material = CreateColoredMaterial("Mat_Vest", new Color(0.98f, 0.85f, 0.1f));

            root.AddComponent<PackageStack>();
            root.AddComponent<PlayerController>();

            return root;
        }

        private static void Create3DLabel(string text, Vector3 pos, Transform parent, Color color)
        {
            GameObject labelObj = new GameObject("3DLabel_" + text.Split('\n')[0]);
            labelObj.transform.SetParent(parent);
            labelObj.transform.position = pos;
            var tm = labelObj.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 45;
            tm.characterSize = 0.08f;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = color;
            labelObj.transform.rotation = Quaternion.Euler(50, 0, 0); // 카메라 각도에 맞춤
            labelObj.AddComponent<Billboard>();
        }

        private static void CreateGameUI(GameObject managersObj)
        {
            // EventSystem 생성
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<StandaloneInputModule>();

            // Canvas 생성
            GameObject canvasObj = new GameObject("Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();

            Font defaultFont = null;
            try { defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }

            // 1. 상단 HUD 바 패널
            GameObject topHUD = CreateUIPanel("TopHUD", canvasObj.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(0f, 100f), new Color(0.12f, 0.14f, 0.18f, 0.92f));

            Text moneyTxt = CreateUIText("MoneyText", topHUD.transform, "₩ 1,500", 32, TextAnchor.MiddleLeft, new Vector2(0.02f, 0.5f), new Vector2(250f, 60f), Color.yellow, defaultFont);
            Text profitTxt = CreateUIText("NetProfitText", topHUD.transform, "순이익: +₩0/분", 20, TextAnchor.MiddleLeft, new Vector2(0.20f, 0.5f), new Vector2(400f, 60f), Color.white, defaultFont);
            Text timeTxt = CreateUIText("TimeText", topHUD.transform, "☀️ 08:00 (Day 1)", 24, TextAnchor.MiddleCenter, new Vector2(0.50f, 0.5f), new Vector2(260f, 60f), Color.white, defaultFont);
            Text repTxt = CreateUIText("ReputationText", topHUD.transform, "평판: ★★★☆☆ (50%)", 22, TextAnchor.MiddleRight, new Vector2(0.75f, 0.5f), new Vector2(280f, 60f), new Color(0.3f, 0.9f, 1.0f), defaultFont);
            Text tierTxt = CreateUIText("TierText", topHUD.transform, "🏢 Tier 1: 작은 창고", 20, TextAnchor.MiddleRight, new Vector2(0.96f, 0.5f), new Vector2(220f, 60f), new Color(0.9f, 0.8f, 0.3f), defaultFont);

            // 2. 이벤트 배너 패널
            GameObject eventBanner = CreateUIPanel("EventBanner", canvasObj.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -115f), new Vector2(750f, 50f), new Color(0.85f, 0.25f, 0.25f, 0.95f));
            Text eventTxt = CreateUIText("EventText", eventBanner.transform, "🔥 [피버타임: 앙팡데이] 물량 및 수익 2배 폭증!", 20, TextAnchor.MiddleLeft, new Vector2(0.04f, 0.5f), new Vector2(550f, 40f), Color.white, defaultFont);
            Text eventTimerTxt = CreateUIText("EventTimerText", eventBanner.transform, "45.0s", 20, TextAnchor.MiddleRight, new Vector2(0.95f, 0.5f), new Vector2(120f, 40f), Color.yellow, defaultFont);
            eventBanner.SetActive(false);

            // 3. 출고 트럭 출발 버튼 (우측 하단)
            GameObject truckBtnObj = CreateUIButton("TruckDepartButton", canvasObj.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(360f, 80f), new Color(0.2f, 0.6f, 0.9f));
            Text truckBtnTxt = CreateUIText("Label", truckBtnObj.transform, "🚚 출고 출발 대기 중... (0/18)", 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(340f, 70f), Color.white, defaultFont);

            // 4. 하단 명령바 버튼들 (좌측 하단 메뉴)
            GameObject workerBtnObj = CreateUIButton("OpenWorkerButton", canvasObj.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(220f, 70f), new Color(0.9f, 0.5f, 0.15f));
            CreateUIText("Label", workerBtnObj.transform, "👥 알바 관리 / 고용", 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(200f, 60f), Color.white, defaultFont);

            GameObject upgradeBtnObj = CreateUIButton("OpenUpgradeButton", canvasObj.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(280f, 40f), new Vector2(220f, 70f), new Color(0.2f, 0.7f, 0.4f));
            CreateUIText("Label", upgradeBtnObj.transform, "⚡ 시설 업그레이드", 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(200f, 60f), Color.white, defaultFont);

            // 5. 알바 관리 팝업 패널
            GameObject workerPanel = CreateUIPanel("WorkerPanel", canvasObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 600f), new Color(0.15f, 0.17f, 0.22f, 0.98f));
            CreateUIText("Title", workerPanel.transform, "👥 알바 직원 관리 & 채용", 28, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.93f), new Vector2(600f, 50f), Color.white, defaultFont);
            Text workerListTxt = CreateUIText("ListText", workerPanel.transform, "", 18, TextAnchor.UpperLeft, new Vector2(0.08f, 0.85f), new Vector2(600f, 320f), Color.white, defaultFont);

            GameObject hireNormalBtn = CreateUIButton("HireNormalBtn", workerPanel.transform, new Vector2(0.22f, 0.18f), new Vector2(0.22f, 0.18f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 55f), new Color(0.3f, 0.6f, 0.9f));
            CreateUIText("Label", hireNormalBtn.transform, "정규알바 고용\n(₩350)", 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(180f, 50f), Color.white, defaultFont);

            GameObject hireTempBtn = CreateUIButton("HireTempBtn", workerPanel.transform, new Vector2(0.52f, 0.18f), new Vector2(0.52f, 0.18f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 55f), new Color(0.9f, 0.3f, 0.3f));
            CreateUIText("Label", hireTempBtn.transform, "⚡대타/용역 고용\n(₩800)", 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(180f, 50f), Color.white, defaultFont);

            GameObject cycleRoleBtn = CreateUIButton("CycleRoleBtn", workerPanel.transform, new Vector2(0.80f, 0.18f), new Vector2(0.80f, 0.18f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 55f), new Color(0.7f, 0.4f, 0.9f));
            CreateUIText("Label", cycleRoleBtn.transform, "역할 일괄 재배치", 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(150f, 50f), Color.white, defaultFont);

            GameObject closeWorkerBtn = CreateUIButton("CloseWorkerBtn", workerPanel.transform, new Vector2(0.5f, 0.07f), new Vector2(0.5f, 0.07f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180f, 45f), new Color(0.5f, 0.5f, 0.5f));
            CreateUIText("Label", closeWorkerBtn.transform, "닫기", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(160f, 40f), Color.white, defaultFont);

            // 6. 업그레이드 팝업 패널
            GameObject upgPanel = CreateUIPanel("UpgradePanel", canvasObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 600f), new Color(0.15f, 0.17f, 0.22f, 0.98f));
            CreateUIText("Title", upgPanel.transform, "⚡ 센터 시설 & 장비 업그레이드", 28, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.93f), new Vector2(600f, 50f), Color.white, defaultFont);
            Text upgListTxt = CreateUIText("ListText", upgPanel.transform, "", 20, TextAnchor.UpperLeft, new Vector2(0.08f, 0.85f), new Vector2(600f, 260f), Color.white, defaultFont);

            GameObject upgSpeedBtn = CreateUIButton("UpgSpeedBtn", upgPanel.transform, new Vector2(0.25f, 0.32f), new Vector2(0.25f, 0.32f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 50f), new Color(0.2f, 0.6f, 0.9f));
            CreateUIText("Label", upgSpeedBtn.transform, "이동 속도 강화", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(240f, 45f), Color.white, defaultFont);

            GameObject upgCapBtn = CreateUIButton("UpgCapBtn", upgPanel.transform, new Vector2(0.75f, 0.32f), new Vector2(0.75f, 0.32f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 50f), new Color(0.2f, 0.6f, 0.9f));
            CreateUIText("Label", upgCapBtn.transform, "스택 용량 확장", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(240f, 45f), Color.white, defaultFont);

            GameObject upgPalletBtn = CreateUIButton("UpgPalletBtn", upgPanel.transform, new Vector2(0.25f, 0.20f), new Vector2(0.25f, 0.20f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 50f), new Color(0.8f, 0.5f, 0.2f));
            CreateUIText("Label", upgPalletBtn.transform, "파렛트 시스템 해금", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(240f, 45f), Color.white, defaultFont);

            GameObject upgTierBtn = CreateUIButton("UpgTierBtn", upgPanel.transform, new Vector2(0.75f, 0.20f), new Vector2(0.75f, 0.20f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250f, 50f), new Color(0.2f, 0.8f, 0.4f));
            CreateUIText("Label", upgTierBtn.transform, "시설 등급 성장", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(240f, 45f), Color.white, defaultFont);

            GameObject closeUpgBtn = CreateUIButton("CloseUpgBtn", upgPanel.transform, new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180f, 45f), new Color(0.5f, 0.5f, 0.5f));
            CreateUIText("Label", closeUpgBtn.transform, "닫기", 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(160f, 40f), Color.white, defaultFont);

            // 7. 알림 / 영수증 팝업 패널
            GameObject noticePanel = CreateUIPanel("NoticePopup", canvasObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(600f, 220f), new Color(0.1f, 0.1f, 0.14f, 0.95f));
            Text noticeTxt = CreateUIText("NoticeText", noticePanel.transform, "알림 내용", 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.6f), new Vector2(560f, 120f), Color.white, defaultFont);
            GameObject closeNoticeBtn = CreateUIButton("CloseNoticeBtn", noticePanel.transform, new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(140f, 40f), new Color(0.4f, 0.4f, 0.4f));
            CreateUIText("Label", closeNoticeBtn.transform, "확인", 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(120f, 35f), Color.white, defaultFont);
            noticePanel.SetActive(false);

            // 8. 가상 조이스틱 (좌측 하단)
            GameObject joystickBg = CreateUIPanel("VirtualJoystick", canvasObj.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(160f, 220f), new Vector2(160f, 160f), new Color(1f, 1f, 1f, 0.25f));
            GameObject joystickHandle = CreateUIPanel("Handle", joystickBg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f), new Color(1f, 1f, 1f, 0.65f));
            joystickBg.AddComponent<VirtualJoystick>();

            // 9. TopHUDUI (좌측 상단 컴팩트 이벤트 표시 & 우측 상단 상시 앙팡데이 수동 버튼)
            var topHudUI = canvasObj.AddComponent<TopHUDUI>();
            topHudUI.InitializeSubHUD(canvasObj.transform);

            // UIManager 컴포넌트에 바인딩
            var uiMgr = managersObj.AddComponent<UIManager>();
            SetPrivateField(uiMgr, "moneyText", moneyTxt);
            SetPrivateField(uiMgr, "netProfitText", profitTxt);
            SetPrivateField(uiMgr, "timeText", timeTxt);
            SetPrivateField(uiMgr, "reputationText", repTxt);
            SetPrivateField(uiMgr, "facilityTierText", tierTxt);

            SetPrivateField(uiMgr, "eventBannerPanel", eventBanner);
            SetPrivateField(uiMgr, "eventBannerText", eventTxt);
            SetPrivateField(uiMgr, "eventTimerText", eventTimerTxt);

            SetPrivateField(uiMgr, "truckDepartButton", truckBtnObj.GetComponent<Button>());
            SetPrivateField(uiMgr, "truckDepartButtonText", truckBtnTxt);

            SetPrivateField(uiMgr, "openWorkerPanelButton", workerBtnObj.GetComponent<Button>());
            SetPrivateField(uiMgr, "openUpgradePanelButton", upgradeBtnObj.GetComponent<Button>());

            SetPrivateField(uiMgr, "workerPanel", workerPanel);
            SetPrivateField(uiMgr, "workerListText", workerListTxt);
            SetPrivateField(uiMgr, "closeWorkerPanelButton", closeWorkerBtn.GetComponent<Button>());
            SetPrivateField(uiMgr, "hireNormalWorkerButton", hireNormalBtn.GetComponent<Button>());
            SetPrivateField(uiMgr, "hireTempWorkerButton", hireTempBtn.GetComponent<Button>());
            SetPrivateField(uiMgr, "reassignAllNextRoleButton", cycleRoleBtn.GetComponent<Button>());

            SetPrivateField(uiMgr, "upgradePanel", upgPanel);
            SetPrivateField(uiMgr, "upgradeListText", upgListTxt);
            SetPrivateField(uiMgr, "closeUpgradePanelButton", closeUpgBtn.GetComponent<Button>());
            SetPrivateField(uiMgr, "upgradeSpeedButton", upgSpeedBtn.GetComponent<Button>());
            SetPrivateField(uiMgr, "upgradeCapacityButton", upgCapBtn.GetComponent<Button>());
            SetPrivateField(uiMgr, "unlockPalletButton", upgPalletBtn.GetComponent<Button>());
            SetPrivateField(uiMgr, "upgradeTierButton", upgTierBtn.GetComponent<Button>());

            SetPrivateField(uiMgr, "noticePopupPanel", noticePanel);
            SetPrivateField(uiMgr, "noticePopupText", noticeTxt);
            SetPrivateField(uiMgr, "closeNoticeButton", closeNoticeBtn.GetComponent<Button>());
        }

        private static GameObject CreateUIPanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color color)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
            Image img = obj.AddComponent<Image>();
            img.color = color;
            return obj;
        }

        private static GameObject CreateUIButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color color)
        {
            GameObject obj = CreateUIPanel(name, parent, anchorMin, anchorMax, pivot, anchoredPos, size, color);
            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.highlightedColor = color * 1.15f;
            cb.pressedColor = color * 0.85f;
            btn.colors = cb;
            return obj;
        }

        private static Text CreateUIText(string name, Transform parent, string text, int fontSize, TextAnchor alignment, Vector2 anchor, Vector2 size, Color color, Font font)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            Text txt = obj.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.color = color;
            if (font != null) txt.font = font;
            return txt;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            if (target == null) return;
            Type t = target.GetType();
            FieldInfo fi = t.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (fi != null)
            {
                fi.SetValue(target, value);
            }
        }

        [MenuItem("APCenter/Perform Standalone Build")]
        public static void PerformBuild()
        {
            if (!File.Exists("Assets/Scenes/MainGame.unity"))
            {
                GenerateMainScene();
            }

            string buildDir = "Builds";
            if (!Directory.Exists(buildDir)) Directory.CreateDirectory(buildDir);

            string exePath = Path.Combine(buildDir, "APCenter_Game.exe");
            Debug.Log($"[GameSceneBuilder] Starting Standalone Windows64 build to: {exePath}");

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = new[] { "Assets/Scenes/MainGame.unity" };
            buildPlayerOptions.locationPathName = exePath;
            buildPlayerOptions.target = BuildTarget.StandaloneWindows64;
            buildPlayerOptions.options = BuildOptions.None;

            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[GameSceneBuilder] BUILD SUCCEEDED! Total size: {report.summary.totalSize} bytes");
            }
            else
            {
                Debug.LogError($"[GameSceneBuilder] BUILD FAILED with {report.summary.totalErrors} errors!");
            }
        }
    }
}
