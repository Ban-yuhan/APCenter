using System;
using UnityEngine;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Player;
using APCenter.UI;

namespace APCenter.Facilities
{
    /// <summary>
    /// 남쪽 출고 도크 앞 상차 발판 (LoadingZone):
    /// - 맨몸 플레이어 진입 시: 0.15초 간격으로 머리 위(Stack)에서 해당 지역 택배가 1개씩 차례대로 상차됩니다.
    /// - 지게차(ForkliftController) 진입 시: 앞쪽 포크(forkPoint)에 9개 결속이 완료된(IsReady == true) 동일 목적지 PalletItem만 상차 가능하며,
    ///   트럭 물리 슬롯 1칸을 차지하고 DeliveryUnits = 9가 반영됩니다. 상차 완료 시 플레이어는 정상 하차합니다.
    ///   (1~8개 미완성 파렛트는 상차가 엄격히 차단됩니다.)
    /// </summary>
    public class LoadingZone : InteractionZone
    {
        [Header("Loading Zone Settings")]
        public PackageDestination targetDestination = PackageDestination.Seoul;
        public OutboundTruck targetTruck;
        public float loadInterval = 0.15f; // 0.15초 간격 상차

        private float stepLoadTimer = 0f;
        private float warningCooldown = 0f;
        private TextMesh padLabel;
        private Renderer padRenderer;

        public void InitializeZone(PackageDestination dest, OutboundTruck truck, Vector3 worldPos)
        {
            targetDestination = dest;
            targetTruck = truck;
            zoneType = ZoneType.OutboundLoading;
            transform.position = worldPos;

            BuildZoneVisuals();
            UpdateZoneLabel();
        }

        private void Start()
        {
            if (targetTruck == null)
            {
                targetTruck = OutboundTruck.GetTruckForDestination(targetDestination);
            }

            BuildZoneVisuals();
            UpdateZoneLabel();
        }

        private void BuildZoneVisuals()
        {
            if (transform.Find("PadVisual") != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            Color destColor = PackageItem.GetDestinationColor(targetDestination);

            GameObject padObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            padObj.name = "PadVisual";
            padObj.transform.SetParent(transform, false);
            padObj.transform.localPosition = Vector3.zero;
            padObj.transform.localScale = new Vector3(3.8f, 0.1f, 3.0f);
            Destroy(padObj.GetComponent<Collider>());

            padRenderer = padObj.GetComponent<Renderer>();
            if (padRenderer != null)
            {
                Material mat = new Material(shader);
                mat.color = Color.Lerp(destColor, new Color(0.95f, 0.85f, 0.2f), 0.25f);
                padRenderer.material = mat;
            }

            var col = GetComponent<BoxCollider>();
            if (col == null)
            {
                col = gameObject.AddComponent<BoxCollider>();
            }
            col.size = new Vector3(3.8f, 2.0f, 3.0f);
            col.center = new Vector3(0f, 1.0f, 0f);
            col.isTrigger = true;

            GameObject lblObj = new GameObject("LoadingZoneLabel");
            lblObj.transform.SetParent(transform, false);
            lblObj.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            lblObj.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            padLabel = lblObj.AddComponent<TextMesh>();
            padLabel.fontSize = 45;
            padLabel.characterSize = 0.08f;
            padLabel.alignment = TextAlignment.Center;
            padLabel.anchor = TextAnchor.MiddleCenter;
            padLabel.color = Color.black;
            Billboard.Attach(lblObj);
        }

        private void Update()
        {
            if (warningCooldown > 0f) warningCooldown -= Time.deltaTime;

            if (targetTruck == null)
            {
                targetTruck = OutboundTruck.GetTruckForDestination(targetDestination);
            }

            UpdateZoneLabel();

            // 1. 지게차(ForkliftController)가 상차 발판에 진입한 경우 처리
            if (ForkliftController.Instance != null && ForkliftController.Instance.IsDriving)
            {
                Vector3 fkDiff = ForkliftController.Instance.transform.position - transform.position;
                bool isForkliftOnPad = Mathf.Abs(fkDiff.x) <= 2.4f && Mathf.Abs(fkDiff.z) <= 2.2f;
                if (isForkliftOnPad)
                {
                    TryLoadForkliftPallet(ForkliftController.Instance);
                    return;
                }
            }

            // 2. 맨몸 플레이어가 상차 발판에 진입한 경우 처리
            PlayerController p = PlayerController.Instance;
            if (p == null || p.IsDrivingForklift || p.Stack == null) return;

            Vector3 diff = p.transform.position - transform.position;
            bool isOnPad = isPlayerInside || (Mathf.Abs(diff.x) <= 2.2f && Mathf.Abs(diff.z) <= 1.9f);

            if (isOnPad)
            {
                stepLoadTimer += Time.deltaTime;
                if (stepLoadTimer >= loadInterval)
                {
                    stepLoadTimer = 0f;
                    TryLoadOneItemFromPlayer(p);
                }
            }
            else
            {
                stepLoadTimer = loadInterval * 0.8f;
            }
        }

        private void LateUpdate()
        {
            if (padLabel != null && Camera.main != null)
            {
                padLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }

        protected override void OnTick(PlayerController p)
        {
        }

        private void TryLoadForkliftPallet(ForkliftController forklift)
        {
            if (targetTruck == null || !targetTruck.CanLoadPackage()) return;

            PalletItem forkPallet = forklift.CurrentForkPallet;
            if (forkPallet == null || forkPallet.CurrentBoxCount == 0) return;

            // 9개 미만 미완성 파렛트(1~8개)는 출고 트럭 상차 엄격 차단!
            if (!forkPallet.IsReady || !forkPallet.IsMerged || forkPallet.CurrentBoxCount < PalletItem.RequiredPackageCount)
            {
                if (warningCooldown <= 0f)
                {
                    warningCooldown = 2.0f;
                    SoundManager.Instance?.PlayError();
                    UIManager.Instance?.ShowNotice(
                        $"🚫 [상차 불가] 미완성 파렛트({forkPallet.GetProgressStatusText()})는 상차할 수 없습니다! 동일 목적지 택배 9개를 모두 결속해 주세요."
                    );
                }
                return;
            }

            // 목적지 일치 여부 확인
            if (forkPallet.Destination != targetDestination)
            {
                if (warningCooldown <= 0f)
                {
                    warningCooldown = 2.0f;
                    SoundManager.Instance?.PlayError();
                    string palletCity = PalletItem.GetDestinationKoreanName(forkPallet.Destination);
                    string dockCity = PalletItem.GetDestinationKoreanName(targetDestination);
                    UIManager.Instance?.ShowNotice(
                        $"🚫 [도크 불일치] 현재 파렛트는 [{palletCity}행]입니다! [{dockCity}] 도크에는 상차할 수 없습니다."
                    );
                }
                return;
            }

            // 9개 결속 완료된 파렛트 상차 실행 (트럭 물리 슬롯 1칸 차지, DeliveryUnits = 9 반영)
            forkPallet.SetLabelVisible(true);
            if (targetTruck.LoadPackage(forkPallet))
            {
                // 상차 완료 시 플레이어 정상 하차 및 빈 지게차 복귀
                forklift.OnPalletLoadedToTruck(forkPallet);
            }
        }

        private void TryLoadOneItemFromPlayer(PlayerController p)
        {
            if (targetTruck == null || !targetTruck.CanLoadPackage()) return;
            if (p.Stack.Count == 0) return;

            PackageItem matchItem = p.Stack.FindMatching(targetDestination);
            if (matchItem != null)
            {
                targetTruck.LoadPackage(matchItem);
            }
        }

        private void UpdateZoneLabel()
        {
            if (padLabel == null) return;

            string city = (targetDestination == PackageDestination.Seoul) ? "🔴 서울"
                        : (targetDestination == PackageDestination.Busan) ? "🔵 부산"
                        : "🟢 대구";

            if (targetTruck != null)
            {
                padLabel.text = $"▼ {city} 상차 발판 ▼\n[{targetTruck.LoadedSlotCount}/{targetTruck.MaxCapacity}칸 | 정산 {targetTruck.TotalDeliveryUnits}단위]";
            }
            else
            {
                padLabel.text = $"▼ {city} 상차 발판 ▼";
            }
            padLabel.color = Color.black;
        }
    }
}
