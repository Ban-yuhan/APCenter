using System;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.Delivery;
using APCenter.Player;

namespace APCenter.Facilities
{
    public class ReturnStation : InteractionZone
    {
        [Header("Holding & Pad")]
        [SerializeField] private Transform holdingPad;
        [SerializeField] private Transform scrapBin;      // 폐기장 위치
        [SerializeField] private Transform reinboundPad;  // 정상 재입고 위치

        private List<PackageItem> returnQueue = new List<PackageItem>();

        public int QueueCount => returnQueue.Count;

        public event Action<string> OnInspectionResult; // 메시지 이벤트

        protected override void OnTick(PlayerController p)
        {
            if (p == null) return;

            // 1. 플레이어가 불량 택배를 들고 있다면 반품소에 넣음
            PackageItem def = p.Stack.FindDefective();
            if (def != null)
            {
                AddReturnPackage(def);
                return;
            }

            // 2. 플레이어가 반품소에 서서 직접 검수 진행 (플레이어는 숙련도 95% 수준)
            if (returnQueue.Count > 0)
            {
                InspectNextPackage(0.95f, "플레이어");
            }
        }

        public void AddReturnPackage(PackageItem item)
        {
            item.State = PackageState.ReturnPending;
            returnQueue.Add(item);

            Vector3 padCenter = (holdingPad != null) ? holdingPad.position : (transform.position + Vector3.left * 1.5f);
            int idx = returnQueue.Count - 1;
            Vector3 pos = padCenter + new Vector3((idx % 3 - 1) * 0.7f, 0.25f, (idx / 3) * 0.7f);

            item.FlyTo(null, pos, 0.25f, () =>
            {
                item.transform.position = pos;
                SoundManager.Instance?.PlayDrop();
            });
        }

        public void InspectNextPackage(float accuracyRate, string inspectorName)
        {
            if (returnQueue.Count == 0) return;

            PackageItem item = returnQueue[0];
            returnQueue.RemoveAt(0);

            // 주사위를 굴려 검수 판정
            bool judgedAsDefective;
            bool rollSuccess = (UnityEngine.Random.value <= accuracyRate);

            if (rollSuccess)
            {
                // 정확하게 판정
                judgedAsDefective = item.IsDefective;
            }
            else
            {
                // 오판 (정상을 불량으로 오인하거나, 불량을 정상으로 오인)
                judgedAsDefective = !item.IsDefective;
            }

            if (judgedAsDefective)
            {
                // 폐기장으로 이동
                item.State = PackageState.Scrapped;
                Vector3 scrapPos = (scrapBin != null) ? scrapBin.position : (transform.position + Vector3.back * 2f);
                item.FlyTo(null, scrapPos, 0.3f, () =>
                {
                    Destroy(item.gameObject);
                });

                if (!item.IsDefective)
                {
                    // 정상 택배를 오판하여 폐기 -> 손실액 정산!
                    EconomyManager.Instance.SpendMoney(150);
                    OnInspectionResult?.Invoke($"⚠️ [{inspectorName}] 검수 오판! 정상 택배를 폐기하여 손실(150원)이 발생했습니다.");
                }
                else
                {
                    OnInspectionResult?.Invoke($"✅ [{inspectorName}] 파손 택배 폐기 완료!");
                    SoundManager.Instance?.PlaySuccess();
                }
            }
            else
            {
                // 정상 판정 -> 재입고 라인으로 이동
                item.State = PackageState.ReInbounded;
                Vector3 reinboundPos = (reinboundPad != null) ? reinboundPad.position : (transform.position + Vector3.forward * 2f);
                item.FlyTo(null, reinboundPos, 0.3f, () =>
                {
                    item.transform.position = reinboundPos;
                });

                if (item.IsDefective)
                {
                    // 불량 택배인데 정상으로 오판하여 재입고 -> 경고 발생!
                    OnInspectionResult?.Invoke($"🚨 [{inspectorName}] 검수 오판! 불량 택배가 정상으로 오분류 재입고되었습니다! 출고 시 주의!");
                    SoundManager.Instance?.PlayAlert();
                }
                else
                {
                    OnInspectionResult?.Invoke($"✅ [{inspectorName}] 정상 택배 확인 후 재입고 패드로 이송 완료.");
                    SoundManager.Instance?.PlaySuccess();
                }
            }
        }
    }
}
