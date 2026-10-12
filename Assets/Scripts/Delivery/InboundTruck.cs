using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
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

            // 트럭과 자식의 모든 콜라이더를 트리거로 만들어 플레이어를 들이받아 맵 밖으로 밀어내지 않도록 방지!
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

            transform.position = roadSpawnPos;
            SetVisualsActive(false); // 대기 중에는 숨김
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
            transform.rotation = Quaternion.Euler(0, 90, 0); // 도로 방향(동쪽)
            SetVisualsActive(true);

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

            // 2. 도크 앞 도착 알람 (경적)
            SoundManager.Instance?.PlayTruckHorn();
            OnTruckArrived?.Invoke();
            UIManager.Instance?.ShowNotice("📢 [입고 트럭 도착] 도크 문이 개방되며 물량이 하차됩니다!");

            // 3. 도크 문 개방
            yield return OpenDoorCoroutine(true);

            // 4. 후진 도킹: 도로(Z=20)에서 도크 입구 턱(Z=17.6)으로 후진 밀착 (창고 안으로 깊숙이 들어가지 않음!)
            float dockBackTime = 1.6f;
            el = 0f;
            while (el < dockBackTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / dockBackTime);
                transform.position = Vector3.Lerp(roadDockWaitPos, dockContactPos, t);
                // 회전: 도로 정면(90도)에서 후진 각도(180도)로 부드럽게 정렬
                transform.rotation = Quaternion.Slerp(Quaternion.Euler(0, 90, 0), Quaternion.Euler(0, 180, 0), t);
                yield return null;
            }
            transform.position = dockContactPos;
            transform.rotation = Quaternion.Euler(0, 180, 0);
            IsDocked = true;

            yield return new WaitForSeconds(0.4f);

            // 5. 택배 하차 작업 (트럭 뒷문에서 창고 내부 언로딩 패드로)
            IsUnloading = true;
            yield return UnloadPackagesCoroutine();
            IsUnloading = false;

            yield return new WaitForSeconds(1.2f);

            // 6. 하차 완료 후 출발:
            // 앞으로(북쪽 도로 방향 Z=20) 직진으로 빠져나감 (벽을 뚫지 않음!)
            SoundManager.Instance?.PlayTruckHorn();
            IsDocked = false;

            float pullOutTime = 1.4f;
            el = 0f;
            while (el < pullOutTime)
            {
                el += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, el / pullOutTime);
                transform.position = Vector3.Lerp(dockContactPos, roadDockWaitPos, t);
                // 다시 도로 주행 방향(90도)으로 회전
                transform.rotation = Quaternion.Slerp(Quaternion.Euler(0, 180, 0), Quaternion.Euler(0, 90, 0), t);
                yield return null;
            }
            transform.position = roadDockWaitPos;
            transform.rotation = Quaternion.Euler(0, 90, 0);

            // 7. 도크 문 닫기
            StartCoroutine(OpenDoorCoroutine(false));

            // 8. 도로를 따라 오른쪽(+X 방향)으로 쭉 직진하여 퇴장!
            float exitTime = 2.5f;
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

            // 다음 도착 간격 설정
            float volMult = (ReputationSystem.Instance != null) ? ReputationSystem.Instance.GetOrderVolumeMultiplier() : 1f;
            arrivalTimer = Mathf.Max(12f, arrivalInterval / volMult);
        }

        private IEnumerator OpenDoorCoroutine(bool open)
        {
            if (dockDoor == null) yield break;

            isDoorOpen = open;
            Vector3 closedPos = new Vector3(dockDoor.localPosition.x, 2.2f, dockDoor.localPosition.z);
            Vector3 openedPos = new Vector3(dockDoor.localPosition.x, 5.8f, dockDoor.localPosition.z); // 시원하게 위로 오픈!
            Vector3 start = dockDoor.localPosition;
            Vector3 target = open ? openedPos : closedPos;

            float dur = 0.8f;
            float el = 0f;
            while (el < dur)
            {
                el += Time.deltaTime;
                dockDoor.localPosition = Vector3.Lerp(start, target, el / dur);
                yield return null;
            }
            dockDoor.localPosition = target;
        }

        private IEnumerator UnloadPackagesCoroutine()
        {
            // 하차 물량 계산 (10~16개)
            float baseCount = UnityEngine.Random.Range(10, 16);
            float mult = (EventSystem.Instance != null) ? EventSystem.Instance.GetVolumeMultiplier() : 1f;
            int count = Mathf.RoundToInt(baseCount * mult);

            // 하차 도착 중심점 (창고 안 언로딩 패드)
            Vector3 padCenter = (unloadPad != null) ? unloadPad.position : new Vector3(-8f, 0.2f, 12f);
            Vector3 truckBackPos = transform.position + new Vector3(0f, 1.2f, -1.8f); // 트럭 화물칸 뒷문 위치

            for (int i = 0; i < count; i++)
            {
                PackageDestination dest = (PackageDestination)UnityEngine.Random.Range(1, 4);

                if (EventSystem.Instance != null && EventSystem.Instance.ActiveEvent == GameEventType.CorporateOrder)
                {
                    dest = PackageDestination.Seoul;
                }

                bool isDefective = (UnityEngine.Random.value < 0.15f);
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

                // 박스 콜라이더 트리거화
                var col = pkgObj.GetComponent<Collider>();
                if (col != null) col.isTrigger = true;

                PackageItem item = pkgObj.GetComponent<PackageItem>() ?? pkgObj.AddComponent<PackageItem>();
                item.Initialize(dest, isDefective, isUrgent);

                // 언로딩 패드 상 4x4 격자 위치 계산
                int row = i / 4;
                int colIdx = i % 4;
                Vector3 groundPos = padCenter + new Vector3((colIdx - 1.5f) * 1.15f, 0f, (row - 1.5f) * 1.15f);

                // 이미 해당 자리에 물량이 있으면 층층이(2층, 3층...) 위로 쌓이도록 Y 좌표 계산!
                int stackLevel = GetExistingPackageStackCountAt(groundPos);
                float boxHeight = 0.65f;
                float targetY = 0.35f + (stackLevel * boxHeight);
                Vector3 targetPadPos = new Vector3(groundPos.x, targetY, groundPos.z);

                item.FlyTo(null, targetPadPos, 0.4f, () =>
                {
                    item.transform.position = targetPadPos;
                    SoundManager.Instance?.PlayDrop();
                });

                yield return new WaitForSeconds(0.12f);
            }
        }

        private int GetExistingPackageStackCountAt(Vector3 groundPos)
        {
            var packages = FindObjectsByType<PackageItem>(FindObjectsSortMode.None);
            int count = 0;
            foreach (var p in packages)
            {
                if (p.transform.parent == null && (p.State == PackageState.Unloaded || p.State == PackageState.ReInbounded))
                {
                    Vector2 p2D = new Vector2(p.transform.position.x, p.transform.position.z);
                    Vector2 g2D = new Vector2(groundPos.x, groundPos.z);
                    if (Vector2.Distance(p2D, g2D) < 0.55f)
                    {
                        count++;
                    }
                }
            }
            return count;
        }
    }
}
