using System;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Player;

namespace APCenter.Facilities
{
    /// <summary>
    /// 북쪽 입고장 적재 관리 구역 (InboundZone.cs):
    /// - 입고장 최대 적재량을 32개로 엄격히 제한합니다 (maxCapacity = 32).
    /// - 바닥 1층당 16개(4x4)씩 위로 최대 2층까지(4x4x2 = 32칸) 격자 형태로 정렬 적재합니다.
    /// - 입고장에 32개가 모두 차 있으면 만차(IsFull = true) 판정을 내려 입고 트럭이 하차 없이 즉시 회차하도록 연동합니다.
    /// </summary>
    public class InboundZone : InteractionZone
    {
        public static InboundZone Instance { get; private set; }

        [Header("Inbound Capacity & 4x4x2 Grid Settings")]
        public int maxCapacity = 32;            // 최대 32개 (1층 16개 + 2층 16개)
        public int gridColumns = 4;             // 가로 4열
        public int gridRows = 4;                // 세로 4행 (1층당 4x4 = 16개)
        public int maxFloors = 2;               // 최대 2층
        public float cellSpacing = 1.15f;       // 격자 간격
        public float baseHeightOffset = 0.35f;  // 1층 바닥 높이
        public float floorHeightStep = 0.65f;   // 층간 높이 간격

        private PackageItem[] gridSlots;
        private TextMesh zoneStatusLabel;

        public int CurrentCount
        {
            get
            {
                RefreshOccupancy();
                int count = 0;
                for (int i = 0; i < gridSlots.Length; i++)
                {
                    if (gridSlots[i] != null) count++;
                }
                return count;
            }
        }

        public int AvailableSlots => Mathf.Max(0, maxCapacity - CurrentCount);
        public bool IsFull => CurrentCount >= maxCapacity;

        public static InboundZone EnsureInboundZoneExists(Transform unloadPad = null)
        {
            if (Instance != null) return Instance;

            var existing = FindAnyObjectByType<InboundZone>();
            if (existing != null)
            {
                Instance = existing;
                return Instance;
            }

            GameObject padObj = (unloadPad != null) ? unloadPad.gameObject : GameObject.Find("UnloadPad");
            if (padObj == null)
            {
                padObj = new GameObject("UnloadPad");
                padObj.transform.position = new Vector3(-8f, 0.05f, 12f);
            }

            Instance = padObj.GetComponent<InboundZone>() ?? padObj.AddComponent<InboundZone>();
            Instance.zoneType = ZoneType.UnloadPickup;
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            zoneType = ZoneType.UnloadPickup;
            maxCapacity = 32;
            gridSlots = new PackageItem[maxCapacity];
        }

        private void Start()
        {
            if (gridSlots == null || gridSlots.Length != maxCapacity)
            {
                gridSlots = new PackageItem[maxCapacity];
            }

            BuildStatusLabelIfNeeded();
            RefreshOccupancy();
            UpdateStatusLabel();
        }

        private void BuildStatusLabelIfNeeded()
        {
            if (zoneStatusLabel != null) return;

            Transform parentTarget = transform.parent != null ? transform.parent : transform;
            Transform existingLbl = parentTarget.Find("InboundZoneStatusLabel") ?? transform.Find("InboundZoneStatusLabel");
            if (existingLbl != null)
            {
                zoneStatusLabel = existingLbl.GetComponent<TextMesh>();
                if (zoneStatusLabel != null)
                {
                    zoneStatusLabel.fontSize = 45;
                    zoneStatusLabel.color = Color.black;
                }
                Billboard.Attach(existingLbl.gameObject);
                return;
            }

            GameObject lblObj = new GameObject("InboundZoneStatusLabel");
            if (transform.parent != null)
            {
                lblObj.transform.SetParent(transform.parent, true);
            }
            lblObj.transform.position = transform.position + new Vector3(0f, 2.3f, 2.8f);
            lblObj.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            lblObj.transform.localScale = Vector3.one;

            zoneStatusLabel = lblObj.AddComponent<TextMesh>();
            zoneStatusLabel.fontSize = 45;
            zoneStatusLabel.characterSize = 0.08f;
            zoneStatusLabel.alignment = TextAlignment.Center;
            zoneStatusLabel.anchor = TextAnchor.MiddleCenter;
            zoneStatusLabel.color = Color.black;
            Billboard.Attach(lblObj);
        }

        private void Update()
        {
            RefreshOccupancy();
            UpdateStatusLabel();
        }

        private void LateUpdate()
        {
            if (zoneStatusLabel != null && Camera.main != null)
            {
                zoneStatusLabel.transform.rotation = Camera.main.transform.rotation;
            }
        }

        /// <summary>
        /// 슬롯에 있던 택배가 플레이어나 알바에게 수거되었는지 확인하고,
        /// 1층이 비었을 때 2층에 택배가 있으면 1층으로 자연스럽게 내려앉도록 정리합니다.
        /// </summary>
        public void RefreshOccupancy()
        {
            if (gridSlots == null || gridSlots.Length != maxCapacity)
            {
                gridSlots = new PackageItem[maxCapacity];
            }

            for (int i = 0; i < maxCapacity; i++)
            {
                PackageItem pkg = gridSlots[i];
                if (pkg == null) continue;

                bool stillOnPad = pkg.transform.parent == null &&
                                  (pkg.State == PackageState.Unloaded || pkg.State == PackageState.ReInbounded);
                if (!stillOnPad)
                {
                    gridSlots[i] = null;
                }
            }

            // 1층(0~15)이 비고 바로 위 2층(16~31)에 박스가 남아있으면 1층 슬롯으로 강하 정렬
            int slotsPerFloor = gridColumns * gridRows; // 16
            for (int baseIdx = 0; baseIdx < slotsPerFloor; baseIdx++)
            {
                int upperIdx = baseIdx + slotsPerFloor;
                if (upperIdx < maxCapacity && gridSlots[baseIdx] == null && gridSlots[upperIdx] != null)
                {
                    PackageItem upperPkg = gridSlots[upperIdx];
                    gridSlots[baseIdx] = upperPkg;
                    gridSlots[upperIdx] = null;
                    upperPkg.transform.position = GetSlotWorldPosition(baseIdx);
                }
            }
        }

        /// <summary>
        /// 4x4x2 (총 32칸) 격자에서 비어 있는 첫 슬롯(1층 우선 -> 2층)을 할당하고 목표 월드 좌표를 반환합니다.
        /// 32개가 모두 차 있으면 false를 반환합니다.
        /// </summary>
        public bool TryAllocateSlot(PackageItem pkg, out Vector3 targetWorldPos)
        {
            RefreshOccupancy();

            for (int i = 0; i < maxCapacity; i++)
            {
                if (gridSlots[i] == null)
                {
                    gridSlots[i] = pkg;
                    targetWorldPos = GetSlotWorldPosition(i);
                    UpdateStatusLabel();
                    return true;
                }
            }

            targetWorldPos = transform.position;
            return false;
        }

        public Vector3 GetSlotWorldPosition(int slotIndex)
        {
            int slotsPerFloor = gridColumns * gridRows; // 16
            int floor = slotIndex / slotsPerFloor;      // 0: 1층, 1: 2층
            int rem = slotIndex % slotsPerFloor;
            int row = rem / gridColumns;                // 0 ~ 3
            int col = rem % gridColumns;                // 0 ~ 3

            Vector3 padCenter = new Vector3(transform.position.x, 0f, transform.position.z);
            float offsetX = (col - 1.5f) * cellSpacing;
            float offsetZ = (row - 1.5f) * cellSpacing;
            float targetY = baseHeightOffset + (floor * floorHeightStep);

            return padCenter + new Vector3(offsetX, targetY, offsetZ);
        }

        private void UpdateStatusLabel()
        {
            if (zoneStatusLabel == null) return;

            int count = 0;
            for (int i = 0; i < gridSlots.Length; i++)
            {
                if (gridSlots[i] != null) count++;
            }

            if (count >= maxCapacity)
            {
                zoneStatusLabel.text = $"🚨 입고장 만차 FULL! [{count} / {maxCapacity}]\n(택배를 치워야 다음 트럭 하차 가능)";
            }
            else
            {
                zoneStatusLabel.text = $"📦 입고 하차장 [{count} / {maxCapacity}]\n(4x4 2층 격자 적재)";
            }
            zoneStatusLabel.color = Color.black;
        }
    }
}
