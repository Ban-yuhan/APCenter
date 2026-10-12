using System;
using UnityEngine;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Player;
using APCenter.Worker;

namespace APCenter.Facilities
{
    public enum ZoneType
    {
        UnloadPickup,       // 입고 하차장 픽업
        SortingRed,         // 서울(빨강) 분류대
        SortingBlue,        // 부산(파랑) 분류대
        SortingGreen,       // 대구(초록) 분류대
        SortedPickup,       // 출고용 분류 완료 택배 픽업
        OutboundLoading,    // 출고 트럭 상차
        ReturnStation,      // 반품 검수대
        HireWorker,         // 알바 고용 존
        UpgradeCenter,      // 업그레이드 존
        WeatherTask         // 기상 악화 미니 태스크
    }

    public class InteractionZone : MonoBehaviour
    {
        public ZoneType zoneType;
        [SerializeField] private float interactInterval = 0.20f; // 상호작용 간격 (0.2초마다 1개씩 쾌속 전송)

        private float timer = 0f;
        protected bool isPlayerInside = false;
        protected PlayerController player;

        public event Action<PlayerController> OnPlayerStayTick;

        private void OnTriggerEnter(Collider other)
        {
            var p = other.GetComponent<PlayerController>() ?? other.GetComponentInParent<PlayerController>();
            if (p != null)
            {
                isPlayerInside = true;
                player = p;
                timer = 0f;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            var p = other.GetComponent<PlayerController>() ?? other.GetComponentInParent<PlayerController>();
            if (p != null && p == player)
            {
                isPlayerInside = false;
                player = null;
            }
        }

        private void Update()
        {
            if (isPlayerInside && player != null)
            {
                timer += Time.deltaTime;
                if (timer >= interactInterval)
                {
                    timer = 0f;
                    OnTick(player);
                    OnPlayerStayTick?.Invoke(player);
                }
            }
        }

        protected virtual void OnTick(PlayerController p)
        {
            if (p == null || p.Stack == null) return;

            // 1. 입고 패드에서 바닥의 택배 집기
            if (zoneType == ZoneType.UnloadPickup)
            {
                int maxCap = (GameManager.Instance != null) ? GameManager.Instance.GetPlayerMaxStack() : 4;
                if (p.Stack.Count < maxCap)
                {
                    var packages = FindObjectsByType<PackageItem>(FindObjectsSortMode.None);
                    PackageItem nearest = null;
                    float minD = float.MaxValue;
                    foreach (var pkg in packages)
                    {
                        if (pkg.State == PackageState.Unloaded && pkg.transform.parent == null)
                        {
                            float d = Vector3.Distance(transform.position, pkg.transform.position);
                            if (d < 8f && d < minD)
                            {
                                minD = d;
                                nearest = pkg;
                            }
                        }
                    }

                    if (nearest != null)
                    {
                        p.Stack.Push(nearest);
                    }
                }
            }
            // 2. 출고 패드에서 트럭으로 택배 상차
            else if (zoneType == ZoneType.OutboundLoading)
            {
                if (p.Stack.Count > 0 && WorkerController.OutTruck != null && WorkerController.OutTruck.CanLoadPackage())
                {
                    var item = p.Stack.Pop();
                    if (item != null)
                    {
                        WorkerController.OutTruck.LoadPackage(item);
                    }
                }
            }
        }
    }
}
