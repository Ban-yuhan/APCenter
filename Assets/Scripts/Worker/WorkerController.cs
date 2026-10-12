using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Facilities;
using APCenter.Player;

namespace APCenter.Worker
{
    public class WorkerController : MonoBehaviour
    {
        public WorkerData Data { get; private set; }
        public PackageStack Stack { get; private set; }

        [Header("Visuals")]
        [SerializeField] private Transform visualTransform;
        [SerializeField] private GameObject warningBubble; // 부적합 배치 경고 말풍선
        [SerializeField] private TextMesh nameTagText;

        private float actionCooldown = 0f;

        // 캐싱된 센터 주요 시설 위치들
        public static Transform InboundPadTrans;
        public static SortingStation StationRed;
        public static SortingStation StationBlue;
        public static SortingStation StationGreen;
        public static ReturnStation ReturnFacility;
        public static OutboundTruck OutTruck;

        public event Action<WorkerController> OnDeserted;

        public void Initialize(WorkerData data)
        {
            Data = data;
            Stack = GetComponent<PackageStack>() ?? gameObject.AddComponent<PackageStack>();

            if (visualTransform == null)
            {
                visualTransform = transform.Find("Visual") ?? transform;
            }

            EnsureFacilityReferences();
            UpdateVisualAppearance();
        }

        public static void EnsureFacilityReferences()
        {
            if (InboundPadTrans == null)
            {
                var pad = GameObject.Find("UnloadPad");
                if (pad != null) InboundPadTrans = pad.transform;
            }

            if (StationRed == null || StationBlue == null || StationGreen == null)
            {
                var stations = UnityEngine.Object.FindObjectsByType<SortingStation>(FindObjectsSortMode.None);
                foreach (var st in stations)
                {
                    if (st.targetDestination == PackageDestination.Seoul) StationRed = st;
                    else if (st.targetDestination == PackageDestination.Busan) StationBlue = st;
                    else if (st.targetDestination == PackageDestination.Daegu) StationGreen = st;
                }
            }

            if (ReturnFacility == null)
            {
                ReturnFacility = UnityEngine.Object.FindAnyObjectByType<ReturnStation>();
            }

            if (OutTruck == null)
            {
                OutTruck = UnityEngine.Object.FindAnyObjectByType<OutboundTruck>();
            }
        }

        public void UpdateVisualAppearance()
        {
            if (nameTagText != null)
            {
                string rankName = Data.rank.ToString();
                nameTagText.text = $"{Data.workerName}\n[{rankName}]";
            }

            if (warningBubble != null)
            {
                warningBubble.SetActive(Data.isMismatched);
            }
        }

        private void Update()
        {
            if (Data == null) return;

            // 탈주 경고 및 유예시간 체크
            if (Data.isMismatched)
            {
                Data.desertionGraceTimer -= Time.deltaTime;
                if (!Data.isDesertionWarning && Data.desertionGraceTimer < 30f)
                {
                    Data.isDesertionWarning = true;
                    SoundManager.Instance?.PlayAlert();
                }

                if (Data.desertionGraceTimer <= 0f)
                {
                    TriggerDesertion();
                    return;
                }
            }
            else
            {
                Data.desertionGraceTimer = Mathf.Min(45f, Data.desertionGraceTimer + Time.deltaTime * 0.5f);
                Data.isDesertionWarning = false;
            }

            if (warningBubble != null)
            {
                warningBubble.SetActive(Data.isMismatched);
            }

            Data.workHoursAccumulated += Time.deltaTime;

            if (actionCooldown > 0f)
            {
                actionCooldown -= Time.deltaTime;
                return;
            }

            EnsureFacilityReferences();
            ExecuteRoleAI();
        }

        private void ExecuteRoleAI()
        {
            float speed = Data.GetSpeed();
            int maxCap = Data.GetMaxStack();

            switch (Data.currentRole)
            {
                case WorkerRole.Unload:
                    ExecuteUnloadBehavior(speed, maxCap);
                    break;
                case WorkerRole.Sort:
                    ExecuteSortBehavior(speed, maxCap);
                    break;
                case WorkerRole.Transport:
                    ExecuteTransportBehavior(speed, maxCap);
                    break;
                case WorkerRole.Load:
                    ExecuteLoadBehavior(speed, maxCap);
                    break;
                case WorkerRole.Inspection:
                    ExecuteInspectionBehavior(speed, maxCap);
                    break;
            }
        }

        #region Role Behaviors

        private void ExecuteUnloadBehavior(float speed, int maxCap)
        {
            Vector3 inPos = (InboundPadTrans != null) ? InboundPadTrans.position : new Vector3(-8f, 0f, 12f);

            // 손에 박스가 없거나, 아직 더 실을 수 있고 바닥에 박스가 있는 경우 줍기
            var nearbyPkg = FindNearestUncollectedPackage(inPos, 6.0f);

            if (Stack.Count == 0 || (Stack.Count < maxCap && nearbyPkg != null))
            {
                MoveTowards(inPos, speed, () =>
                {
                    var pkg = FindNearestUncollectedPackage(inPos, 6.0f);
                    if (pkg != null && Stack.CanAdd(maxCap))
                    {
                        Stack.Push(pkg);
                        actionCooldown = 0.18f;
                    }
                });
            }
            else
            {
                // 손에 박스가 1개라도 있으면 즉시 분류대로 운반!
                var held = Stack.Peek();
                if (held != null)
                {
                    SortingStation targetStation = GetStationForDest(held.Destination);
                    Vector3 destPos = (targetStation != null) ? targetStation.transform.position : new Vector3(0f, 0f, 2f);

                    MoveTowards(destPos, speed, () =>
                    {
                        var dropped = Stack.Pop();
                        if (dropped != null)
                        {
                            if (targetStation != null) targetStation.SortPackage(dropped);
                            else dropped.transform.position = destPos;
                            actionCooldown = 0.2f;
                        }
                    });
                }
            }
        }

        private void ExecuteSortBehavior(float speed, int maxCap)
        {
            Vector3 pickupPos = (InboundPadTrans != null) ? InboundPadTrans.position : new Vector3(-8f, 0f, 12f);
            var nearbyPkg = FindNearestUncollectedPackage(pickupPos, 8.0f);

            if (Stack.Count == 0)
            {
                MoveTowards(pickupPos, speed, () =>
                {
                    var p = FindNearestUncollectedPackage(pickupPos, 8.0f);
                    if (p != null)
                    {
                        Stack.Push(p);
                        actionCooldown = 0.2f;
                    }
                });
            }
            else
            {
                var pkg = Stack.Peek();
                if (pkg == null) return;

                SortingStation targetStation = GetStationForDest(pkg.Destination);

                float errorProb = (1f - Data.GetAccuracyRate()) * (Data.isMismatched ? 2.0f : 1.0f);
                if (UnityEngine.Random.value < errorProb)
                {
                    targetStation = StationBlue; // 오분류
                }

                Vector3 targetPos = (targetStation != null) ? targetStation.transform.position : new Vector3(0f, 0f, 2f);
                MoveTowards(targetPos, speed, () =>
                {
                    var dropped = Stack.Pop();
                    if (dropped != null && targetStation != null)
                    {
                        targetStation.SortPackage(dropped);
                        actionCooldown = 0.2f;
                    }
                });
            }
        }

        private void ExecuteTransportBehavior(float speed, int maxCap)
        {
            PackageDestination targetDest = (OutTruck != null) ? OutTruck.targetDestination : PackageDestination.Seoul;
            SortingStation preferredStation = GetStationForDest(targetDest);

            if (Stack.Count < maxCap)
            {
                SortingStation source = null;
                if (preferredStation != null && preferredStation.HasSortedPackages()) source = preferredStation;
                else if (StationRed != null && StationRed.HasSortedPackages()) source = StationRed;
                else if (StationBlue != null && StationBlue.HasSortedPackages()) source = StationBlue;
                else if (StationGreen != null && StationGreen.HasSortedPackages()) source = StationGreen;

                if (source != null)
                {
                    MoveTowards(source.transform.position, speed, () =>
                    {
                        var p = source.PopSortedPackage();
                        if (p != null)
                        {
                            Stack.Push(p);
                            actionCooldown = 0.18f;
                        }
                    });
                    return;
                }
            }

            // 손에 든 것이 있으면 출고 트럭이나 대기장으로 이동
            if (Stack.Count > 0)
            {
                var held = Stack.Peek();
                if (OutTruck != null && OutTruck.CanLoadPackage(held))
                {
                    MoveTowards(OutTruck.transform.position, speed, () =>
                    {
                        var p = Stack.Pop();
                        if (p != null)
                        {
                            OutTruck.LoadPackage(p);
                            actionCooldown = 0.2f;
                        }
                    });
                }
                else
                {
                    Vector3 stagingPos = new Vector3(8f, 0f, -10f);
                    MoveTowards(stagingPos, speed, () =>
                    {
                        var p = Stack.Pop();
                        if (p != null)
                        {
                            p.transform.position = stagingPos + new Vector3(UnityEngine.Random.Range(-1f, 1f), 0.2f, UnityEngine.Random.Range(-1f, 1f));
                            actionCooldown = 0.2f;
                        }
                    });
                }
            }
        }

        private void ExecuteLoadBehavior(float speed, int maxCap)
        {
            PackageDestination targetDest = (OutTruck != null) ? OutTruck.targetDestination : PackageDestination.Seoul;
            SortingStation preferredStation = GetStationForDest(targetDest);

            if (Stack.Count == 0)
            {
                if (preferredStation != null && preferredStation.HasSortedPackages())
                {
                    MoveTowards(preferredStation.transform.position, speed, () =>
                    {
                        var p = preferredStation.PopSortedPackage();
                        if (p != null)
                        {
                            Stack.Push(p);
                            actionCooldown = 0.18f;
                        }
                    });
                }
            }
            else
            {
                var held = Stack.Peek();
                if (OutTruck != null && OutTruck.CanLoadPackage(held))
                {
                    MoveTowards(OutTruck.transform.position, speed, () =>
                    {
                        var p = Stack.Pop();
                        if (p != null)
                        {
                            OutTruck.LoadPackage(p);
                            actionCooldown = 0.2f;
                        }
                    });
                }
            }
        }

        private void ExecuteInspectionBehavior(float speed, int maxCap)
        {
            if (ReturnFacility != null)
            {
                MoveTowards(ReturnFacility.transform.position, speed, () =>
                {
                    if (ReturnFacility.QueueCount > 0)
                    {
                        ReturnFacility.InspectNextPackage(Data.GetAccuracyRate(), Data.workerName);
                        actionCooldown = 1.0f;
                    }
                });
            }
        }

        #endregion

        private void MoveTowards(Vector3 target, float speed, Action onArrived)
        {
            Vector3 diff = target - transform.position;
            diff.y = 0;
            if (diff.magnitude > 1.0f)
            {
                Vector3 dir = diff.normalized;
                transform.position += dir * speed * Time.deltaTime;
                visualTransform.rotation = Quaternion.Slerp(visualTransform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);
            }
            else
            {
                onArrived?.Invoke();
            }
        }

        private PackageItem FindNearestUncollectedPackage(Vector3 center, float radius)
        {
            var packages = FindObjectsByType<PackageItem>(FindObjectsSortMode.None);
            PackageItem best = null;
            float minD = float.MaxValue;
            foreach (var p in packages)
            {
                if (p.transform.parent == null && (p.State == PackageState.Unloaded || p.State == PackageState.ReInbounded))
                {
                    float d = Vector3.Distance(center, p.transform.position);
                    if (d < radius && d < minD)
                    {
                        minD = d;
                        best = p;
                    }
                }
            }
            return best;
        }

        private SortingStation GetStationForDest(PackageDestination dest)
        {
            switch (dest)
            {
                case PackageDestination.Seoul: return StationRed;
                case PackageDestination.Busan: return StationBlue;
                case PackageDestination.Daegu: return StationGreen;
                default: return StationRed;
            }
        }

        private void TriggerDesertion()
        {
            SoundManager.Instance?.PlayAlert();
            ReputationSystem.Instance?.PenaltyWorkerDesertion();
            OnDeserted?.Invoke(this);

            StartCoroutine(DesertionCoroutine());
        }

        private IEnumerator DesertionCoroutine()
        {
            enabled = false;
            Vector3 exitPos = new Vector3(-25f, 0f, 0f);
            float elapsed = 0f;
            Vector3 start = transform.position;
            while (elapsed < 3f)
            {
                elapsed += Time.deltaTime;
                transform.position = Vector3.Lerp(start, exitPos, elapsed / 3f);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
