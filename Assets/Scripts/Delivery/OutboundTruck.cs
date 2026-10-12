using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.UI;

namespace APCenter.Delivery
{
    public class OutboundTruck : MonoBehaviour
    {
        [Header("Target Destination")]
        public PackageDestination targetDestination = PackageDestination.None;

        // 💡 targetDestination이 None이 아니면 '목적지 확정' 상태로 간주
        public bool HasAssignedDestination => targetDestination != PackageDestination.None;

        [Header("Capacity & Grid")]
        public int gridWidth = 3;   // X
        public int gridHeight = 2;  // Y
        public int gridDepth = 3;   // Z
        public int MaxCapacity => gridWidth * gridHeight * gridDepth;

        [Header("Cargo Bay Transform")]
        [SerializeField] private Transform cargoBayRoot;
        [SerializeField] private Renderer containerRenderer;
        [SerializeField] private TextMesh destinationLabel;

        [Header("Economics")]
        [SerializeField] private int basePricePerPackage = 120;
        [SerializeField] private int fuelCost = 250;

        [Header("Waypoints")]
        private Vector3 dockWaitPos = new Vector3(8f, 0f, -16f);
        private Vector3 roadExitJunction = new Vector3(8f, 0f, -20f);
        private Vector3 roadExitFar = new Vector3(35f, 0f, -20f);
        private Vector3 roadSpawnFar = new Vector3(-28f, 0f, -20f);

        private List<PackageItem> loadedPackages = new List<PackageItem>();
        public bool IsDeparting { get; private set; } = false;
        public bool IsReadyToDepart => (float)loadedPackages.Count / MaxCapacity >= 0.5f;
        public float LoadRatio => Mathf.Clamp01((float)loadedPackages.Count / MaxCapacity);

        public event Action<float> OnLoadRatioChanged;
        public event Action<long, int, bool> OnSettlementCompleted;

        private void Awake()
        {
            var colliders = GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                col.isTrigger = true;
            }

            // 💡 시작 시 목적지 미정(None)으로 초기화
            targetDestination = PackageDestination.None;
            loadedPackages.Clear();
        }

        private void Start()
        {
            if (cargoBayRoot == null)
            {
                var bay = transform.Find("CargoBay");
                if (bay == null)
                {
                    var bayObj = new GameObject("CargoBay");
                    bayObj.transform.SetParent(transform);
                    bayObj.transform.localPosition = new Vector3(0f, 0.5f, -1.5f);
                    cargoBayRoot = bayObj.transform;
                }
                else
                {
                    cargoBayRoot = bay;
                }
            }

            transform.position = dockWaitPos;
            transform.rotation = Quaternion.Euler(0, 180, 0);

            targetDestination = PackageDestination.None;
            UpdateDestinationVisuals();
        }

        private void lateUpdate()
        {
            if (destinationLabel != null && Camera.main != null)
            {
                // 카메라 방향에 맞춰 텍스트 회전을 평평하게 정렬 (빌보드 효과)
                destinationLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }

        public void UpdateDestinationVisuals()
        {
            if (containerRenderer == null)
            {
                var cont = transform.Find("Container");
                if (cont != null) containerRenderer = cont.GetComponent<Renderer>();
            }

            if (destinationLabel == null)
            {
                var lblObj = transform.Find("DestLabel");
                if (lblObj == null)
                {
                    var go = new GameObject("DestLabel");
                    go.transform.SetParent(transform);
                    go.transform.localPosition = new Vector3(0f, 3.2f, -1.2f);
                    destinationLabel = go.AddComponent<TextMesh>();
                    destinationLabel.fontSize = 28;
                    destinationLabel.characterSize = 0.08f;
                    destinationLabel.alignment = TextAlignment.Center;
                    destinationLabel.anchor = TextAnchor.MiddleCenter;
                }
                else
                {
                    destinationLabel = lblObj.GetComponent<TextMesh>();
                }
            }

            // 💡 1. 목적지 미정 대기 상태
            if (targetDestination == PackageDestination.None)
            {
                if (containerRenderer != null)
                {
                    containerRenderer.material.color = new Color(0.7f, 0.7f, 0.7f);
                }

                if (destinationLabel != null)
                {
                    destinationLabel.text = "📦 상차 대기 중\n(첫 택배 시 목적지 확정)";
                    destinationLabel.color = Color.black; // 💡 2. 노란색 -> 검정색으로 수정
                }
                return;
            }

            // 💡 2. 목적지 확정 상태
            Color destColor = PackageItem.GetDestinationColor(targetDestination);

            if (containerRenderer != null)
            {
                containerRenderer.material.color = destColor;
            }

            if (destinationLabel != null)
            {
                string dName = "";
                switch (targetDestination)
                {
                    case PackageDestination.Seoul:
                        dName = "서울/수도권행";
                        break;
                    case PackageDestination.Busan:
                        dName = "부산/영남행";
                        break;
                    default:
                        dName = "대구/충청행";
                        break;
                }
                destinationLabel.text = dName;
                destinationLabel.color = Color.black; // 💡 확정 후에도 흰색 대신 검정색이 더 잘 보이시면 Color.black으로 유지 가능합니다.
            }
        }

        // 💡 [핵심 해결 지점] 적재 가능 조건 함수
        public bool CanLoadPackage(PackageItem package = null)
        {
            // 트럭이 출발 중이거나 적재 한도 초과 시 불가
            if (IsDeparting || loadedPackages.Count >= MaxCapacity) return false;

            // 검사할 택배 정보가 주어지지 않았다면 기본 자리 유무만 반환
            if (package == null) return true;

            // 택배 자체가 유효하지 않은 목적지(None)라면 적재 불가
            if (package.Destination == PackageDestination.None) return false;

            // 💡 1) 트럭 목적지가 미정(None)이면 -> 무슨 택배든(서울, 부산, 대구) 무조건 적재 허용!
            if (targetDestination == PackageDestination.None) return true;

            // 💡 2) 트럭 목적지가 확정된 상태라면 -> 목적지가 일치할 때만 적재 허용
            return package.Destination == targetDestination;
        }

        public bool LoadPackage(PackageItem package)
        {
            if (!CanLoadPackage(package)) return false;

            // 💡 트럭이 미정 상태일 때 첫 택배가 들어오면 해당 택배의 목적지로 즉시 확정!
            if (targetDestination == PackageDestination.None && package != null)
            {
                targetDestination = package.Destination;
                UpdateDestinationVisuals();
            }

            int index = loadedPackages.Count;
            loadedPackages.Add(package);

            int x = index % gridWidth;
            int y = (index / gridWidth) % gridHeight;
            int z = index / (gridWidth * gridHeight);

            float spacingX = 0.8f;
            float spacingY = 0.65f;
            float spacingZ = 0.8f;

            Vector3 gridLocalPos = new Vector3(
                (x - (gridWidth - 1) * 0.5f) * spacingX,
                y * spacingY,
                (z - (gridDepth - 1) * 0.5f) * spacingZ
            );

            package.State = PackageState.LoadedInTruck;
            package.FlyTo(cargoBayRoot, gridLocalPos, 0.25f, () =>
            {
                SoundManager.Instance?.PlayDrop();
            });

            OnLoadRatioChanged?.Invoke(LoadRatio);
            return true;
        }

        public void TryDepart()
        {
            if (IsDeparting || !IsReadyToDepart) return;
            StartCoroutine(DepartRoutine());
        }

        private IEnumerator DepartRoutine()
        {
            IsDeparting = true;
            SoundManager.Instance?.PlayTruckHorn();

            bool isPerfect = (loadedPackages.Count >= MaxCapacity);
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
            foreach (var pkg in loadedPackages)
            {
                if (pkg.IsDefective) defectiveCount++;
                if (pkg.IsUrgent)
                {
                    EventSystem.Instance?.CompleteUrgentDelivery();
                }
            }

            if (defectiveCount > 0)
            {
                for (int i = 0; i < defectiveCount; i++)
                {
                    ReputationSystem.Instance?.PenaltyDefectiveShipment();
                }
            }

            long netMoney = EconomyManager.Instance.CalculateOutboundSettlement(
                loadedPackages.Count,
                basePricePerPackage,
                bonusMultiplier,
                fuelCost
            );

            EconomyManager.Instance.AddMoney(netMoney, true);
            OnSettlementCompleted?.Invoke(netMoney, loadedPackages.Count, isPerfect);

            string destName = "";
            switch (targetDestination)
            {
                case PackageDestination.Seoul:
                    destName = "서울행";
                    break;
                case PackageDestination.Busan:
                    destName = "부산행";
                    break;
                default:
                    destName = "대구행";
                    break;
            }

            UIManager.Instance?.ShowNotice($"🚚 [{destName}] 차량 출발! 정산: +₩{netMoney:N0} (적재: {loadedPackages.Count}개)");

            // 주행 연출
            float pullOutTime = 1.6f;
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

            float turnTime = 0.8f;
            el = 0f;
            Quaternion startRot = transform.rotation;
            Quaternion targetRot = Quaternion.Euler(0, 90, 0);
            while (el < turnTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / turnTime);
                transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
                yield return null;
            }
            transform.rotation = targetRot;

            float driveTime = 2.4f;
            el = 0f;
            startPos = transform.position;
            while (el < driveTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / driveTime);
                transform.position = Vector3.Lerp(startPos, roadExitFar, t);
                yield return null;
            }

            // 화물 정리
            foreach (var pkg in loadedPackages)
            {
                if (pkg != null) Destroy(pkg.gameObject);
            }
            loadedPackages.Clear();
            OnLoadRatioChanged?.Invoke(0f);

            // 💡 다음 차량 오면 다시 '미정(None)'으로 초기화
            targetDestination = PackageDestination.None;
            UpdateDestinationVisuals();

            // 새 차량 진입
            yield return new WaitForSeconds(2.0f);
            transform.position = roadSpawnFar;
            transform.rotation = Quaternion.Euler(0, 90, 0);

            el = 0f;
            while (el < 2.0f)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / 2.0f);
                transform.position = Vector3.Lerp(roadSpawnFar, roadExitJunction, t);
                yield return null;
            }

            el = 0f;
            while (el < 1.4f)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / 1.4f);
                transform.position = Vector3.Lerp(roadExitJunction, dockWaitPos, t);
                transform.rotation = Quaternion.Slerp(Quaternion.Euler(0, 90, 0), Quaternion.Euler(0, 180, 0), t);
                yield return null;
            }
            transform.position = dockWaitPos;
            transform.rotation = Quaternion.Euler(0, 180, 0);

            IsDeparting = false;
        }
    }
}