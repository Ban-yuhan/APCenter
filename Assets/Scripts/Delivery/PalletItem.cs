using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;

namespace APCenter.Delivery
{
    /// <summary>
    /// 파렛트(Pallet) 결속 오브젝트:
    /// - 반드시 '동일한 목적지(서울/부산/대구)'의 실제 택배가 정확히 9개 누적되었을 때만 결속 완료(IsMerged = true, isReady = true)됩니다.
    /// - 1~8개 미완성 상태에서는 출고 트럭에 상차할 수 없으며 현재 진행도(예: "[서울] 4 / 9")만 표시합니다.
    /// - 트럭 적재함(최대 10칸)에서는 물리적 슬롯을 '1칸'만 차지하며, DeliveryUnits = 9를 통해 정산 및 기업 대량주문에 +9개로 반영됩니다.
    /// </summary>
    public class PalletItem : PackageItem
    {
        public const int RequiredPackageCount = 9;

        private int deliveryUnits = 9;
        public override int DeliveryUnits
        {
            get => deliveryUnits;
            set => deliveryUnits = value;
        }

        private readonly List<PackageItem> storedPackages = new List<PackageItem>();
        public IReadOnlyList<PackageItem> StoredPackages => storedPackages;

        public int CurrentBoxCount => IsMerged ? RequiredPackageCount : storedPackages.Count;

        /// <summary>
        /// 9개 동일 목적지 택배가 모두 모여 결속이 완료되었는지 여부 (9개 미만일 때는 엄격히 false)
        /// </summary>
        public bool isReady { get; private set; } = false;
        public bool IsReady => isReady && IsMerged && CurrentBoxCount >= RequiredPackageCount;

        public bool IsMerged { get; private set; } = false;
        public bool IsMergingInProgress { get; private set; } = false;

        private Transform boxesContainer;
        private GameObject bindingWrapVisual;
        private TextMesh palletLabel;
        private Renderer palletBaseRenderer;

        public void InitializeEmptyPallet()
        {
            PackageId = nextId++;
            Destination = PackageDestination.None;
            State = PackageState.Carried;
            IsPalletized = true;
            IsMerged = false;
            isReady = false;
            IsMergingInProgress = false;
            storedPackages.Clear();
            DeliveryUnits = RequiredPackageCount;

            BuildPalletVisuals();
            RefreshPalletLabel();
        }

        private void BuildPalletVisuals()
        {
            if (transform.Find("PalletBase") != null) return;

            var col = GetComponent<Collider>();
            if (col == null)
            {
                var boxCol = gameObject.AddComponent<BoxCollider>();
                boxCol.size = new Vector3(1.1f, 0.5f, 1.1f);
                boxCol.isTrigger = true;
            }
            else
            {
                col.isTrigger = true;
            }

            var selfRenderer = GetComponent<MeshRenderer>();
            if (selfRenderer != null) selfRenderer.enabled = false;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");

            // 파렛트 목재 상판
            GameObject basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlate.name = "PalletBase";
            basePlate.transform.SetParent(transform, false);
            basePlate.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            basePlate.transform.localScale = new Vector3(1.08f, 0.10f, 1.08f);
            Destroy(basePlate.GetComponent<Collider>());

            palletBaseRenderer = basePlate.GetComponent<Renderer>();
            if (palletBaseRenderer != null)
            {
                palletBaseRenderer.material = new Material(shader) { color = new Color(0.68f, 0.48f, 0.24f) };
            }

            // 파렛트 하단 받침목 3개
            Material beamMat = new Material(shader) { color = new Color(0.52f, 0.35f, 0.16f) };
            for (int i = -1; i <= 1; i++)
            {
                GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
                beam.name = $"PalletBeam_{i}";
                beam.transform.SetParent(transform, false);
                beam.transform.localPosition = new Vector3(i * 0.38f, 0.0f, 0f);
                beam.transform.localScale = new Vector3(0.14f, 0.08f, 1.08f);
                Destroy(beam.GetComponent<Collider>());
                var br = beam.GetComponent<Renderer>();
                if (br != null) br.material = beamMat;
            }

            // 3x3 택배 적재 컨테이너
            GameObject containerObj = new GameObject("AlignedBoxes_3x3");
            containerObj.transform.SetParent(transform, false);
            containerObj.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            boxesContainer = containerObj.transform;

            // 9개 결속 완료 시 표시될 래핑/결속 밴드 비주얼 (초기 비활성)
            bindingWrapVisual = new GameObject("BindingWrapVisual");
            bindingWrapVisual.transform.SetParent(transform, false);
            bindingWrapVisual.transform.localPosition = new Vector3(0f, 0.28f, 0f);

            Material strapMat = new Material(shader) { color = new Color(0.95f, 0.85f, 0.15f) };
            for (int i = -1; i <= 1; i += 2)
            {
                GameObject strapX = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strapX.name = $"StrapX_{i}";
                strapX.transform.SetParent(bindingWrapVisual.transform, false);
                strapX.transform.localPosition = new Vector3(i * 0.25f, 0.02f, 0f);
                strapX.transform.localScale = new Vector3(0.05f, 0.34f, 1.02f);
                Destroy(strapX.GetComponent<Collider>());
                strapX.GetComponent<Renderer>().material = strapMat;

                GameObject strapZ = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strapZ.name = $"StrapZ_{i}";
                strapZ.transform.SetParent(bindingWrapVisual.transform, false);
                strapZ.transform.localPosition = new Vector3(0f, 0.02f, i * 0.25f);
                strapZ.transform.localScale = new Vector3(1.02f, 0.34f, 0.05f);
                Destroy(strapZ.GetComponent<Collider>());
                strapZ.GetComponent<Renderer>().material = strapMat;
            }
            bindingWrapVisual.SetActive(false);

            // 상단 World Text 라벨
            GameObject lblObj = new GameObject("PalletStatusLabel");
            lblObj.transform.SetParent(transform, false);
            lblObj.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            palletLabel = lblObj.AddComponent<TextMesh>();
            palletLabel.fontSize = 45;
            palletLabel.characterSize = 0.08f;
            palletLabel.alignment = TextAlignment.Center;
            palletLabel.anchor = TextAnchor.MiddleCenter;
            palletLabel.color = Color.black;
            Billboard.Attach(lblObj);
        }

        private void LateUpdate()
        {
            if (palletLabel != null && Camera.main != null)
            {
                palletLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }

        public void SetLabelVisible(bool visible)
        {
            if (palletLabel != null)
            {
                palletLabel.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 해당 택배를 이 파렛트에 추가할 수 있는지 검증합니다. (동일 목적지 엄수 조건 및 9개 한도 체크)
        /// </summary>
        public bool CanAcceptPackage(PackageItem pkg, out string rejectReason)
        {
            rejectReason = string.Empty;

            if (pkg == null || pkg is PalletItem)
            {
                rejectReason = "유효하지 않은 택배입니다.";
                return false;
            }

            if (IsMerged || isReady || storedPackages.Count >= RequiredPackageCount)
            {
                rejectReason = "이미 9개 결속이 완료된 파렛트입니다.";
                return false;
            }

            if (pkg.IsDefective)
            {
                rejectReason = "불량 택배는 파렛트에 적재할 수 없습니다! (반품 구역으로 이동하세요)";
                return false;
            }

            if (pkg.Destination == PackageDestination.None)
            {
                rejectReason = "목적지가 지정되지 않은 택배입니다.";
                return false;
            }

            // 첫 번째 택배가 들어온 이후에는 반드시 동일한 목적지의 택배만 허용
            if (storedPackages.Count > 0 && Destination != PackageDestination.None && pkg.Destination != Destination)
            {
                string currentDestKor = GetDestinationKoreanName(Destination);
                string pkgDestKor = GetDestinationKoreanName(pkg.Destination);
                rejectReason = $"동일 목적지 엄수! 현재 [{currentDestKor}행] 파렛트 작업 중이므로 [{pkgDestKor}행] 택배는 투입할 수 없습니다.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 택배 1개를 파렛트 위 3x3 격자 슬롯에 차곡차곡 적재합니다.
        /// 정확히 9개가 누적되었을 때만 단일 결속 PalletItem으로 변환됩니다.
        /// </summary>
        public bool TryAddPackage(PackageItem pkg, Action onNineCompleted = null)
        {
            if (!CanAcceptPackage(pkg, out _)) return false;

            if (boxesContainer == null)
            {
                BuildPalletVisuals();
            }

            // 첫 번째 택배의 목적지를 파렛트 기준 목적지로 설정
            if (storedPackages.Count == 0 || Destination == PackageDestination.None)
            {
                Destination = pkg.Destination;
                Color destColor = GetDestinationColor(Destination);
                if (palletBaseRenderer != null)
                {
                    palletBaseRenderer.material.color = Color.Lerp(new Color(0.68f, 0.48f, 0.24f), destColor, 0.28f);
                }
            }

            int slotIdx = storedPackages.Count;
            storedPackages.Add(pkg);

            if (pkg.IsUrgent) IsUrgent = true;

            pkg.State = PackageState.Carried;
            var pkgCol = pkg.GetComponent<Collider>();
            if (pkgCol != null) pkgCol.enabled = false;

            // 3x3 격자 위치 계산 (0~8)
            int row = slotIdx / 3;
            int col = slotIdx % 3;
            Vector3 slotLocalPos = new Vector3((col - 1) * 0.32f, 0.16f, (row - 1) * 0.32f);
            Vector3 slotLocalScale = new Vector3(0.28f, 0.28f, 0.28f);

            pkg.transform.SetParent(boxesContainer, true);
            StartCoroutine(AnimateBoxToSlot(pkg.transform, slotLocalPos, slotLocalScale));

            SoundManager.Instance?.PlayPickup();

            // 정확히 9개가 모였을 때만 결속 완료 처리!
            if (storedPackages.Count >= RequiredPackageCount)
            {
                CompletePalletBinding(onNineCompleted);
            }
            else
            {
                isReady = false;
                IsMerged = false;
                RefreshPalletLabel();
            }

            return true;
        }

        private IEnumerator AnimateBoxToSlot(Transform boxTrans, Vector3 targetLocalPos, Vector3 targetLocalScale)
        {
            if (boxTrans == null) yield break;

            Vector3 startLocalPos = boxTrans.localPosition;
            Vector3 startLocalScale = boxTrans.localScale;
            Quaternion startLocalRot = boxTrans.localRotation;

            float duration = 0.12f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (boxTrans == null) yield break;
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                boxTrans.localPosition = Vector3.Lerp(startLocalPos, targetLocalPos, t);
                boxTrans.localScale = Vector3.Lerp(startLocalScale, targetLocalScale, t);
                boxTrans.localRotation = Quaternion.Slerp(startLocalRot, Quaternion.identity, t);
                yield return null;
            }

            if (boxTrans != null)
            {
                boxTrans.localPosition = targetLocalPos;
                boxTrans.localScale = targetLocalScale;
                boxTrans.localRotation = Quaternion.identity;
            }
        }

        /// <summary>
        /// 동일 목적지 택배 9개가 모두 모였을 때 단일 PalletItem 오브젝트로 최종 결속/래핑합니다.
        /// </summary>
        private void CompletePalletBinding(Action onComplete = null)
        {
            if (storedPackages.Count < RequiredPackageCount) return;

            Color destColor = GetDestinationColor(Destination);

            for (int i = 0; i < storedPackages.Count; i++)
            {
                PackageItem srcPkg = storedPackages[i];
                if (srcPkg == null) continue;

                int row = i / 3;
                int col = i % 3;
                Vector3 slotLocalPos = new Vector3((col - 1) * 0.32f, 0.16f, (row - 1) * 0.32f);
                Vector3 slotLocalScale = new Vector3(0.28f, 0.28f, 0.28f);

                GameObject visualBox = srcPkg.gameObject;
                visualBox.name = $"BoundBox_{i + 1}";
                visualBox.transform.SetParent(boxesContainer, false);
                visualBox.transform.localPosition = slotLocalPos;
                visualBox.transform.localScale = slotLocalScale;
                visualBox.transform.localRotation = Quaternion.identity;

                var rend = visualBox.GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    rend.material.color = destColor;
                }

                // 개별 택배 콜라이더 및 PackageItem 컴포넌트를 제거하여 단일 PalletItem으로 완전 결속
                var colComp = visualBox.GetComponent<Collider>();
                if (colComp != null) Destroy(colComp);
                Destroy(srcPkg);
            }

            DeliveryUnits = RequiredPackageCount; // DeliveryUnits = 9 확정
            IsPalletized = true;
            IsMerged = true;
            isReady = true;
            IsMergingInProgress = false;

            if (bindingWrapVisual != null)
            {
                bindingWrapVisual.SetActive(true);
            }

            if (palletBaseRenderer != null)
            {
                palletBaseRenderer.material.color = Color.Lerp(new Color(0.68f, 0.48f, 0.24f), destColor, 0.45f);
            }

            RefreshPalletLabel();
            SoundManager.Instance?.PlaySuccess();
            onComplete?.Invoke();
        }

        public static string GetDestinationKoreanName(PackageDestination dest)
        {
            switch (dest)
            {
                case PackageDestination.Seoul: return "서울";
                case PackageDestination.Busan: return "부산";
                case PackageDestination.Daegu: return "대구";
                default: return "미정";
            }
        }

        public string GetProgressStatusText()
        {
            if (CurrentBoxCount == 0 || Destination == PackageDestination.None)
            {
                return $"[대기] 0 / {RequiredPackageCount}";
            }

            string dKor = GetDestinationKoreanName(Destination);
            if (IsReady)
            {
                return $"[{dKor}] {RequiredPackageCount} / {RequiredPackageCount} (결속완료)";
            }

            return $"[{dKor}] {CurrentBoxCount} / {RequiredPackageCount}";
        }

        public void RefreshPalletLabel()
        {
            if (palletLabel == null) return;

            palletLabel.color = Color.black;

            if (State == PackageState.LoadedInTruck)
            {
                string dTag = GetDestinationKoreanName(Destination);
                palletLabel.text = $"[{dTag} 파렛트 x{DeliveryUnits}]";
                return;
            }

            if (!IsMerged || !isReady)
            {
                if (CurrentBoxCount == 0 || Destination == PackageDestination.None)
                {
                    palletLabel.text = $"🪵 빈 파렛트 (0 / {RequiredPackageCount})";
                }
                else
                {
                    string dKor = GetDestinationKoreanName(Destination);
                    palletLabel.text = $"{dKor}행 파렛트: {CurrentBoxCount} / {RequiredPackageCount}";
                }
            }
            else
            {
                string destName = Destination == PackageDestination.Seoul ? "🔴서울"
                                : Destination == PackageDestination.Busan ? "🔵부산"
                                : "🟢대구";
                palletLabel.text = $"📦 {destName}행 파렛트: 9 / 9 [결속완료]\n(슬롯 1칸 / 정산 {DeliveryUnits}개분)";
            }
        }
    }
}
