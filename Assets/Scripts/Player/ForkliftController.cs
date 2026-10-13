using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Facilities;
using APCenter.UI;

namespace APCenter.Player
{
    /// <summary>
    /// 독립 탈것 오브젝트로 동작하는 지게차 컨트롤러 (ForkliftController):
    /// - 창고 '북동쪽 구석(North-East: 13.5, 0, 12.5)'에 기본 스폰됩니다.
    /// - 시설 업그레이드에서 '지게차 시스템 해금'(hasForklift) 전에는 탑승 불가(미해금 텍스트 출력), 해금 후 탑승 가능합니다.
    /// - 지게차 앞쪽 포크(forkPoint)에 팔렛트가 기본 고정 부착되어 있습니다.
    /// - 도크에 파렛트를 상차한 뒤에도 원위치로 텔레포트되거나 플레이어가 강제 하차되지 않고 그 자리에서 계속 탑승/조작을 유지합니다.
    /// - 지게차 구역(ForkliftZone)에 진입하면 언제든(1~8개 미완성 파렛트 포함) 자유롭게 하차할 수 있습니다.
    /// </summary>
    public class ForkliftController : MonoBehaviour
    {
        public static ForkliftController Instance { get; private set; }

        [Header("Transform Anchor Points")]
        public Transform driverSeatPoint;
        public Transform forkPoint;

        [Header("Balance & Movement Penalty (75% of Barehand Speed)")]
        [SerializeField] private float speedRatioToPlayer = 0.75f;
        [SerializeField] private float turnSpeed = 8.5f;

        [Header("Home Zone (Warehouse North-East Corner)")]
        public Vector3 homeZonePosition = new Vector3(13.5f, 0f, 12.5f);

        public bool IsDriving { get; private set; } = false;
        public PalletItem CurrentForkPallet { get; private set; }

        private PlayerController currentDriver;
        private GameObject seatedDriverVisual;
        private TextMesh forkliftProgressLabel;

        private float collectTimer = 0f;
        private float noticeCooldown = 0f;
        private float mountStateCooldown = 0f;
        private bool hasLeftZoneWhileDriving = false;

        public bool IsUnlocked => (GameManager.Instance != null) && GameManager.Instance.hasForklift;

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
            BuildForkliftVehicleVisuals();
            EnsureForkPalletExists();
            RefreshProgressUI();

            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnUpgradeChanged += RefreshProgressUI;
            }
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnUpgradeChanged -= RefreshProgressUI;
            }
        }

        private void BuildForkliftVehicleVisuals()
        {
            if (transform.Find("ForkliftBodyRoot") != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            Material yellowMat = new Material(shader) { color = new Color(0.96f, 0.76f, 0.10f) };
            Material darkMetalMat = new Material(shader) { color = new Color(0.20f, 0.21f, 0.23f) };
            Material tireMat = new Material(shader) { color = new Color(0.12f, 0.12f, 0.12f) };
            Material seatMat = new Material(shader) { color = new Color(0.28f, 0.28f, 0.32f) };

            GameObject bodyRoot = new GameObject("ForkliftBodyRoot");
            bodyRoot.transform.SetParent(transform, false);
            bodyRoot.transform.localPosition = Vector3.zero;

            GameObject chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chassis.name = "Chassis";
            chassis.transform.SetParent(bodyRoot.transform, false);
            chassis.transform.localPosition = new Vector3(0f, 0.42f, -0.15f);
            chassis.transform.localScale = new Vector3(1.25f, 0.55f, 1.55f);
            Destroy(chassis.GetComponent<Collider>());
            chassis.GetComponent<Renderer>().material = yellowMat;

            GameObject counterWeight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            counterWeight.name = "CounterWeight";
            counterWeight.transform.SetParent(bodyRoot.transform, false);
            counterWeight.transform.localPosition = new Vector3(0f, 0.50f, -0.82f);
            counterWeight.transform.localScale = new Vector3(1.20f, 0.65f, 0.35f);
            Destroy(counterWeight.GetComponent<Collider>());
            counterWeight.GetComponent<Renderer>().material = darkMetalMat;

            Vector3[] wheelOffsets = new Vector3[]
            {
                new Vector3(-0.62f, 0.22f, 0.38f),
                new Vector3(0.62f, 0.22f, 0.38f),
                new Vector3(-0.58f, 0.20f, -0.68f),
                new Vector3(0.58f, 0.20f, -0.68f)
            };
            for (int i = 0; i < wheelOffsets.Length; i++)
            {
                GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = $"Wheel_{i}";
                wheel.transform.SetParent(bodyRoot.transform, false);
                wheel.transform.localPosition = wheelOffsets[i];
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                wheel.transform.localScale = new Vector3(0.42f, 0.12f, 0.42f);
                Destroy(wheel.GetComponent<Collider>());
                wheel.GetComponent<Renderer>().material = tireMat;
            }

            GameObject seat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seat.name = "DriverSeat";
            seat.transform.SetParent(bodyRoot.transform, false);
            seat.transform.localPosition = new Vector3(0f, 0.82f, -0.22f);
            seat.transform.localScale = new Vector3(0.55f, 0.32f, 0.50f);
            Destroy(seat.GetComponent<Collider>());
            seat.GetComponent<Renderer>().material = seatMat;

            Vector3[] pillarPos = new Vector3[]
            {
                new Vector3(-0.52f, 1.25f, 0.35f),
                new Vector3(0.52f, 1.25f, 0.35f),
                new Vector3(-0.52f, 1.25f, -0.65f),
                new Vector3(0.52f, 1.25f, -0.65f)
            };
            for (int i = 0; i < pillarPos.Length; i++)
            {
                GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = $"CabinPillar_{i}";
                pillar.transform.SetParent(bodyRoot.transform, false);
                pillar.transform.localPosition = pillarPos[i];
                pillar.transform.localScale = new Vector3(0.08f, 1.15f, 0.08f);
                Destroy(pillar.GetComponent<Collider>());
                pillar.GetComponent<Renderer>().material = darkMetalMat;
            }

            GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "CabinRoof";
            roof.transform.SetParent(bodyRoot.transform, false);
            roof.transform.localPosition = new Vector3(0f, 1.85f, -0.15f);
            roof.transform.localScale = new Vector3(1.16f, 0.08f, 1.12f);
            Destroy(roof.GetComponent<Collider>());
            roof.GetComponent<Renderer>().material = yellowMat;

            for (int i = -1; i <= 1; i += 2)
            {
                GameObject mastRail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mastRail.name = $"MastRail_{i}";
                mastRail.transform.SetParent(bodyRoot.transform, false);
                mastRail.transform.localPosition = new Vector3(i * 0.36f, 0.95f, 0.68f);
                mastRail.transform.localScale = new Vector3(0.10f, 1.55f, 0.12f);
                Destroy(mastRail.GetComponent<Collider>());
                mastRail.GetComponent<Renderer>().material = darkMetalMat;

                GameObject forkBlade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                forkBlade.name = $"ForkBlade_{i}";
                forkBlade.transform.SetParent(bodyRoot.transform, false);
                forkBlade.transform.localPosition = new Vector3(i * 0.34f, 0.18f, 1.22f);
                forkBlade.transform.localScale = new Vector3(0.14f, 0.07f, 1.15f);
                Destroy(forkBlade.GetComponent<Collider>());
                forkBlade.GetComponent<Renderer>().material = darkMetalMat;
            }

            if (driverSeatPoint == null)
            {
                GameObject seatPointObj = new GameObject("driverSeatPoint");
                seatPointObj.transform.SetParent(transform, false);
                seatPointObj.transform.localPosition = new Vector3(0f, 0.85f, -0.18f);
                seatPointObj.transform.localRotation = Quaternion.identity;
                driverSeatPoint = seatPointObj.transform;
            }

            seatedDriverVisual = new GameObject("SeatedOperatorVisual");
            seatedDriverVisual.transform.SetParent(driverSeatPoint, false);
            seatedDriverVisual.transform.localPosition = Vector3.zero;

            Material operatorMat = new Material(shader) { color = new Color(0.22f, 0.55f, 0.92f) };
            Material helmetMat = new Material(shader) { color = new Color(1.0f, 0.85f, 0.15f) };

            GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            torso.name = "OperatorTorso";
            torso.transform.SetParent(seatedDriverVisual.transform, false);
            torso.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            torso.transform.localScale = new Vector3(0.42f, 0.32f, 0.42f);
            Destroy(torso.GetComponent<Collider>());
            torso.GetComponent<Renderer>().material = operatorMat;

            GameObject helmet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            helmet.name = "SafetyHelmet";
            helmet.transform.SetParent(seatedDriverVisual.transform, false);
            helmet.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            helmet.transform.localScale = new Vector3(0.36f, 0.32f, 0.38f);
            Destroy(helmet.GetComponent<Collider>());
            helmet.GetComponent<Renderer>().material = helmetMat;

            seatedDriverVisual.SetActive(false);

            if (forkPoint == null)
            {
                GameObject forkPointObj = new GameObject("forkPoint");
                forkPointObj.transform.SetParent(transform, false);
                forkPointObj.transform.localPosition = new Vector3(0f, 0.22f, 1.25f);
                forkPointObj.transform.localRotation = Quaternion.identity;
                forkPoint = forkPointObj.transform;
            }

            GameObject lblObj = new GameObject("ForkliftProgressText");
            lblObj.transform.SetParent(transform, false);
            lblObj.transform.localPosition = new Vector3(0f, 2.45f, 0.2f);
            lblObj.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            forkliftProgressLabel = lblObj.AddComponent<TextMesh>();
            forkliftProgressLabel.fontSize = 45;
            forkliftProgressLabel.characterSize = 0.08f;
            forkliftProgressLabel.alignment = TextAlignment.Center;
            forkliftProgressLabel.anchor = TextAnchor.MiddleCenter;
            forkliftProgressLabel.color = Color.black;
            Billboard.Attach(lblObj);
        }

        public void EnsureForkPalletExists()
        {
            if (CurrentForkPallet != null) return;

            if (forkPoint == null)
            {
                BuildForkliftVehicleVisuals();
            }

            GameObject palletObj = new GameObject("ForkPalletItem");
            palletObj.transform.SetParent(forkPoint, false);
            palletObj.transform.localPosition = Vector3.zero;
            palletObj.transform.localRotation = Quaternion.identity;

            CurrentForkPallet = palletObj.AddComponent<PalletItem>();
            CurrentForkPallet.InitializeEmptyPallet();
            CurrentForkPallet.SetLabelVisible(false);
            RefreshProgressUI();
        }

        private void Update()
        {
            if (noticeCooldown > 0f) noticeCooldown -= Time.deltaTime;
            if (mountStateCooldown > 0f) mountStateCooldown -= Time.deltaTime;

            EnsureForkPalletExists();

            if (IsDriving && currentDriver != null)
            {
                HandleDrivingMovement();
                HandleForkliftCollection();

                float distToHome = Vector3.Distance(
                    new Vector3(transform.position.x, 0f, transform.position.z),
                    new Vector3(homeZonePosition.x, 0f, homeZonePosition.z)
                );

                if (distToHome > 3.2f)
                {
                    hasLeftZoneWhileDriving = true;
                }

                // 북동쪽 지게차 구역(ForkliftZone)에 재진입하거나 구역 내에서 Space/F 입력 시 자유 하차 (1~8개 미완성 포함 언제든 하차 허용)
                if (distToHome <= 2.3f && mountStateCooldown <= 0f)
                {
                    if (hasLeftZoneWhileDriving || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.F))
                    {
                        DismountPlayerAtHomeZone();
                    }
                }
            }
            else
            {
                // 해금된 상태라면 하차 중에도 맨몸으로 동일 목적지 택배를 포크 팔렛트에 올릴 수 있음
                if (IsUnlocked)
                {
                    HandleOnFootDepositToForkPallet();
                }
            }
        }

        private void LateUpdate()
        {
            if (forkliftProgressLabel != null && Camera.main != null)
            {
                forkliftProgressLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }

        /// <summary>
        /// 지게차 시스템 해금 여부를 확인하고, 해금된 경우에만 운전 모드로 탑승합니다.
        /// 미해금 시에는 탑승을 차단하고 미해금 안내 텍스트를 출력합니다.
        /// </summary>
        public bool TryMountPlayer(PlayerController player)
        {
            if (player == null || IsDriving || mountStateCooldown > 0f) return false;

            // [해금 조건 검사]: 해금 전에는 지게차 탑승 불가 (미해금 텍스트 출력)
            if (!IsUnlocked)
            {
                ShowThrottledNotice("🔒 [지게차 미해금] 시설 업그레이드 창에서 '지게차 시스템 해금'을 구매해야 탑승할 수 있습니다!");
                return false;
            }

            EnsureForkPalletExists();

            currentDriver = player;
            IsDriving = true;
            hasLeftZoneWhileDriving = false;
            mountStateCooldown = 0.7f;

            if (seatedDriverVisual != null)
            {
                seatedDriverVisual.SetActive(true);
            }

            player.EnterForkliftDriveMode(this, driverSeatPoint);

            RefreshProgressUI();
            SoundManager.Instance?.PlayPickup();

            string statusMsg = (CurrentForkPallet != null && CurrentForkPallet.CurrentBoxCount > 0)
                ? $"🚜 [지게차 탑승] 포크 위 기존 파렛트({CurrentForkPallet.GetProgressStatusText()})를 이어서 수집합니다!"
                : "🚜 [지게차 탑승] 포크에 기본 장착된 팔렛트에 동일 목적지 택배 9개를 수집하세요!";
            UIManager.Instance?.ShowNotice(statusMsg);

            return true;
        }

        public void DismountPlayerAtHomeZone()
        {
            if (!IsDriving || currentDriver == null) return;

            transform.position = new Vector3(homeZonePosition.x, 0f, homeZonePosition.z);
            transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            Vector3 safeExitPos = new Vector3(homeZonePosition.x - 2.3f, 0f, homeZonePosition.z);
            ExecutePlayerDismount(safeExitPos);

            if (CurrentForkPallet != null && CurrentForkPallet.CurrentBoxCount > 0 && !CurrentForkPallet.IsReady)
            {
                UIManager.Instance?.ShowNotice($"🚶 [지게차 하차] 미완성 파렛트({CurrentForkPallet.GetProgressStatusText()})가 포크 위에 안전하게 유지됩니다.");
            }
            else
            {
                UIManager.Instance?.ShowNotice("🚶 [지게차 하차] 운전 모드를 종료했습니다.");
            }
        }

        /// <summary>
        /// [상차 후 탑승 유지]:
        /// 도크에 파렛트를 실은 뒤 지게차가 원위치로 텔레포트되거나 플레이어가 강제 하차되지 않고,
        /// 그 자리에서 계속 탑승/조작을 유지하며 새 빈 팔렛트가 포크에 준비됩니다.
        /// </summary>
        public void OnPalletLoadedToTruck(PalletItem loadedPallet)
        {
            if (CurrentForkPallet == loadedPallet)
            {
                CurrentForkPallet = null;
            }

            // 강제 하차나 원위치 텔레포트 없이 즉시 새 빈 팔렛트를 포크에 준비하고 탑승/조작 상태 그대로 유지!
            EnsureForkPalletExists();
            RefreshProgressUI();
        }

        private void ExecutePlayerDismount(Vector3 exitWorldPos)
        {
            if (currentDriver == null) return;

            PlayerController p = currentDriver;
            currentDriver = null;
            IsDriving = false;
            hasLeftZoneWhileDriving = false;
            mountStateCooldown = 0.85f;

            if (seatedDriverVisual != null)
            {
                seatedDriverVisual.SetActive(false);
            }

            p.ExitForkliftDriveMode(exitWorldPos);
            RefreshProgressUI();

            if (ForkliftZone.Instance != null)
            {
                ForkliftZone.Instance.NotifyPlayerDismounted();
            }
        }

        private void HandleDrivingMovement()
        {
            float moveX = Input.GetAxisRaw("Horizontal");
            float moveZ = Input.GetAxisRaw("Vertical");

            if (currentDriver != null && currentDriver.JoystickInput.sqrMagnitude > 0.05f)
            {
                moveX = currentDriver.JoystickInput.x;
                moveZ = currentDriver.JoystickInput.y;
            }

            Vector3 moveDir = new Vector3(moveX, 0f, moveZ).normalized;
            if (moveDir.sqrMagnitude < 0.01f) return;

            float barehandSpeed = (GameManager.Instance != null) ? GameManager.Instance.GetPlayerMoveSpeed() : 7.0f;
            float forkliftSpeed = barehandSpeed * speedRatioToPlayer;

            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * turnSpeed);

            Vector3 nextPos = transform.position + moveDir * forkliftSpeed * Time.deltaTime;
            nextPos.x = Mathf.Clamp(nextPos.x, -16.5f, 16.5f);
            nextPos.z = Mathf.Clamp(nextPos.z, -14.0f, 15.5f);
            nextPos.y = 0f;

            transform.position = nextPos;
        }

        private void HandleForkliftCollection()
        {
            EnsureForkPalletExists();

            if (CurrentForkPallet.IsReady) return;

            collectTimer += Time.deltaTime;
            if (collectTimer < 0.15f) return;
            collectTimer = 0f;

            Vector3 collectCenter = (forkPoint != null) ? forkPoint.position : transform.position;

            // 1. 분류대(SortingStation / 남쪽 TakeZone) 근처에서 분류된 택배를 1개씩 포크 위 팔렛트로 적재
            var sortingStations = FindObjectsByType<SortingStation>(FindObjectsSortMode.None);
            foreach (var st in sortingStations)
            {
                if (st == null || !st.HasSortedPackages()) continue;

                float distToCenter = Vector3.Distance(collectCenter, st.transform.position);
                float distToTakeZone = Vector3.Distance(collectCenter, st.TakeZoneWorldPos);
                if (distToCenter < 3.6f || distToTakeZone < 2.6f)
                {
                    if (CurrentForkPallet.CurrentBoxCount > 0 &&
                        CurrentForkPallet.Destination != PackageDestination.None &&
                        st.targetDestination != CurrentForkPallet.Destination)
                    {
                        ShowThrottledNotice(
                            $"🚫 [동일 목적지 엄수] 현재 [{PalletItem.GetDestinationKoreanName(CurrentForkPallet.Destination)}] 파렛트({CurrentForkPallet.CurrentBoxCount}/9) 적재 중입니다! 다른 지역 택배는 섞을 수 없습니다."
                        );
                        return;
                    }

                    PackageItem pkg = st.PopSortedPackage();
                    if (pkg != null)
                    {
                        if (pkg.IsDefective)
                        {
                            st.SortPackage(pkg);
                            ShowThrottledNotice("⚠️ 불량 택배는 파렛트에 적재할 수 없습니다!");
                            return;
                        }

                        AddPackageToForkPallet(pkg);
                        return;
                    }
                }
            }

            // 2. 바닥의 택배(Unloaded / ReInbounded) 근처 접근 시 1개씩 포크 위 팔렛트로 적재
            var allPackages = FindObjectsByType<PackageItem>(FindObjectsSortMode.None);
            PackageItem nearestValid = null;
            PackageItem nearestWrongDest = null;
            float minD = 3.2f;

            foreach (var pkg in allPackages)
            {
                if (pkg == null || pkg is PalletItem || pkg.IsDefective) continue;
                if (pkg.transform.parent == null && (pkg.State == PackageState.Unloaded || pkg.State == PackageState.ReInbounded))
                {
                    float d = Vector3.Distance(collectCenter, pkg.transform.position);
                    if (d < minD)
                    {
                        if (CurrentForkPallet.CurrentBoxCount == 0 ||
                            CurrentForkPallet.Destination == PackageDestination.None ||
                            pkg.Destination == CurrentForkPallet.Destination)
                        {
                            minD = d;
                            nearestValid = pkg;
                        }
                        else
                        {
                            nearestWrongDest = pkg;
                        }
                    }
                }
            }

            if (nearestValid != null)
            {
                AddPackageToForkPallet(nearestValid);
            }
            else if (nearestWrongDest != null)
            {
                ShowThrottledNotice(
                    $"🚫 [동일 목적지 엄수] 현재 [{PalletItem.GetDestinationKoreanName(CurrentForkPallet.Destination)}] 파렛트({CurrentForkPallet.CurrentBoxCount}/9) 적재 중입니다!"
                );
            }
        }

        private void HandleOnFootDepositToForkPallet()
        {
            if (PlayerController.Instance == null || PlayerController.Instance.IsDrivingForklift) return;
            var p = PlayerController.Instance;
            if (p.Stack == null || p.Stack.Count == 0) return;
            if (CurrentForkPallet == null || CurrentForkPallet.IsReady) return;

            Vector3 checkPos = (forkPoint != null) ? forkPoint.position : transform.position;
            if (Vector3.Distance(p.transform.position, checkPos) > 2.4f) return;

            collectTimer += Time.deltaTime;
            if (collectTimer < 0.15f) return;
            collectTimer = 0f;

            if (CurrentForkPallet.CurrentBoxCount == 0 || CurrentForkPallet.Destination == PackageDestination.None)
            {
                PackageItem top = p.Stack.Peek();
                if (top != null && !(top is PalletItem) && !top.IsDefective)
                {
                    AddPackageToForkPallet(p.Stack.Pop());
                }
            }
            else
            {
                PackageItem match = p.Stack.FindMatching(CurrentForkPallet.Destination);
                if (match != null && !match.IsDefective)
                {
                    AddPackageToForkPallet(match);
                }
                else
                {
                    ShowThrottledNotice(
                        $"🚫 [동일 목적지 엄수] 지게차 포크 위 [{PalletItem.GetDestinationKoreanName(CurrentForkPallet.Destination)}] 파렛트({CurrentForkPallet.CurrentBoxCount}/9)에는 같은 지역 택배만 올릴 수 있습니다!"
                    );
                }
            }
        }

        private void AddPackageToForkPallet(PackageItem pkg)
        {
            if (pkg == null || CurrentForkPallet == null) return;

            bool added = CurrentForkPallet.TryAddPackage(pkg, () =>
            {
                string dKor = PalletItem.GetDestinationKoreanName(CurrentForkPallet.Destination);
                UIManager.Instance?.ShowNotice(
                    $"📦 [{dKor} 파렛트 9 / 9 결속 완료!] 지게차 포크(forkPoint)에 결속되었습니다. {dKor} 출고 도크로 이동하세요!"
                );
            });

            if (added)
            {
                RefreshProgressUI();
            }
        }

        private void ShowThrottledNotice(string msg)
        {
            if (noticeCooldown <= 0f)
            {
                noticeCooldown = 1.8f;
                SoundManager.Instance?.PlayError();
                UIManager.Instance?.ShowNotice(msg);
            }
        }

        public void RefreshProgressUI()
        {
            if (forkliftProgressLabel == null) return;

            forkliftProgressLabel.color = Color.black;

            if (!IsUnlocked)
            {
                forkliftProgressLabel.text = "🔒 지게차 [미해금 - 탑승 불가]\n(시설 업그레이드에서 '지게차 시스템 해금' 필요)";
                return;
            }

            if (CurrentForkPallet == null || CurrentForkPallet.CurrentBoxCount == 0 || CurrentForkPallet.Destination == PackageDestination.None)
            {
                forkliftProgressLabel.text = IsDriving
                    ? "🚜 지게차 운전 중 [0 / 9]\n(분류대/입고장 접근 시 포크 팔렛트에 적재)"
                    : "🚜 지게차 대기 [0 / 9]";
                return;
            }

            string dKor = PalletItem.GetDestinationKoreanName(CurrentForkPallet.Destination);
            int cnt = CurrentForkPallet.CurrentBoxCount;

            if (CurrentForkPallet.IsReady)
            {
                forkliftProgressLabel.text = $"[{dKor}] {cnt} / {PalletItem.RequiredPackageCount} ★결속완료★\n({dKor} 출고 도크 상차 가능)";
            }
            else
            {
                forkliftProgressLabel.text = $"[{dKor}] {cnt} / {PalletItem.RequiredPackageCount}";
            }
        }
    }

    /// <summary>
    /// 창고 북동쪽 구석(North-East) 지게차 탑승 및 자유 하차 구역 발판 (ForkliftZone):
    /// - 해금 전에는 미해금 텍스트를 출력하며 탑승할 수 없고, 해금 후 빈손 진입 시 탑승 가능합니다.
    /// - 운전 중 다시 이 구역에 진입하면 파렛트 적재 수량(1~8개 미완성 포함)과 무관하게 언제든 하차할 수 있습니다.
    /// </summary>
    public class ForkliftZone : InteractionZone
    {
        public static ForkliftZone Instance { get; private set; }

        public Vector3 zonePosition = new Vector3(13.5f, 0f, 12.5f);
        private TextMesh zoneLabel;
        private bool playerSteppedOffSinceDismount = true;

        private void Awake()
        {
            Instance = this;
            zoneType = ZoneType.SortedPickup;
        }

        private void Start()
        {
            BuildForkliftZoneVisuals();
            EnsureForkliftVehicleSpawned();
            RefreshZoneLabel();
        }

        private void BuildForkliftZoneVisuals()
        {
            transform.position = zonePosition;
            if (transform.Find("ForkliftPadVisual") != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");

            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "ForkliftPadVisual";
            pad.transform.SetParent(transform, false);
            pad.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            pad.transform.localScale = new Vector3(3.4f, 0.08f, 3.4f);
            Destroy(pad.GetComponent<Collider>());
            pad.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.95f, 0.78f, 0.12f) };

            var col = GetComponent<BoxCollider>();
            if (col == null) col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(3.4f, 2.0f, 3.4f);
            col.center = new Vector3(0f, 1.0f, 0f);
            col.isTrigger = true;

            GameObject lblObj = new GameObject("ForkliftZoneLabel");
            lblObj.transform.SetParent(transform, false);
            lblObj.transform.localPosition = new Vector3(0f, 0.65f, -1.55f);
            lblObj.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            zoneLabel = lblObj.AddComponent<TextMesh>();
            zoneLabel.fontSize = 45;
            zoneLabel.characterSize = 0.08f;
            zoneLabel.alignment = TextAlignment.Center;
            zoneLabel.anchor = TextAnchor.MiddleCenter;
            zoneLabel.color = Color.black;
            Billboard.Attach(lblObj);
        }

        private void EnsureForkliftVehicleSpawned()
        {
            if (ForkliftController.Instance != null)
            {
                ForkliftController.Instance.homeZonePosition = zonePosition;
                ForkliftController.Instance.transform.position = new Vector3(zonePosition.x, 0f, zonePosition.z);
                return;
            }

            GameObject fkObj = new GameObject("ForkliftVehicle");
            fkObj.transform.position = new Vector3(zonePosition.x, 0f, zonePosition.z);
            fkObj.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var fk = fkObj.AddComponent<ForkliftController>();
            fk.homeZonePosition = zonePosition;
        }

        public void NotifyPlayerDismounted()
        {
            playerSteppedOffSinceDismount = false;
        }

        private void Update()
        {
            RefreshZoneLabel();

            PlayerController p = PlayerController.Instance;
            if (p == null || ForkliftController.Instance == null) return;

            if (p.IsDrivingForklift) return;

            float dist = Vector3.Distance(
                new Vector3(p.transform.position.x, 0f, p.transform.position.z),
                new Vector3(transform.position.x, 0f, transform.position.z)
            );

            if (dist > 2.4f)
            {
                playerSteppedOffSinceDismount = true;
                return;
            }

            if (playerSteppedOffSinceDismount && dist <= 1.95f && p.Stack != null && p.Stack.Count == 0)
            {
                if (ForkliftController.Instance.TryMountPlayer(p))
                {
                    playerSteppedOffSinceDismount = false;
                }
            }
        }

        private void LateUpdate()
        {
            if (zoneLabel != null && Camera.main != null)
            {
                zoneLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }

        protected override void OnTick(PlayerController p)
        {
        }

        private void RefreshZoneLabel()
        {
            if (zoneLabel == null) return;

            zoneLabel.color = Color.black;

            bool unlocked = (GameManager.Instance != null) && GameManager.Instance.hasForklift;
            if (!unlocked)
            {
                zoneLabel.text = "🔒 지게차 구역 [미해금]\n(업그레이드 창에서 '지게차 시스템 해금' 후 탑승 가능)";
                return;
            }

            if (ForkliftController.Instance != null && ForkliftController.Instance.IsDriving)
            {
                zoneLabel.text = "▼ 지게차 구역 (진입 시 자유 하차) ▼\n[미완성 파렛트 수량 유지됨]";
            }
            else
            {
                zoneLabel.text = "▼ 지게차 탑승/하차 구역 (북동쪽) ▼\n[기본 팔렛트 장착 - 빈손 진입 시 탑승]";
            }
        }
    }
}
