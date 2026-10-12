using System;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Player;

namespace APCenter.Facilities
{
    public class SortingStation : InteractionZone
    {
        [Header("Target Destination")]
        public PackageDestination targetDestination;

        [Header("Holding & Output Pad")]
        public Transform outputPad;
        private List<PackageItem> sortedPackages = new List<PackageItem>();

        public int SortedCount => sortedPackages.Count;

        protected override void OnTick(PlayerController p)
        {
            // 상호작용은 PlayerController의 Enum FSM(SortingZoneInteractionState)에서 단일 제어합니다.
        }

        public void SortPackage(PackageItem pkg)
        {
            pkg.State = PackageState.Sorted;
            sortedPackages.Add(pkg);

            // 중앙하단 대신 분류대 뒤쪽(Z + 2.0)에 정돈되어 층층이 적재되도록 배치
            Vector3 padCenter = (outputPad != null) ? outputPad.position : (transform.position + Vector3.forward * 2.0f);
            int idx = sortedPackages.Count - 1;
            int col = idx % 3;
            int layer = idx / 3;
            Vector3 targetPos = padCenter + new Vector3((col - 1) * 0.8f, 0.35f + layer * 0.6f, 0f);

            pkg.FlyTo(null, targetPos, 0.25f, () =>
            {
                pkg.transform.position = targetPos;
                SoundManager.Instance?.PlayDrop();
            });
        }

        public PackageItem PopSortedPackage()
        {
            if (sortedPackages.Count == 0) return null;
            int last = sortedPackages.Count - 1;
            PackageItem item = sortedPackages[last];
            sortedPackages.RemoveAt(last);
            return item;
        }

        public bool HasSortedPackages() => sortedPackages.Count > 0;
    }
}
