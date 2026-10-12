using System;
using UnityEngine;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Facilities;

namespace APCenter.Player
{
    public enum SortingZoneInteractionState
    {
        None = 0,           // 분류대 영역 밖
        Depositing = 1,     // 상자를 들고 진입: 내려놓기 진행
        PickingUp = 2,      // 빈손으로 진입: 최대 4개까지 들기 진행
        IdleCompleted = 3   // 작업 완료! 영역을 벗어날 때까지 대기 (반복 차단)
    }

    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PackageStack))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        private CharacterController controller;
        public PackageStack Stack { get; private set; }

        [Header("Movement")]
        [SerializeField] private float turnSpeed = 14f;
        [SerializeField] private Transform visualModel;

        [Header("Procedural Walk Animation")]
        private float walkCycle = 0f;
        private Vector3 initialScale;

        // 조이스틱 드래그 입력 지원
        public Vector2 JoystickInput { get; set; } = Vector2.zero;

        // 주변 상호작용 쿨다운 타이머
        private float interactTimer = 0f;

        // 분류 영역 Enum FSM 상태 관리
        private SortingZoneInteractionState sortingState = SortingZoneInteractionState.None;
        private SortingStation activeSortingStation = null;
        private const int MaxCarryFromSorting = 4; // 분류대에서 출고로 옮길 때 최대 4개까지 들기!

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            controller = GetComponent<CharacterController>();
            Stack = GetComponent<PackageStack>();

            if (visualModel == null)
            {
                visualModel = transform.Find("Visual") ?? transform;
            }
            initialScale = visualModel.localScale;
        }

        private void Update()
        {
            HandleMovement();
            HandleProximityInteractions();
        }

        private void HandleMovement()
        {
            float moveX = Input.GetAxisRaw("Horizontal");
            float moveZ = Input.GetAxisRaw("Vertical");

            if (JoystickInput.sqrMagnitude > 0.05f)
            {
                moveX = JoystickInput.x;
                moveZ = JoystickInput.y;
            }

            Vector3 moveDir = new Vector3(moveX, 0f, moveZ).normalized;

            float speed = (GameManager.Instance != null) ? GameManager.Instance.GetPlayerMoveSpeed() : 7.0f;

            if (moveDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                visualModel.rotation = Quaternion.Slerp(visualModel.rotation, targetRot, Time.deltaTime * turnSpeed);

                controller.Move(moveDir * speed * Time.deltaTime);

                Stack.AddWobbleImpulse(moveDir);

                walkCycle += Time.deltaTime * speed * 2.2f;
                float bounce = Mathf.Sin(walkCycle) * 0.08f;
                float squash = Mathf.Cos(walkCycle) * 0.04f;
                visualModel.localScale = new Vector3(
                    initialScale.x * (1f + squash),
                    initialScale.y * (1f + bounce),
                    initialScale.z * (1f - squash)
                );
            }
            else
            {
                visualModel.localScale = Vector3.Lerp(visualModel.localScale, initialScale, Time.deltaTime * 10f);
            }

            controller.Move(Vector3.down * 9.81f * Time.deltaTime);
        }

        private void HandleProximityInteractions()
        {
            // 1. 분류대(SortingStation) 인터랙션 (Enum FSM 상태 관리)
            var sortingStations = FindObjectsByType<SortingStation>(FindObjectsSortMode.None);
            SortingStation nearbyStation = null;
            foreach (var st in sortingStations)
            {
                if (Vector3.Distance(transform.position, st.transform.position) < 3.2f)
                {
                    nearbyStation = st;
                    break;
                }
            }

            if (nearbyStation != null)
            {
                // 새로 영역에 진입한 순간 상태 결정
                if (activeSortingStation != nearbyStation || sortingState == SortingZoneInteractionState.None)
                {
                    activeSortingStation = nearbyStation;
                    if (Stack.Count > 0)
                    {
                        sortingState = SortingZoneInteractionState.Depositing; // 상자 들고 진입 -> 놓기만!
                    }
                    else
                    {
                        sortingState = SortingZoneInteractionState.PickingUp;  // 빈손으로 진입 -> 들기만!
                    }
                }

                // 0.15초마다 1개씩 처리
                interactTimer += Time.deltaTime;
                if (interactTimer >= 0.15f)
                {
                    interactTimer = 0f;

                    if (sortingState == SortingZoneInteractionState.Depositing)
                    {
                        // 일치하는 택배가 있으면 1개씩 내려놓기
                        var match = Stack.FindMatching(activeSortingStation.targetDestination);
                        if (match != null)
                        {
                            activeSortingStation.SortPackage(match);
                        }
                        else
                        {
                            // 더 이상 내려놓을 상자가 없으면 즉시 IdleCompleted 상태로 전환! (절대 집지 않음)
                            sortingState = SortingZoneInteractionState.IdleCompleted;
                        }
                    }
                    else if (sortingState == SortingZoneInteractionState.PickingUp)
                    {
                        // 빈손으로 들어온 경우: 최대 4개까지 들기!
                        if (Stack.Count < MaxCarryFromSorting && activeSortingStation.HasSortedPackages())
                        {
                            var sorted = activeSortingStation.PopSortedPackage();
                            if (sorted != null)
                            {
                                Stack.Push(sorted);
                            }
                        }
                        else
                        {
                            // 4개를 다 채웠거나 남은 상자가 없으면 즉시 IdleCompleted 상태로 전환! (절대 다시 놓지 않음)
                            sortingState = SortingZoneInteractionState.IdleCompleted;
                        }
                    }
                    else if (sortingState == SortingZoneInteractionState.IdleCompleted)
                    {
                        // 영역을 벗어날 때까지는 아무 작업도 하지 않고 완벽히 IDLE 대기!
                    }
                }
                return;
            }
            else
            {
                // 분류 영역을 완전히 벗어난 경우 상태 리셋!
                activeSortingStation = null;
                sortingState = SortingZoneInteractionState.None;
            }

            // 분류 영역 밖에서의 일반 상호작용
            interactTimer += Time.deltaTime;
            if (interactTimer < 0.15f) return;
            interactTimer = 0f;

            int maxCap = (GameManager.Instance != null) ? GameManager.Instance.GetPlayerMaxStack() : 4;

            // 2. 출고 트럭(OutboundTruck) 근처: 해당 트럭 지역 색깔과 일치하는 택배만 적재!
            var outTruck = FindAnyObjectByType<OutboundTruck>();
            if (outTruck != null && Vector3.Distance(transform.position, outTruck.transform.position) < 6.0f)
            {
                if (Stack.Count > 0 && outTruck.CanLoadPackage())
                {
                    var matchPkg = Stack.FindMatching(outTruck.targetDestination);
                    if (matchPkg != null)
                    {
                        outTruck.LoadPackage(matchPkg);
                        return;
                    }
                }
            }

            // 3. 반품 구역(ReturnStation) 근처: 불량 박스 넣기 또는 검수
            var returnStation = FindAnyObjectByType<ReturnStation>();
            if (returnStation != null && Vector3.Distance(transform.position, returnStation.transform.position) < 3.5f)
            {
                var def = Stack.FindDefective();
                if (def != null)
                {
                    returnStation.AddReturnPackage(def);
                    return;
                }
                else if (returnStation.QueueCount > 0)
                {
                    returnStation.InspectNextPackage(0.95f, "플레이어");
                    return;
                }
            }

            // 4. 바닥의 미분류 택배 줍기 (언로딩 패드 근처)
            if (Stack.Count < maxCap)
            {
                var packages = FindObjectsByType<PackageItem>(FindObjectsSortMode.None);
                PackageItem nearest = null;
                float minD = 3.0f;

                foreach (var p in packages)
                {
                    if (p.transform.parent == null && (p.State == PackageState.Unloaded || p.State == PackageState.ReInbounded))
                    {
                        float d = Vector3.Distance(transform.position, p.transform.position);
                        if (d < minD)
                        {
                            minD = d;
                            nearest = p;
                        }
                    }
                }

                if (nearest != null)
                {
                    Stack.Push(nearest);
                }
            }
        }
    }
}
