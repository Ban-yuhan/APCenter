using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
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
        [SerializeField] private GameObject warningBubble;
        [SerializeField] private TextMesh nameTagText;

        private NavMeshAgent navAgent;
        private float actionCooldown = 0f;

        // 알바 상차/하차 루틴 상태 제어:
        // - isDeliveringBatch: 수집 완료 후 목표 지점에 0개가 될 때까지 내려놓는 상태
        // - isReturningToSorting: 출고 도크 이동/상차 중 트럭이 출발(또는 만차/회차)했을 때
        //   즉시 상차를 중단하고 해당 목적지 분류대의 북쪽 내려놓기 발판(PutZone)으로 유턴하여 잔여 택배를 모두 원복하는 상태
        private bool isDeliveringBatch = false;
        private bool isReturningToSorting = false;
        private PackageDestination lockedUnloadDestination = PackageDestination.None;
        private SortingStation lockedUnloadSortingStation = null;

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
            navAgent = GetComponent<NavMeshAgent>();

            if (visualTransform == null)
            {
                visualTransform = transform.Find("Visual") ?? transform;
            }

            if (nameTagText == null)
            {
                var nt = transform.Find("NameTag");
                if (nt != null) nameTagText = nt.GetComponent<TextMesh>();
                if (nameTagText == null) nameTagText = GetComponentInChildren<TextMesh>();
            }
            if (nameTagText != null)
            {
                nameTagText.fontSize = 45;
                nameTagText.color = Color.black;
                Billboard.Attach(nameTagText);
            }

            isDeliveringBatch = false;
            isReturningToSorting = false;
            lockedUnloadDestination = PackageDestination.None;
            lockedUnloadSortingStation = null;

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
            if (nameTagText != null && Data != null)
            {
                string rankKor = Data.rank == WorkerRank.Rookie ? "신입"
                               : Data.rank == WorkerRank.Junior ? "일반"
                               : Data.rank == WorkerRank.Senior ? "숙련" : "베테랑";
                string roleKor = Data.currentRole == WorkerRole.Transport ? "운반"
                               : Data.currentRole == WorkerRole.Load ? "적재"
                               : Data.currentRole == WorkerRole.Inspection ? "검수"
                               : Data.currentRole == WorkerRole.Sort ? "분류" : "상하차";
                int cap = Data.GetMaxStack();
                nameTagText.text = $"{Data.workerName} [{rankKor}|용량:{cap}]\n<{roleKor} 전담>";
            }

            if (warningBubble != null)
            {
                warningBubble.SetActive(Data.isMismatched);
            }
        }

        private void LateUpdate()
        {
            if (nameTagText != null && Camera.main != null)
            {
                nameTagText.transform.rotation = Camera.main.transform.rotation;
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

            // ① 출고 도크 이동/상차 중 목표 트럭 출발 실시간 감지 (쿨다운 중에도 매 프레임 즉시 감지!)
            if (Data.currentRole == WorkerRole.Load && isDeliveringBatch && !isReturningToSorting && Stack.Count > 0)
            {
                if (lockedUnloadDestination != PackageDestination.None)
                {
                    OutboundTruck targetTruck = OutboundTruck.GetTruckForDestination(lockedUnloadDestination);
                    if (!IsTruckDockedAndAvailable(targetTruck))
                    {
                        // 트럭이 출발했거나 만차/회차 중이면 즉시 경로 초기화 및 분류대 회차(Return To Sorting) 모드로 전환!
                        AbortOutboundAndReturnToSorting();
                    }
                }
            }

            if (actionCooldown > 0f)
            {
                actionCooldown -= Time.deltaTime;
                return;
            }

            EnsureFacilityReferences();
            ExecuteRoleAI();
        }

        private void AbortOutboundAndReturnToSorting()
        {
            ResetMovementPath();
            actionCooldown = 0f;
            isReturningToSorting = true;
        }

        private void ResetMovementPath()
        {
            if (navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh)
            {
                navAgent.ResetPath();
            }
        }

        private bool IsTruckDockedAndAvailable(OutboundTruck truck)
        {
            if (truck == null) return false;
            if (truck.IsDeparting || truck.IsReturning || truck.IsFull || !truck.CanLoadPackage()) return false;
            return true;
        }

        /// <summary>
        /// 움직이는 트럭의 Transform 좌표 대신, 창고 내부 고정된 출고 도크 상차 발판(LoadingZone) 좌표를 반환합니다.
        /// (트럭이 출발해 맵 밖으로 이동하더라도 알바가 절대 맵 밖으로 따라나가지 않도록 방지)
        /// </summary>
        private Vector3 GetFixedDockLoadingPosition(PackageDestination dest)
        {
            float dockX = (dest == PackageDestination.Seoul) ? -7f
                        : (dest == PackageDestination.Busan) ? 0f
                        : 7f;
            return new Vector3(dockX, 0f, -11.2f);
        }

        private void ExecuteRoleAI()
        {
            float speed = Data.GetSpeed();
            int maxCap = Data.GetMaxStack();

            switch (Data.currentRole)
            {
                case WorkerRole.Unload:
                case WorkerRole.Transport:
                    ExecuteInboundToSortingBehavior(speed, maxCap);
                    break;
                case WorkerRole.Sort:
                    ExecuteSortBehavior(speed, maxCap);
                    break;
                case WorkerRole.Load:
                    ExecuteSortingToOutboundBehavior(speed, maxCap);
                    break;
                case WorkerRole.Inspection:
                    ExecuteInspectionBehavior(speed, maxCap);
                    break;
            }
        }

        #region Role Behaviors

        /// <summary>
        /// 입고장 ➔ 분류대 운반 루틴 (Unload / Transport):
        /// 1. 수집 단계(!isDeliveringBatch): 자신의 개별 maxCapacity까지 입고장 택배를 수집.
        /// 2. 하차 단계(isDeliveringBatch): 분류대 북쪽 PutZone에 도착하면 해당 지역 택배가 0개가 될 때까지 0.15초 간격으로 전부 내려놓음.
        /// </summary>
        private void ExecuteInboundToSortingBehavior(float speed, int maxCap)
        {
            Vector3 inPos = (InboundPadTrans != null) ? InboundPadTrans.position : new Vector3(-8f, 0f, 12f);

            if (Stack.Count == 0)
            {
                isDeliveringBatch = false;
                isReturningToSorting = false;
                lockedUnloadDestination = PackageDestination.None;
                lockedUnloadSortingStation = null;
            }

            if (!isDeliveringBatch)
            {
                var nearbyPkg = FindNearestUncollectedPackage(inPos, 7.5f);
                if (Stack.Count < maxCap && nearbyPkg != null)
                {
                    MoveTowards(inPos, speed, () =>
                    {
                        var pkg = FindNearestUncollectedPackage(inPos, 7.5f);
                        if (pkg != null && Stack.CanAdd(maxCap))
                        {
                            Stack.Push(pkg);
                            actionCooldown = 0.15f;
                            if (Stack.Count >= maxCap)
                            {
                                isDeliveringBatch = true;
                            }
                        }
                        else if (Stack.Count > 0)
                        {
                            isDeliveringBatch = true;
                        }
                    });
                    return;
                }
                else if (Stack.Count > 0)
                {
                    isDeliveringBatch = true;
                }
                else
                {
                    MoveTowards(inPos + new Vector3(2.2f, 0f, -1.5f), speed, null);
                    return;
                }
            }

            if (isDeliveringBatch && Stack.Count > 0)
            {
                if (lockedUnloadDestination == PackageDestination.None || !Stack.HasMatching(lockedUnloadDestination))
                {
                    var top = Stack.Peek();
                    if (top != null)
                    {
                        lockedUnloadDestination = top.Destination;
                        lockedUnloadSortingStation = GetStationForDest(lockedUnloadDestination);
                    }
                }

                SortingStation targetStation = lockedUnloadSortingStation ?? GetStationForDest(lockedUnloadDestination);
                Vector3 putZonePos = (targetStation != null) ? targetStation.PutZoneWorldPos : new Vector3(0f, 0f, 3.8f);

                MoveTowards(putZonePos, speed, () =>
                {
                    var matched = Stack.FindMatching(lockedUnloadDestination);
                    if (matched != null)
                    {
                        if (targetStation != null) targetStation.SortPackage(matched);
                        else matched.transform.position = putZonePos;

                        actionCooldown = 0.15f;
                    }

                    if (!Stack.HasMatching(lockedUnloadDestination))
                    {
                        lockedUnloadDestination = PackageDestination.None;
                        lockedUnloadSortingStation = null;
                    }

                    if (Stack.Count == 0)
                    {
                        isDeliveringBatch = false;
                    }
                });
            }
        }

        private void ExecuteSortBehavior(float speed, int maxCap)
        {
            Vector3 inPos = (InboundPadTrans != null) ? InboundPadTrans.position : new Vector3(-8f, 0f, 12f);

            if (Stack.Count == 0)
            {
                isDeliveringBatch = false;
                isReturningToSorting = false;
                lockedUnloadDestination = PackageDestination.None;
                lockedUnloadSortingStation = null;
            }

            if (!isDeliveringBatch)
            {
                var nearbyPkg = FindNearestUncollectedPackage(inPos, 8.0f);
                if (Stack.Count < maxCap && nearbyPkg != null)
                {
                    MoveTowards(inPos, speed, () =>
                    {
                        var p = FindNearestUncollectedPackage(inPos, 8.0f);
                        if (p != null && Stack.CanAdd(maxCap))
                        {
                            Stack.Push(p);
                            actionCooldown = 0.15f;
                            if (Stack.Count >= maxCap)
                            {
                                isDeliveringBatch = true;
                            }
                        }
                        else if (Stack.Count > 0)
                        {
                            isDeliveringBatch = true;
                        }
                    });
                    return;
                }
                else if (Stack.Count > 0)
                {
                    isDeliveringBatch = true;
                }
                else
                {
                    MoveTowards(inPos + new Vector3(-2.2f, 0f, -1.5f), speed, null);
                    return;
                }
            }

            if (isDeliveringBatch && Stack.Count > 0)
            {
                if (lockedUnloadDestination == PackageDestination.None || !Stack.HasMatching(lockedUnloadDestination))
                {
                    var pkg = Stack.Peek();
                    if (pkg != null)
                    {
                        lockedUnloadDestination = pkg.Destination;
                        lockedUnloadSortingStation = GetStationForDest(lockedUnloadDestination);

                        float errorProb = (1f - Data.GetAccuracyRate()) * (Data.isMismatched ? 2.0f : 1.0f);
                        if (UnityEngine.Random.value < errorProb && StationBlue != null)
                        {
                            lockedUnloadSortingStation = StationBlue;
                        }
                    }
                }

                SortingStation targetStation = lockedUnloadSortingStation ?? GetStationForDest(lockedUnloadDestination);
                Vector3 putPos = (targetStation != null) ? targetStation.PutZoneWorldPos : new Vector3(0f, 0f, 3.8f);

                MoveTowards(putPos, speed, () =>
                {
                    var matched = Stack.FindMatching(lockedUnloadDestination);
                    if (matched != null)
                    {
                        if (targetStation != null) targetStation.SortPackage(matched);
                        else matched.transform.position = putPos;

                        actionCooldown = 0.15f;
                    }

                    if (!Stack.HasMatching(lockedUnloadDestination))
                    {
                        lockedUnloadDestination = PackageDestination.None;
                        lockedUnloadSortingStation = null;
                    }

                    if (Stack.Count == 0)
                    {
                        isDeliveringBatch = false;
                    }
                });
            }
        }

        /// <summary>
        /// 분류대 ➔ 출고 도크 상차 루틴 (Load):
        /// ① 트럭 출발 실시간 감지: 이동/상차 중 트럭이 출발(또는 쿨타임 회차/만차)하면 즉시 경로 초기화 및 상차 중단.
        /// ② 분류대 회차 및 원복(Return To Sorting): 손에 든 잔여 택배를 가지고 해당 목적지 분류 구역 [북쪽 내려놓기 발판(PutZone)]으로 유턴하여
        ///    0.15초 간격으로 손이 완전히 빌 때까지(0개) 모두 다시 내려놓음.
        /// ③ 다음 작업 전환: 손이 완전히 빈손이 된 후 다른 출발 대기 중인 정상 도크의 택배를 찾거나 대기 상태로 복귀.
        /// </summary>
        private void ExecuteSortingToOutboundBehavior(float speed, int maxCap)
        {
            // ③ 손이 완전히 빈손(0개)이 되면 모든 플래그 초기화 후 다음 작업으로 전환
            if (Stack.Count == 0)
            {
                isDeliveringBatch = false;
                isReturningToSorting = false;
                lockedUnloadDestination = PackageDestination.None;
                lockedUnloadSortingStation = null;
            }

            // ② 분류대 회차 및 원복 (Return To Sorting) 루틴:
            // 트럭이 떠나버려 상차가 중단된 경우, 손이 완전히 빌 때까지(0개) 해당 목적지 분류대의 북쪽 PutZone에 0.15초 간격으로 모두 다시 내려놓음!
            if (isReturningToSorting && Stack.Count > 0)
            {
                var topPkg = Stack.Peek();
                PackageDestination returnDest = (topPkg != null) ? topPkg.Destination : lockedUnloadDestination;
                SortingStation returnStation = GetStationForDest(returnDest);
                Vector3 putZonePos = (returnStation != null) ? returnStation.PutZoneWorldPos : new Vector3(0f, 0f, 3.8f);

                MoveTowards(putZonePos, speed, () =>
                {
                    var pkgToReturn = Stack.FindMatching(returnDest) ?? Stack.Pop();
                    if (pkgToReturn != null)
                    {
                        SortingStation st = GetStationForDest(pkgToReturn.Destination) ?? returnStation;
                        if (st != null)
                        {
                            st.SortPackage(pkgToReturn);
                        }
                        actionCooldown = 0.15f;
                    }

                    // 손이 완전히 빈손(0개)이 되면 원복 완료 -> 다음 작업 전환!
                    if (Stack.Count == 0)
                    {
                        isReturningToSorting = false;
                        isDeliveringBatch = false;
                        lockedUnloadDestination = PackageDestination.None;
                        lockedUnloadSortingStation = null;
                    }
                });
                return;
            }

            // 1. 수집 단계: 정상 대기 중인 출고 트럭의 분류대(남쪽 TakeZone)에서 개별 maxCapacity까지 수집
            if (!isDeliveringBatch)
            {
                // 만약 수집 도중 목표 트럭이 출발해버렸고 손에 이미 집은 택배가 있다면 즉시 북쪽 PutZone으로 원복!
                if (Stack.Count > 0)
                {
                    var heldTop = Stack.Peek();
                    if (heldTop != null)
                    {
                        OutboundTruck checkTruck = OutboundTruck.GetTruckForDestination(heldTop.Destination);
                        if (!IsTruckDockedAndAvailable(checkTruck))
                        {
                            AbortOutboundAndReturnToSorting();
                            return;
                        }
                    }
                }

                SortingStation source = ChooseAvailableDockSortingStation();
                if (source != null && Stack.Count < maxCap)
                {
                    Vector3 takePos = source.TakeZoneWorldPos;
                    MoveTowards(takePos, speed, () =>
                    {
                        // 집기 직전에도 해당 목적지 트럭이 정상 대기 중인지 확인
                        OutboundTruck checkTruck = OutboundTruck.GetTruckForDestination(source.targetDestination);
                        if (!IsTruckDockedAndAvailable(checkTruck))
                        {
                            if (Stack.Count > 0)
                            {
                                AbortOutboundAndReturnToSorting();
                            }
                            return;
                        }

                        if (source.HasSortedPackages() && Stack.CanAdd(maxCap))
                        {
                            var p = source.PopSortedPackage();
                            if (p != null)
                            {
                                Stack.Push(p);
                                lockedUnloadDestination = p.Destination;
                                actionCooldown = 0.15f;
                            }
                        }

                        if (Stack.Count >= maxCap || (!source.HasSortedPackages() && Stack.Count > 0))
                        {
                            isDeliveringBatch = true;
                        }
                    });
                    return;
                }
                else if (Stack.Count > 0)
                {
                    isDeliveringBatch = true;
                }
                else
                {
                    // 출발 대기 중인 도크 택배가 없으면 중앙 대기 위치에서 대기
                    MoveTowards(new Vector3(0f, 0f, -2.5f), speed, null);
                    return;
                }
            }

            // 2. 상차 단계: 고정된 도크 상차 발판으로 이동하여 0.15초 간격으로 전부 상차 (트럭 출발 감지 시 즉시 분류대 PutZone으로 회차)
            if (isDeliveringBatch && Stack.Count > 0)
            {
                if (lockedUnloadDestination == PackageDestination.None || !Stack.HasMatching(lockedUnloadDestination))
                {
                    var top = Stack.Peek();
                    if (top != null) lockedUnloadDestination = top.Destination;
                }

                OutboundTruck targetTruck = OutboundTruck.GetTruckForDestination(lockedUnloadDestination);

                // ① 실시간 트럭 출발/회차/만차 감지 -> 즉시 상차 중단 및 분류대 북쪽 PutZone으로 회차 원복!
                if (!IsTruckDockedAndAvailable(targetTruck))
                {
                    AbortOutboundAndReturnToSorting();
                    return;
                }

                // 트럭 오브젝트의 실시간 위치가 아닌 창고 내부의 고정 도크 상차 위치로 이동 (맵 밖 이탈 원천 차단)
                Vector3 fixedDockLoadPos = GetFixedDockLoadingPosition(lockedUnloadDestination);
                MoveTowards(fixedDockLoadPos, speed, () =>
                {
                    // 도착 순간 트럭이 출발했거나 만차가 되었으면 즉시 분류대 북쪽 PutZone으로 회차 원복!
                    if (!IsTruckDockedAndAvailable(targetTruck))
                    {
                        AbortOutboundAndReturnToSorting();
                        return;
                    }

                    var pkg = Stack.FindMatching(lockedUnloadDestination);
                    if (pkg != null)
                    {
                        if (!targetTruck.LoadPackage(pkg))
                        {
                            // 만약 트럭이 방금 출발/만차되어 상차에 실패했다면 다시 스택에 넣고 분류대로 원복
                            Stack.Push(pkg);
                            AbortOutboundAndReturnToSorting();
                            return;
                        }
                        actionCooldown = 0.15f;
                    }

                    // 손에 든 해당 도크 택배가 완전히 0개가 되었는지 확인
                    if (!Stack.HasMatching(lockedUnloadDestination))
                    {
                        lockedUnloadDestination = PackageDestination.None;
                        if (Stack.Count == 0)
                        {
                            isDeliveringBatch = false;
                        }
                    }
                    else if (!IsTruckDockedAndAvailable(targetTruck))
                    {
                        // 방금 상차로 인해 트럭이 10칸 만차 출발한 경우 남은 택배는 분류대 PutZone으로 원복!
                        AbortOutboundAndReturnToSorting();
                    }
                });
            }
        }

        /// <summary>
        /// 현재 도크에 정상 대기 중(출발/복귀/만차가 아닌 상태)인 트럭의 분류대만 선택합니다.
        /// </summary>
        private SortingStation ChooseAvailableDockSortingStation()
        {
            if (Stack.Count > 0)
            {
                var top = Stack.Peek();
                if (top != null)
                {
                    OutboundTruck t = OutboundTruck.GetTruckForDestination(top.Destination);
                    SortingStation sameSt = GetStationForDest(top.Destination);
                    if (IsTruckDockedAndAvailable(t) && sameSt != null && sameSt.HasSortedPackages())
                    {
                        return sameSt;
                    }
                }
            }

            SortingStation[] stations = { StationRed, StationBlue, StationGreen };
            foreach (var st in stations)
            {
                if (st != null && st.HasSortedPackages())
                {
                    var truck = OutboundTruck.GetTruckForDestination(st.targetDestination);
                    if (IsTruckDockedAndAvailable(truck))
                    {
                        return st;
                    }
                }
            }

            return null;
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
            // 창고 내부 안전 경계 클램핑 (맵 밖 이탈 원천 방지)
            target.x = Mathf.Clamp(target.x, -16.0f, 16.0f);
            target.z = Mathf.Clamp(target.z, -13.5f, 15.0f);
            target.y = 0f;

            Vector3 diff = target - transform.position;
            diff.y = 0;
            if (diff.magnitude > 1.0f)
            {
                Vector3 dir = diff.normalized;
                Vector3 nextPos = transform.position + dir * speed * Time.deltaTime;
                nextPos.x = Mathf.Clamp(nextPos.x, -16.5f, 16.5f);
                nextPos.z = Mathf.Clamp(nextPos.z, -14.0f, 15.5f);
                nextPos.y = 0f;

                transform.position = nextPos;
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
                if (p is PalletItem) continue;
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
