using System;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Player;

namespace APCenter.Facilities
{
    /// <summary>
    /// 목적지별(서울, 부산, 대구) 분류 구역 (SortingStation):
    /// 트리거 영역을 북쪽(PutZone)과 남쪽(TakeZone) 2개로 물리적 분리합니다.
    /// ① [북쪽 - 입고 도크 방향] PutZone (내려놓기 전용):
    ///    - 손에 해당 지역 택배를 들고 진입 시 0.15초 간격으로 내려놓음 (빈손일 때 집기 금지).
    /// ② [남쪽 - 출고 도크 방향] TakeZone (집기 전용):
    ///    - 손에 빈자리가 있을 때 진입 시 0.15초 간격으로 택배를 집어 올림 (들고 있어도 내려놓기 금지).
    /// </summary>
    public class SortingStation : InteractionZone
    {
        [Header("Target Destination")]
        public PackageDestination targetDestination;

        [Header("Holding & Output Pad")]
        public Transform outputPad;
        private readonly List<PackageItem> sortedPackages = new List<PackageItem>();

        public SortingPutZone PutZone { get; private set; }
        public SortingTakeZone TakeZone { get; private set; }

        public Vector3 PutZoneWorldPos => (PutZone != null) ? PutZone.transform.position : (transform.position + new Vector3(0f, 0f, 1.9f));
        public Vector3 TakeZoneWorldPos => (TakeZone != null) ? TakeZone.transform.position : (transform.position + new Vector3(0f, 0f, -1.9f));

        public int SortedCount
        {
            get
            {
                CleanupSortedList();
                return sortedPackages.Count;
            }
        }

        private TextMesh centerCountLabel;

        private void Start()
        {
            BuildSplitZonesVisualsAndTriggers();
            UpdateCenterLabel();
        }

        private void BuildSplitZonesVisualsAndTriggers()
        {
            if (transform.Find("PutZone_North") != null) return;

            // 기존 단일 StationPad의 렌더러와 콜라이더를 비활성화하고 스케일을 (1,1,1)로 정규화하여 자식 트리거 왜곡 방지
            var oldRenderer = GetComponent<MeshRenderer>();
            if (oldRenderer != null) oldRenderer.enabled = false;
            var oldCol = GetComponent<Collider>();
            if (oldCol != null) oldCol.enabled = false;

            Vector3 worldPos = transform.position;
            transform.localScale = Vector3.one;
            transform.position = new Vector3(worldPos.x, 0f, worldPos.z);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            Color destColor = PackageItem.GetDestinationColor(targetDestination);
            string cityKor = PalletItem.GetDestinationKoreanName(targetDestination);

            // 0. 중앙 택배 적재 베이스 (PutZone과 TakeZone 사이 Z = 0)
            GameObject storageBase = GameObject.CreatePrimitive(PrimitiveType.Cube);
            storageBase.name = "SortedStoragePad";
            storageBase.transform.SetParent(transform, false);
            storageBase.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            storageBase.transform.localScale = new Vector3(3.2f, 0.08f, 1.8f);
            Destroy(storageBase.GetComponent<Collider>());
            storageBase.GetComponent<Renderer>().material = new Material(shader)
            {
                color = Color.Lerp(destColor, new Color(0.22f, 0.24f, 0.28f), 0.55f)
            };
            outputPad = storageBase.transform;

            // 중앙 수량 표시 텍스트 (Z값 0.5 감소: 0f -> -0.5f)
            GameObject centerLblObj = new GameObject("SortingCountLabel");
            centerLblObj.transform.SetParent(transform, false);
            centerLblObj.transform.localPosition = new Vector3(0f, 1.65f, -0.5f);
            centerLblObj.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            centerCountLabel = centerLblObj.AddComponent<TextMesh>();
            centerCountLabel.fontSize = 45;
            centerCountLabel.characterSize = 0.08f;
            centerCountLabel.alignment = TextAlignment.Center;
            centerCountLabel.anchor = TextAnchor.MiddleCenter;
            centerCountLabel.color = Color.black;
            Billboard.Attach(centerLblObj);

            // 부모(stationRoot)에 기존 3DLabel이 있다면 45pt 및 Billboard 적용 (지역 표시 외 검정색)
            if (transform.parent != null)
            {
                foreach (var existingTm in transform.parent.GetComponentsInChildren<TextMesh>(true))
                {
                    existingTm.fontSize = 45;
                    if (!Billboard.IsRegionalHeaderText(existingTm.text, existingTm.gameObject.name))
                    {
                        existingTm.color = Color.black;
                    }
                    Billboard.Attach(existingTm.gameObject);
                }
            }

            // ① [북쪽 - 입고 도크 방향]: 내려놓기 전용 구역 (PutZone)
            GameObject putObj = new GameObject("PutZone_North");
            putObj.transform.SetParent(transform, false);
            putObj.transform.localPosition = new Vector3(0f, 0.05f, 1.95f);

            GameObject putVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            putVisual.name = "PutPadVisual";
            putVisual.transform.SetParent(putObj.transform, false);
            putVisual.transform.localPosition = Vector3.zero;
            putVisual.transform.localScale = new Vector3(3.2f, 0.10f, 1.65f);
            Destroy(putVisual.GetComponent<Collider>());
            putVisual.GetComponent<Renderer>().material = new Material(shader)
            {
                color = destColor
            };

            BoxCollider putCol = putObj.AddComponent<BoxCollider>();
            putCol.size = new Vector3(3.2f, 2.0f, 1.65f);
            putCol.center = new Vector3(0f, 1.0f, 0f);
            putCol.isTrigger = true;

            PutZone = putObj.AddComponent<SortingPutZone>();
            PutZone.Initialize(this);

            CreateZoneLabel(putObj.transform, $"▼ [{cityKor}] 내려놓기 (PutZone) ▼\n(입고 방면 - 놓기 전용)", new Vector3(0f, 1.15f, 0.2f), Color.black);

            // ② [남쪽 - 출고 도크 방향]: 집기 전용 구역 (TakeZone)
            GameObject takeObj = new GameObject("TakeZone_South");
            takeObj.transform.SetParent(transform, false);
            takeObj.transform.localPosition = new Vector3(0f, 0.05f, -1.95f);

            GameObject takeVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            takeVisual.name = "TakePadVisual";
            takeVisual.transform.SetParent(takeObj.transform, false);
            takeVisual.transform.localPosition = Vector3.zero;
            takeVisual.transform.localScale = new Vector3(3.2f, 0.10f, 1.65f);
            Destroy(takeVisual.GetComponent<Collider>());
            takeVisual.GetComponent<Renderer>().material = new Material(shader)
            {
                color = Color.Lerp(destColor, new Color(0.95f, 0.92f, 0.25f), 0.32f)
            };

            BoxCollider takeCol = takeObj.AddComponent<BoxCollider>();
            takeCol.size = new Vector3(3.2f, 2.0f, 1.65f);
            takeCol.center = new Vector3(0f, 1.0f, 0f);
            takeCol.isTrigger = true;

            TakeZone = takeObj.AddComponent<SortingTakeZone>();
            TakeZone.Initialize(this);

            CreateZoneLabel(takeObj.transform, $"▲ [{cityKor}] 집기 (TakeZone) ▲\n(출고 방면 - 들기 전용)", new Vector3(0f, 1.15f, -0.2f), Color.black);
        }

        private TextMesh CreateZoneLabel(Transform parent, string text, Vector3 localPos, Color color)
        {
            GameObject lblObj = new GameObject("ZoneLabel");
            lblObj.transform.SetParent(parent, false);
            lblObj.transform.localPosition = localPos;
            lblObj.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            TextMesh tm = lblObj.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 45;
            tm.characterSize = 0.08f;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.black;
            Billboard.Attach(lblObj);
            return tm;
        }

        private void LateUpdate()
        {
            if (centerCountLabel != null && Camera.main != null)
            {
                centerCountLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }

        protected override void OnTick(PlayerController p)
        {
            // 상호작용은 물리적으로 분리된 PutZone(북쪽)과 TakeZone(남쪽)에서 각각 독립 처리합니다.
        }

        private void CleanupSortedList()
        {
            for (int i = sortedPackages.Count - 1; i >= 0; i--)
            {
                if (sortedPackages[i] == null || sortedPackages[i].State != PackageState.Sorted)
                {
                    sortedPackages.RemoveAt(i);
                }
            }
        }

        public void SortPackage(PackageItem pkg)
        {
            if (pkg == null) return;
            CleanupSortedList();

            pkg.State = PackageState.Sorted;
            sortedPackages.Add(pkg);

            Vector3 padCenter = (outputPad != null) ? outputPad.position : transform.position;
            int idx = sortedPackages.Count - 1;
            int col = idx % 3;
            int row = (idx / 3) % 2;
            int layer = idx / 6;

            Vector3 targetPos = padCenter + new Vector3((col - 1) * 0.85f, 0.35f + layer * 0.60f, (row - 0.5f) * 0.70f);

            pkg.FlyTo(null, targetPos, 0.20f, () =>
            {
                if (pkg != null && pkg.State == PackageState.Sorted)
                {
                    pkg.transform.position = targetPos;
                }
                SoundManager.Instance?.PlayDrop();
            });

            UpdateCenterLabel();
        }

        public PackageItem PopSortedPackage()
        {
            CleanupSortedList();
            if (sortedPackages.Count == 0) return null;

            int last = sortedPackages.Count - 1;
            PackageItem item = sortedPackages[last];
            sortedPackages.RemoveAt(last);
            UpdateCenterLabel();
            return item;
        }

        public List<PackageItem> PopPackagesForPallet(int maxCount)
        {
            CleanupSortedList();
            List<PackageItem> popped = new List<PackageItem>();
            while (sortedPackages.Count > 0 && popped.Count < maxCount)
            {
                int last = sortedPackages.Count - 1;
                PackageItem item = sortedPackages[last];
                sortedPackages.RemoveAt(last);
                if (item != null)
                {
                    popped.Add(item);
                }
            }
            UpdateCenterLabel();
            return popped;
        }

        public bool HasSortedPackages()
        {
            CleanupSortedList();
            return sortedPackages.Count > 0;
        }

        private void UpdateCenterLabel()
        {
            if (centerCountLabel == null) return;
            string cityKor = PalletItem.GetDestinationKoreanName(targetDestination);
            centerCountLabel.text = $"📦 {cityKor} 분류 적재: {sortedPackages.Count}개";
        }
    }

    /// <summary>
    /// ① [북쪽 - 입고 도크 방향]: 내려놓기 전용 구역 (PutZone)
    /// - 손에 해당 지역 택배를 들고 진입 시 0.15초 간격으로 바닥에 내려놓음 (빈손일 때 집기 금지).
    /// </summary>
    public class SortingPutZone : MonoBehaviour
    {
        public SortingStation ParentStation { get; private set; }
        public float putInterval = 0.15f;

        private bool isPlayerInTrigger = false;
        private float putTimer = 0f;
        private TextMesh labelMesh;

        public void Initialize(SortingStation station)
        {
            ParentStation = station;
        }

        private void OnTriggerEnter(Collider other)
        {
            var p = other.GetComponent<PlayerController>() ?? other.GetComponentInParent<PlayerController>();
            if (p != null && !p.IsDrivingForklift)
            {
                isPlayerInTrigger = true;
                putTimer = putInterval * 0.85f;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            var p = other.GetComponent<PlayerController>() ?? other.GetComponentInParent<PlayerController>();
            if (p != null)
            {
                isPlayerInTrigger = false;
            }
        }

        private void Update()
        {
            if (ParentStation == null) return;

            PlayerController p = PlayerController.Instance;
            if (p == null || p.IsDrivingForklift || p.Stack == null) return;

            Vector3 diff = p.transform.position - transform.position;
            bool inside = isPlayerInTrigger || (Mathf.Abs(diff.x) <= 1.75f && Mathf.Abs(diff.z) <= 0.95f);

            if (inside)
            {
                // 빈손일 때는 절대 집지 않음 (내려놓기 전용!)
                if (p.Stack.Count == 0) return;

                putTimer += Time.deltaTime;
                if (putTimer >= putInterval)
                {
                    putTimer = 0f;
                    var match = p.Stack.FindMatching(ParentStation.targetDestination);
                    if (match != null)
                    {
                        ParentStation.SortPackage(match);
                    }
                }
            }
            else
            {
                putTimer = putInterval * 0.85f;
            }
        }

        private void LateUpdate()
        {
            if (labelMesh == null) labelMesh = GetComponentInChildren<TextMesh>();
            if (labelMesh != null && Camera.main != null)
            {
                labelMesh.transform.rotation = Camera.main.transform.rotation;
            }
        }
    }

    /// <summary>
    /// ② [남쪽 - 출고 도크 방향]: 집기 전용 구역 (TakeZone)
    /// - 손에 빈자리가 있을 때 진입 시 0.15초 간격으로 택배를 집어 올림 (들고 있어도 내려놓기 금지).
    /// </summary>
    public class SortingTakeZone : MonoBehaviour
    {
        public SortingStation ParentStation { get; private set; }
        public float takeInterval = 0.15f;

        private bool isPlayerInTrigger = false;
        private float takeTimer = 0f;
        private TextMesh labelMesh;

        public void Initialize(SortingStation station)
        {
            ParentStation = station;
        }

        private void OnTriggerEnter(Collider other)
        {
            var p = other.GetComponent<PlayerController>() ?? other.GetComponentInParent<PlayerController>();
            if (p != null && !p.IsDrivingForklift)
            {
                isPlayerInTrigger = true;
                takeTimer = takeInterval * 0.85f;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            var p = other.GetComponent<PlayerController>() ?? other.GetComponentInParent<PlayerController>();
            if (p != null)
            {
                isPlayerInTrigger = false;
            }
        }

        private void Update()
        {
            if (ParentStation == null) return;

            PlayerController p = PlayerController.Instance;
            if (p == null || p.IsDrivingForklift || p.Stack == null) return;

            Vector3 diff = p.transform.position - transform.position;
            bool inside = isPlayerInTrigger || (Mathf.Abs(diff.x) <= 1.75f && Mathf.Abs(diff.z) <= 0.95f);

            if (inside)
            {
                // 들고 있어도 절대 내려놓지 않음 (집기 전용!)
                int maxCap = (GameManager.Instance != null) ? GameManager.Instance.GetPlayerMaxStack() : 4;
                if (p.Stack.Count >= maxCap || !ParentStation.HasSortedPackages()) return;

                takeTimer += Time.deltaTime;
                if (takeTimer >= takeInterval)
                {
                    takeTimer = 0f;
                    PackageItem sorted = ParentStation.PopSortedPackage();
                    if (sorted != null)
                    {
                        p.Stack.Push(sorted);
                    }
                }
            }
            else
            {
                takeTimer = takeInterval * 0.85f;
            }
        }

        private void LateUpdate()
        {
            if (labelMesh == null) labelMesh = GetComponentInChildren<TextMesh>();
            if (labelMesh != null && Camera.main != null)
            {
                labelMesh.transform.rotation = Camera.main.transform.rotation;
            }
        }
    }
}
