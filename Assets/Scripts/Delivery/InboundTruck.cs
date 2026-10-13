using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.Facilities;
using APCenter.UI;

namespace APCenter.Delivery
{
    public class InboundTruck : MonoBehaviour
    {
        [Header("Waypoints (Outside Road Z = 20, Warehouse Inside Z < 18)")]
        private Vector3 roadSpawnPos = new Vector3(-28f, 0f, 20f);   // 도로 시작점
        private Vector3 roadDockWaitPos = new Vector3(-8f, 0f, 20f); // 도크 문 앞 도로
        private Vector3 dockContactPos = new Vector3(-8f, 0f, 17.6f);// 도크 턱에 후진 도킹 밀착 위치
        private Vector3 roadExitPos = new Vector3(32f, 0f, 20f);     // 도로 우측 퇴장점

        [Header("Dock Door")]
        public Transform dockDoor;

        [Header("Unload Area")]
        public Transform unloadPad;
        public GameObject packagePrefab;

        [Header("Timing")]
        public float arrivalInterval = 18f; // 다음 입고 트럭 주기
        public float arrivalTimer = 1.5f;   // 첫 트럭은 시작 후 1.5초 만에 도착!

        public bool IsDocked { get; private set; } = false;
        public bool IsUnloading { get; private set; } = false;
        private bool isRoutineRunning = false;
        private bool isDoorOpen = false;

        public event Action OnTruckArrived;
        public event Action OnTruckDeparted;

        private Renderer[] truckRenderers;

        private void Awake()
        {
            truckRenderers = GetComponentsInChildren<Renderer>();

            var colliders = GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                col.isTrigger = true;
            }
        }

        private void Start()
        {
            if (dockDoor == null)
            {
                var d = GameObject.Find("InboundShutterDoor");
                if (d != null) dockDoor = d.transform;
            }

            if (unloadPad == null)
            {
                var p = GameObject.Find("UnloadPad");
                if (p != null) unloadPad = p.transform;
            }

            InboundZone.EnsureInboundZoneExists(unloadPad);

            transform.position = roadSpawnPos;
            SetVisualsActive(false);
        }

        private void Update()
        {
            if (!isRoutineRunning)
            {
                arrivalTimer -= Time.deltaTime;
                if (arrivalTimer <= 0f)
                {
                    StartCoroutine(TruckSequenceRoutine());
                }
            }
        }

        private void SetVisualsActive(bool active)
        {
            if (truckRenderers == null || truckRenderers.Length == 0)
            {
                truckRenderers = GetComponentsInChildren<Renderer>();
            }

            foreach (var r in truckRenderers)
            {
                if (r != null) r.enabled = active;
            }
        }

        private IEnumerator TruckSequenceRoutine()
        {
            isRoutineRunning = true;
            transform.position = roadSpawnPos;
            transform.rotation = Quaternion.Euler(0, 90, 0);
            SetVisualsActive(true);

            InboundZone inboundZone = InboundZone.EnsureInboundZoneExists(unloadPad);

            // 1. 도로를 따라 도크 앞까지 진입 주행
            float driveToDoorTime = 2.0f;
            float el = 0f;
            while (el < driveToDoorTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / driveToDoorTime);
                transform.position = Vector3.Lerp(roadSpawnPos, roadDockWaitPos, t);
                yield return null;
            }
            transform.position = roadDockWaitPos;

            // 2. 도크 앞 도착 및 후진 도킹
            SoundManager.Instance?.PlayTruckHorn();
            OnTruckArrived?.Invoke();

            yield return OpenDoorCoroutine(true);

            float dockBackTime = 1.4f;
            el = 0f;
            while (el < dockBackTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / dockBackTime);
                transform.position = Vector3.Lerp(roadDockWaitPos, dockContactPos, t);
                transform.rotation = Quaternion.Slerp(Quaternion.Euler(0, 90, 0), Quaternion.Euler(0, 180, 0), t);
                yield return null;
            }
            transform.position = dockContactPos;
            transform.rotation = Quaternion.Euler(0, 180, 0);
            IsDocked = true;

            yield return new WaitForSeconds(0.3f);

            // 3. 입고장 적재 한도(32개) 확인: 만차이거나 자리가 부족하면 하차하지 않고 즉시 문을 닫고 회차!
            inboundZone.RefreshOccupancy();
            if (inboundZone.IsFull || inboundZone.AvailableSlots <= 0)
            {
                SoundManager.Instance?.PlayAlert();
                SoundManager.Instance?.PlayError();
                UIManager.Instance?.ShowNotice("🚨 [입고장 만차(32/32) 회차!] 입고장에 자리가 없어 트럭이 하차하지 않고 즉시 문을 닫고 회차합니다!");

                // 즉시 문을 닫고 회차 출발
                yield return OpenDoorCoroutine(false);
            }
            else
            {
                UIManager.Instance?.ShowNotice($"📢 [입고 트럭 도착] 물량 하차를 시작합니다! (현재 입고장: {inboundZone.CurrentCount}/{inboundZone.maxCapacity})");

                IsUnloading = true;
                yield return UnloadPackagesCoroutine(inboundZone);
                IsUnloading = false;

                yield return new WaitForSeconds(0.8f);
            }

            // 4. 출발 (또는 만차 회차): 도로 방향으로 빠져나감
            SoundManager.Instance?.PlayTruckHorn();
            IsDocked = false;

            float pullOutTime = 1.3f;
            el = 0f;
            while (el < pullOutTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / pullOutTime);
                transform.position = Vector3.Lerp(dockContactPos, roadDockWaitPos, t);
                transform.rotation = Quaternion.Slerp(Quaternion.Euler(0, 180, 0), Quaternion.Euler(0, 90, 0), t);
                yield return null;
            }
            transform.position = roadDockWaitPos;
            transform.rotation = Quaternion.Euler(0, 90, 0);

            // 문이 아직 열려 있다면 닫기
            if (isDoorOpen)
            {
                StartCoroutine(OpenDoorCoroutine(false));
            }

            // 5. 도로를 따라 우측(+X)으로 퇴장
            float exitTime = 2.4f;
            el = 0f;
            while (el < exitTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / exitTime);
                transform.position = Vector3.Lerp(roadDockWaitPos, roadExitPos, t);
                yield return null;
            }

            SetVisualsActive(false);
            isRoutineRunning = false;
            OnTruckDeparted?.Invoke();

            float volMult = (ReputationSystem.Instance != null) ? ReputationSystem.Instance.GetOrderVolumeMultiplier() : 1f;
            if (EventSystem.Instance != null) volMult *= EventSystem.Instance.GetVolumeMultiplier();
            arrivalTimer = Mathf.Max(5f, arrivalInterval / volMult);
        }

        private IEnumerator OpenDoorCoroutine(bool open)
        {
            if (dockDoor == null) yield break;

            isDoorOpen = open;
            Vector3 closedPos = new Vector3(dockDoor.localPosition.x, 2.2f, dockDoor.localPosition.z);
            Vector3 openedPos = new Vector3(dockDoor.localPosition.x, 5.8f, dockDoor.localPosition.z);
            Vector3 start = dockDoor.localPosition;
            Vector3 target = open ? openedPos : closedPos;

            float dur = 0.7f;
            float el = 0f;
            while (el < dur)
            {
                el += Time.deltaTime;
                dockDoor.localPosition = Vector3.Lerp(start, target, el / dur);
                yield return null;
            }
            dockDoor.localPosition = target;
        }

        private IEnumerator UnloadPackagesCoroutine(InboundZone inboundZone)
        {
            // 하차 예정 물량 계산 (10~16개)
            float baseCount = UnityEngine.Random.Range(10, 16);
            float mult = (EventSystem.Instance != null) ? EventSystem.Instance.GetVolumeMultiplier() : 1f;
            int plannedCount = Mathf.RoundToInt(baseCount * mult);

            Vector3 truckBackPos = transform.position + new Vector3(0f, 1.2f, -1.8f);

            for (int i = 0; i < plannedCount; i++)
            {
                // 매 박스 하차 전 입고장 잔여 슬롯(32개 한도) 체크
                inboundZone.RefreshOccupancy();
                if (inboundZone.IsFull || inboundZone.AvailableSlots <= 0)
                {
                    SoundManager.Instance?.PlayAlert();
                    SoundManager.Instance?.PlayError();
                    UIManager.Instance?.ShowNotice("🚨 [입고장 만차(32/32) 도달!] 자리가 부족하여 남은 하차를 중단하고 즉시 문을 닫고 회차합니다!");
                    yield return OpenDoorCoroutine(false);
                    yield break;
                }

                PackageDestination dest;
                bool isCorporateBulkItem = false;

                if (EventSystem.Instance != null &&
                    EventSystem.Instance.TryConsumeCorporateBulkSpawn(out PackageDestination bulkDest))
                {
                    dest = bulkDest;
                    isCorporateBulkItem = true;
                }
                else
                {
                    dest = (PackageDestination)UnityEngine.Random.Range(1, 4);
                }

                bool isDefective = !isCorporateBulkItem && (UnityEngine.Random.value < 0.15f);
                bool isUrgent = false;
                if (EventSystem.Instance != null && EventSystem.Instance.ActiveEvent == GameEventType.UrgentDelivery && i == 0)
                {
                    isUrgent = true;
                }

                GameObject pkgObj;
                if (packagePrefab != null)
                {
                    pkgObj = Instantiate(packagePrefab, truckBackPos, Quaternion.identity);
                }
                else
                {
                    pkgObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pkgObj.transform.position = truckBackPos;
                    pkgObj.transform.localScale = new Vector3(0.85f, 0.65f, 0.85f);
                }

                var col = pkgObj.GetComponent<Collider>();
                if (col != null) col.isTrigger = true;

                PackageItem item = pkgObj.GetComponent<PackageItem>() ?? pkgObj.AddComponent<PackageItem>();
                item.Initialize(dest, isDefective, isUrgent);

                // InboundZone의 4x4x2 (최대 32칸) 격자 슬롯 할당
                if (!inboundZone.TryAllocateSlot(item, out Vector3 targetPadPos))
                {
                    Destroy(pkgObj);
                    SoundManager.Instance?.PlayAlert();
                    UIManager.Instance?.ShowNotice("🚨 [입고장 만차(32/32)!] 더 이상 적재할 자리가 없어 트럭이 즉시 회차합니다!");
                    yield return OpenDoorCoroutine(false);
                    yield break;
                }

                item.FlyTo(null, targetPadPos, 0.35f, () =>
                {
                    if (item != null)
                    {
                        item.transform.position = targetPadPos;
                    }
                    SoundManager.Instance?.PlayDrop();
                });

                yield return new WaitForSeconds(0.12f);
            }
        }
    }
}
