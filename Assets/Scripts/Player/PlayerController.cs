using System;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Facilities;
using APCenter.UI;

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

        // 지게차 탑승 운전 상태
        public bool IsDrivingForklift { get; private set; } = false;
        public ForkliftController ActiveForklift { get; private set; } = null;

        // 주변 상호작용 쿨다운 타이머
        private float interactTimer = 0f;

        // 분류 영역 Enum FSM 상태 관리
        private SortingZoneInteractionState sortingState = SortingZoneInteractionState.None;
        private SortingStation activeSortingStation = null;
        private const int MaxCarryFromSorting = 4; // 분류대에서 출고로 옮길 때 최대 4개까지 들기

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
            // 지게차 운전 중일 때는 조작 입력(WASD)을 ForkliftController에서 처리합니다.
            if (IsDrivingForklift)
            {
                return;
            }

            HandleMovement();
            HandleProximityInteractions();
        }

        /// <summary>
        /// 지게차 탑승 시 호출:
        /// ① 플레이어 비주얼 메쉬를 끄고(playerVisual.SetActive(false)),
        /// ② 플레이어 트랜스폼을 지게차의 driverSeatPoint 자식으로 배치하여 운전 모드로 전환합니다.
        /// </summary>
        public void EnterForkliftDriveMode(ForkliftController forklift, Transform driverSeatPoint)
        {
            IsDrivingForklift = true;
            ActiveForklift = forklift;
            sortingState = SortingZoneInteractionState.None;
            activeSortingStation = null;

            if (controller != null)
            {
                controller.enabled = false;
            }

            SetPlayerVisualActive(false);

            if (driverSeatPoint != null)
            {
                transform.SetParent(driverSeatPoint, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>
        /// 지게차 하차 시 호출:
        /// 플레이어 트랜스폼을 독립시키고 비주얼 메쉬 및 CharacterController를 복구합니다.
        /// </summary>
        public void ExitForkliftDriveMode(Vector3 dismountWorldPosition)
        {
            transform.SetParent(null, true);
            transform.position = new Vector3(dismountWorldPosition.x, 0f, dismountWorldPosition.z);
            transform.rotation = Quaternion.identity;

            SetPlayerVisualActive(true);
            if (visualModel != null)
            {
                visualModel.localScale = initialScale;
            }

            if (controller != null)
            {
                controller.enabled = true;
            }

            IsDrivingForklift = false;
            ActiveForklift = null;
        }

        private void SetPlayerVisualActive(bool active)
        {
            if (visualModel != null && visualModel != transform)
            {
                visualModel.gameObject.SetActive(active);
            }
            else
            {
                var renderers = GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    if (Stack != null && r.transform.IsChildOf(Stack.transform) && r.transform != transform)
                    {
                        continue;
                    }
                    r.enabled = active;
                }
            }
        }

        public void SetForkliftVisualActive(bool active)
        {
            // 지게차 인간 연출 방지를 위해 플레이어 자식 어태치먼트 방식은 사용하지 않습니다.
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
            // 분류 구역은 물리적으로 분리된 PutZone(북쪽 - 내려놓기 전용)과 TakeZone(남쪽 - 집기 전용) 트리거에서 독립 처리됩니다.
            interactTimer += Time.deltaTime;
            if (interactTimer < 0.15f) return;
            interactTimer = 0f;

            int maxCap = (GameManager.Instance != null) ? GameManager.Instance.GetPlayerMaxStack() : 4;

            // 2. 반품 구역(ReturnStation) 근처: 불량 박스 넣기 또는 검수
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

            // 3. 바닥의 미분류 택배 줍기 (언로딩 패드 근처)
            if (Stack.Count < maxCap)
            {
                var packages = FindObjectsByType<PackageItem>(FindObjectsSortMode.None);
                PackageItem nearest = null;
                float minD = 3.0f;

                foreach (var p in packages)
                {
                    if (p is PalletItem) continue;
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
