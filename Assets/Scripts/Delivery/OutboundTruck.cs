using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.Facilities;
using APCenter.UI;

namespace APCenter.Delivery
{
    /// <summary>
    /// 남쪽 도크에 배치되는 목적지 전용 출고 차량 (서울, 부산, 대구 독립 3대).
    /// - 각 차량은 물리적 적재 슬롯을 최대 10칸(maxSlots = 10) 가집니다.
    /// - 5칸 이상 적재 시 출발 가능하며, 만차(10칸) 시 퍼펙트 보너스를 받고 출발합니다.
    /// - 파렛트(PalletItem)는 물리적 슬롯을 '1칸'만 차지하지만, 정산 시 DeliveryUnits = 9가 반영됩니다.
    /// - 3대의 차량은 각각 독립적인 출발/복귀 타이머를 가집니다.
    /// </summary>
    public class OutboundTruck : MonoBehaviour
    {
        public static readonly List<OutboundTruck> AllTrucks = new List<OutboundTruck>();
        private static bool fleetInitialized = false;

        [Header("Dedicated Destination")]
        public PackageDestination targetDestination = PackageDestination.Seoul;
        public bool HasAssignedDestination => targetDestination != PackageDestination.None;

        [Header("Physical Slot Capacity (Max 10 Slots)")]
        public int maxSlots = 10;          // 최대 10칸 물리적 적재 슬롯
        public int minSlotsToDepart = 5;   // 5칸 이상 적재 시 출발 가능
        public int MaxCapacity => maxSlots;

        [Header("Independent Timers")]
        public float autoDepartDelay = 6.0f;    // 5칸 이상 적재 후 자동 출발까지 대기 타이머
        public float returnWaitDuration = 5.0f; // 출고 후 빈 차 복귀 대기 타이머
        public float CurrentDepartTimer { get; private set; } = 0f;
        public float CurrentReturnTimer { get; private set; } = 0f;

        [Header("Cargo Bay Transform")]
        [SerializeField] private Transform cargoBayRoot;
        [SerializeField] private Renderer containerRenderer;
        [SerializeField] private Renderer cabinRenderer;
        [SerializeField] private TextMesh destinationLabel;

        [Header("Economics")]
        [SerializeField] private int basePricePerPackage = 120;
        [SerializeField] private int fuelCost = 250;

        [Header("Waypoints")]
        private Vector3 dockWaitPos = new Vector3(-7f, 0f, -16f);
        private Vector3 roadExitJunction = new Vector3(-7f, 0f, -20f);
        private Vector3 roadExitFar = new Vector3(35f, 0f, -20f);
        private Vector3 roadSpawnFar = new Vector3(-28f, 0f, -20f);
        private bool dockConfigured = false;

        private readonly List<PackageItem> loadedPackages = new List<PackageItem>();

        public int LoadedSlotCount => loadedPackages.Count;

        /// <summary>
        /// 정산 시 계산되는 총 택배 단위 수량 (일반 택배 = 1, PalletItem = 9)
        /// </summary>
        public int TotalDeliveryUnits
        {
            get
            {
                int units = 0;
                for (int i = 0; i < loadedPackages.Count; i++)
                {
                    if (loadedPackages[i] != null)
                    {
                        units += Mathf.Max(1, loadedPackages[i].DeliveryUnits);
                    }
                }
                return units;
            }
        }

        public bool IsDeparting { get; private set; } = false;
        public bool IsReturning { get; private set; } = false;
        public bool IsReadyToDepart => !IsDeparting && !IsReturning && LoadedSlotCount >= minSlotsToDepart;
        public bool IsFull => LoadedSlotCount >= maxSlots;
        public float LoadRatio => Mathf.Clamp01((float)LoadedSlotCount / maxSlots);

        public event Action<float> OnLoadRatioChanged;
        public event Action<long, int, bool> OnSettlementCompleted;

        public static OutboundTruck GetTruckForDestination(PackageDestination dest)
        {
            for (int i = 0; i < AllTrucks.Count; i++)
            {
                var t = AllTrucks[i];
                if (t != null && t.targetDestination == dest)
                {
                    return t;
                }
            }
            return AllTrucks.Count > 0 ? AllTrucks[0] : null;
        }

        private void Awake()
        {
            if (!AllTrucks.Contains(this))
            {
                AllTrucks.Add(this);
            }

            var colliders = GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                col.isTrigger = true;
            }
        }

        private void OnDestroy()
        {
            AllTrucks.Remove(this);
            if (AllTrucks.Count == 0)
            {
                fleetInitialized = false;
            }
        }

        private void Start()
        {
            EnsureCargoBayAndVisuals();

            // 기존 씬에 단일 트럭만 있을 경우 자동으로 [서울, 부산, 대구] 3대 도크와 LoadingZone, 파렛트 작업대를 구성
            if (!fleetInitialized)
            {
                fleetInitialized = true;
                InitializeThreeDockSystemIfNeeded();
            }

            if (!dockConfigured)
            {
                float defaultX = (targetDestination == PackageDestination.Seoul) ? -7f
                               : (targetDestination == PackageDestination.Busan) ? 0f : 7f;
                if (targetDestination == PackageDestination.None)
                {
                    targetDestination = PackageDestination.Seoul;
                    defaultX = -7f;
                }
                ConfigureDock(targetDestination, new Vector3(defaultX, 0f, -16f));
            }

            UpdateDestinationVisuals();
        }

        public void ConfigureDock(PackageDestination dest, Vector3 dockPos)
        {
            targetDestination = dest;
            dockWaitPos = dockPos;
            roadExitJunction = new Vector3(dockPos.x, 0f, -20f);
            roadExitFar = new Vector3(35f, 0f, -20f);
            roadSpawnFar = new Vector3(-28f, 0f, -20f);
            dockConfigured = true;

            transform.position = dockWaitPos;
            transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            EnsureCargoBayAndVisuals();
            UpdateDestinationVisuals();
        }

        private void EnsureCargoBayAndVisuals()
        {
            if (cargoBayRoot == null)
            {
                var bay = transform.Find("CargoBay");
                if (bay == null)
                {
                    var bayObj = new GameObject("CargoBay");
                    bayObj.transform.SetParent(transform, false);
                    bayObj.transform.localPosition = new Vector3(0f, 0.55f, -1.2f);
                    cargoBayRoot = bayObj.transform;
                }
                else
                {
                    cargoBayRoot = bay;
                }
            }

            // 화물칸을 개방형 평판/적재함 형태로 낮춰 10칸 적재 슬롯이 위에서 잘 보이도록 조정
            var cont = transform.Find("Container");
            if (cont != null)
            {
                containerRenderer = cont.GetComponent<Renderer>();
                cont.localPosition = new Vector3(0f, 0.55f, -1.2f);
                cont.localScale = new Vector3(2.6f, 0.35f, 4.8f);
            }

            var cab = transform.Find("Cabin");
            if (cab != null)
            {
                cabinRenderer = cab.GetComponent<Renderer>();
            }
        }

        private void Update()
        {
            // 독립 출발 타이머: 5칸 이상 적재 시 카운트다운 후 자동 출발, 만차(10칸) 시 즉시 출발
            if (!IsDeparting && !IsReturning && LoadedSlotCount >= minSlotsToDepart)
            {
                if (CurrentDepartTimer > 0f)
                {
                    CurrentDepartTimer -= Time.deltaTime;
                    UpdateDestinationVisuals();
                    if (CurrentDepartTimer <= 0f)
                    {
                        TryDepart();
                    }
                }
            }
        }

        private void LateUpdate()
        {
            if (destinationLabel != null && Camera.main != null)
            {
                destinationLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }

        public void UpdateDestinationVisuals()
        {
            Color destColor = PackageItem.GetDestinationColor(targetDestination);

            if (containerRenderer != null)
            {
                containerRenderer.material.color = destColor;
            }
            if (cabinRenderer != null)
            {
                cabinRenderer.material.color = Color.Lerp(destColor, Color.white, 0.2f);
            }

            if (destinationLabel == null)
            {
                var lblObj = transform.Find("DestLabel");
                if (lblObj == null)
                {
                    var go = new GameObject("DestLabel");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(0f, 3.3f, -1.0f);
                    destinationLabel = go.AddComponent<TextMesh>();
                    destinationLabel.fontSize = 45;
                    destinationLabel.characterSize = 0.08f;
                    destinationLabel.alignment = TextAlignment.Center;
                    destinationLabel.anchor = TextAnchor.MiddleCenter;
                    destinationLabel.color = Color.black;
                    Billboard.Attach(go);
                }
                else
                {
                    destinationLabel = lblObj.GetComponent<TextMesh>();
                    if (destinationLabel != null)
                    {
                        destinationLabel.fontSize = 45;
                        destinationLabel.color = Color.black;
                    }
                    Billboard.Attach(lblObj.gameObject);
                }
            }

            if (destinationLabel != null)
            {
                string dName = (targetDestination == PackageDestination.Seoul) ? "🔴 [서울 전용]"
                             : (targetDestination == PackageDestination.Busan) ? "🔵 [부산 전용]"
                             : "🟢 [대구 전용]";

                if (IsReturning)
                {
                    destinationLabel.text = $"{dName}\n⏳ 빈 차 복귀 중... ({CurrentReturnTimer:F1}s)";
                }
                else if (IsDeparting)
                {
                    destinationLabel.text = $"{dName}\n🚚 출고 출발 중! (슬롯 {LoadedSlotCount}/{maxSlots} | {TotalDeliveryUnits}단위)";
                }
                else if (IsFull)
                {
                    destinationLabel.text = $"{dName} ★만차 PERFECT!★\n슬롯: {LoadedSlotCount}/{maxSlots}칸 (정산: {TotalDeliveryUnits}개분)";
                }
                else if (IsReadyToDepart)
                {
                    destinationLabel.text = $"{dName} [출발 가능: {CurrentDepartTimer:F1}s]\n슬롯: {LoadedSlotCount}/{maxSlots}칸 (정산: {TotalDeliveryUnits}개분)";
                }
                else
                {
                    destinationLabel.text = $"{dName} 대기 중\n슬롯: {LoadedSlotCount}/{maxSlots}칸 (최소 {minSlotsToDepart}칸 필요)";
                }
                destinationLabel.color = Color.black;
            }
        }

        public bool CanLoadPackage(PackageItem package = null)
        {
            if (IsDeparting || IsReturning || LoadedSlotCount >= maxSlots) return false;
            if (package == null) return true;

            // 9개가 모두 모여 결속 완료된(IsReady && IsMerged) 파렛트만 상차 허용 (1~8개 미완성 엄격 차단)
            if (package is PalletItem pallet && (!pallet.IsMerged || !pallet.IsReady || pallet.CurrentBoxCount < PalletItem.RequiredPackageCount))
            {
                return false;
            }

            return package.Destination == targetDestination;
        }

        public bool LoadPackage(PackageItem package)
        {
            if (!CanLoadPackage(package)) return false;

            int slotIndex = loadedPackages.Count;
            loadedPackages.Add(package);

            // 10칸 물리적 슬롯 격자 배치 (2열 x 5행)
            int col = slotIndex % 2; // 0: 좌측, 1: 우측
            int row = slotIndex / 2; // 0 ~ 4 (5행)

            float spacingX = 1.05f;
            float spacingZ = 0.86f;

            Vector3 slotLocalPos = new Vector3(
                (col == 0 ? -0.5f : 0.5f) * spacingX,
                0.28f,
                (row - 2.0f) * spacingZ
            );

            package.State = PackageState.LoadedInTruck;
            if (package is PalletItem loadedPallet)
            {
                loadedPallet.RefreshPalletLabel();
            }

            package.FlyTo(cargoBayRoot, slotLocalPos, 0.22f, () =>
            {
                SoundManager.Instance?.PlayDrop();
            });

            // ⑤ [기업 대량주문] 진행 중일 때 상차/출고 시 DeliveryUnits(일반 택배 +1, 파렛트 +9) 즉시 카운트 연동
            if (EventSystem.Instance != null && EventSystem.Instance.ActiveEvent == GameEventType.CorporateOrder && !package.CountedInCorporateOrder)
            {
                package.CountedInCorporateOrder = true;
                EventSystem.Instance.AddCorporateOrderProgress(package.Destination, Mathf.Max(1, package.DeliveryUnits));
            }

            // 만차(10칸) 도달 시 즉시 퍼펙트 출발 타이머 설정, 5~9칸 적재 시 독립 출발 카운트다운 갱신
            if (LoadedSlotCount >= maxSlots)
            {
                CurrentDepartTimer = 0.4f;
            }
            else if (LoadedSlotCount >= minSlotsToDepart)
            {
                CurrentDepartTimer = autoDepartDelay;
            }

            UpdateDestinationVisuals();
            OnLoadRatioChanged?.Invoke(LoadRatio);
            return true;
        }

        public void TryDepart()
        {
            if (IsDeparting || IsReturning || !IsReadyToDepart) return;
            StartCoroutine(DepartAndReturnRoutine());
        }

        private IEnumerator DepartAndReturnRoutine()
        {
            IsDeparting = true;
            CurrentDepartTimer = 0f;
            SoundManager.Instance?.PlayTruckHorn();
            UpdateDestinationVisuals();

            int occupiedSlots = LoadedSlotCount;
            int totalUnits = TotalDeliveryUnits; // 슬롯과 수량 분리: PalletItem은 9단위 합산!
            bool isPerfect = (occupiedSlots >= maxSlots);

            float bonusMultiplier = 1.0f;

            if (ReputationSystem.Instance != null)
            {
                bonusMultiplier *= ReputationSystem.Instance.GetPriceMultiplier();
            }

            if (isPerfect)
            {
                bonusMultiplier *= 1.4f;
                ReputationSystem.Instance?.BonusPerfectLoad();
            }
            else
            {
                ReputationSystem.Instance?.BonusOnTimeDelivery();
            }

            if (EventSystem.Instance != null)
            {
                bonusMultiplier *= EventSystem.Instance.GetRevenueMultiplier();
            }

            int defectiveCount = 0;
            int uncountedCorporateUnits = 0;
            foreach (var pkg in loadedPackages)
            {
                if (pkg == null) continue;
                if (pkg.IsDefective) defectiveCount++;
                if (pkg.IsUrgent)
                {
                    EventSystem.Instance?.CompleteUrgentDelivery();
                }
                if (!pkg.CountedInCorporateOrder)
                {
                    pkg.CountedInCorporateOrder = true;
                    uncountedCorporateUnits += Mathf.Max(1, pkg.DeliveryUnits);
                }
            }

            if (uncountedCorporateUnits > 0 && EventSystem.Instance != null && EventSystem.Instance.ActiveEvent == GameEventType.CorporateOrder)
            {
                EventSystem.Instance.AddCorporateOrderProgress(targetDestination, uncountedCorporateUnits);
            }

            if (defectiveCount > 0)
            {
                for (int i = 0; i < defectiveCount; i++)
                {
                    ReputationSystem.Instance?.PenaltyDefectiveShipment();
                }
            }

            // 정산금 계산 시 물리적 슬롯 수가 아닌 DeliveryUnits 총합(totalUnits)을 기준으로 가산!
            long netMoney = EconomyManager.Instance.CalculateOutboundSettlement(
                totalUnits,
                basePricePerPackage,
                bonusMultiplier,
                fuelCost
            );

            EconomyManager.Instance.AddMoney(netMoney, true);
            OnSettlementCompleted?.Invoke(netMoney, totalUnits, isPerfect);

            string destName = (targetDestination == PackageDestination.Seoul) ? "서울행"
                            : (targetDestination == PackageDestination.Busan) ? "부산행" : "대구행";
            string perfectTag = isPerfect ? " [★만차 PERFECT 보너스!]" : "";
            UIManager.Instance?.ShowNotice(
                $"🚚 [{destName}] 출발!{perfectTag} 슬롯: {occupiedSlots}/{maxSlots}칸 (정산수량: {totalUnits}개분) ➔ +₩{netMoney:N0}");

            // 1. 도크에서 남쪽 도로로 진출
            float pullOutTime = 1.3f;
            float el = 0f;
            Vector3 startPos = transform.position;
            while (el < pullOutTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / pullOutTime);
                transform.position = Vector3.Lerp(startPos, roadExitJunction, t);
                yield return null;
            }
            transform.position = roadExitJunction;

            // 2. 오른쪽(+X) 도로 방향으로 회전
            float turnTime = 0.6f;
            el = 0f;
            Quaternion startRot = transform.rotation;
            Quaternion targetRot = Quaternion.Euler(0f, 90f, 0f);
            while (el < turnTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / turnTime);
                transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
                yield return null;
            }
            transform.rotation = targetRot;

            // 3. 우측 씬 밖으로 주행 퇴장
            float driveTime = 2.0f;
            el = 0f;
            startPos = transform.position;
            while (el < driveTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / driveTime);
                transform.position = Vector3.Lerp(startPos, roadExitFar, t);
                yield return null;
            }

            // 4. 적재 화물 정리 (빈 차 초기화)
            foreach (var pkg in loadedPackages)
            {
                if (pkg != null) Destroy(pkg.gameObject);
            }
            loadedPackages.Clear();
            OnLoadRatioChanged?.Invoke(0f);

            // 5. 차량별 독립 복귀 타이머 카운트다운
            IsReturning = true;
            CurrentReturnTimer = returnWaitDuration;
            while (CurrentReturnTimer > 0f)
            {
                CurrentReturnTimer -= Time.deltaTime;
                UpdateDestinationVisuals();
                yield return null;
            }
            CurrentReturnTimer = 0f;

            // 6. 좌측 도로에서 진입하여 자신의 전용 도크로 복귀
            transform.position = roadSpawnFar;
            transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            el = 0f;
            float driveInTime = 1.8f;
            while (el < driveInTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / driveInTime);
                transform.position = Vector3.Lerp(roadSpawnFar, roadExitJunction, t);
                yield return null;
            }

            el = 0f;
            float dockTime = 1.2f;
            while (el < dockTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / dockTime);
                transform.position = Vector3.Lerp(roadExitJunction, dockWaitPos, t);
                transform.rotation = Quaternion.Slerp(Quaternion.Euler(0f, 90f, 0f), Quaternion.Euler(0f, 180f, 0f), t);
                yield return null;
            }

            transform.position = dockWaitPos;
            transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            IsReturning = false;
            IsDeparting = false;
            UpdateDestinationVisuals();
        }

        // ─── 런타임 3대 독립 도크 & LoadingZone & 파렛트 작업대 자동 구성 ─────────────────────────

        private void InitializeThreeDockSystemIfNeeded()
        {
            // 기존 단일 OutboundLoadPad 비활성화 (새 LoadingZone 3개로 대체)
            var oldPad = GameObject.Find("OutboundLoadPad");
            if (oldPad != null && oldPad.GetComponent<LoadingZone>() == null)
            {
                oldPad.SetActive(false);
            }

            // 남쪽 도크 왼쪽부터 [서울(-7), 부산(0), 대구(+7)] 3대 배치
            PackageDestination[] dests = new PackageDestination[]
            {
                PackageDestination.Seoul,
                PackageDestination.Busan,
                PackageDestination.Daegu
            };
            float[] dockXs = new float[] { -7f, 0f, 7f };

            // 현재 오브젝트를 첫 번째(서울) 차량으로 설정 (이미 3대가 있다면 각각 매칭)
            var existingTrucks = FindObjectsByType<OutboundTruck>(FindObjectsSortMode.None);

            for (int i = 0; i < 3; i++)
            {
                OutboundTruck truck;
                if (i < existingTrucks.Length)
                {
                    truck = existingTrucks[i];
                }
                else
                {
                    // 현재 트럭 모델을 복제하여 부산/대구 차량 생성
                    GameObject clone = Instantiate(gameObject);
                    clone.name = $"OutboundTruck_{dests[i]}";
                    truck = clone.GetComponent<OutboundTruck>();
                }

                truck.gameObject.name = $"OutboundTruck_{dests[i]}";
                truck.ConfigureDock(dests[i], new Vector3(dockXs[i], 0f, -16f));

                // 도크 앞 상차 발판 (LoadingZone) 생성 또는 연결
                string zoneName = $"LoadingZone_{dests[i]}";
                GameObject zoneObj = GameObject.Find(zoneName);
                if (zoneObj == null)
                {
                    zoneObj = new GameObject(zoneName);
                    var lz = zoneObj.AddComponent<LoadingZone>();
                    lz.InitializeZone(dests[i], truck, new Vector3(dockXs[i], 0.05f, -11.2f));
                }
                else
                {
                    var lz = zoneObj.GetComponent<LoadingZone>() ?? zoneObj.AddComponent<LoadingZone>();
                    lz.InitializeZone(dests[i], truck, new Vector3(dockXs[i], 0.05f, -11.2f));
                }
            }

            // 분류 구역 우측에 파렛트 작업대(PalletZone) 및 지게차 탑승/하차 구역(ForkliftZone) 자동 배치
            PalletStation.EnsurePalletAndForkliftZonesExist();
        }
    }
}