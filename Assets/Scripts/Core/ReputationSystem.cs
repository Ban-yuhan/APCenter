using System;
using UnityEngine;

namespace APCenter.Core
{
    public class ReputationSystem : MonoBehaviour
    {
        public static ReputationSystem Instance { get; private set; }

        [Header("Reputation Settings")]
        [Range(0f, 100f)]
        [SerializeField] private float reputation = 50f; // 시작 50점

        public float CurrentReputation => reputation;

        public event Action<float> OnReputationChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void AddReputation(float delta)
        {
            float old = reputation;
            reputation = Mathf.Clamp(reputation + delta, 0f, 100f);
            if (!Mathf.Approximately(old, reputation))
            {
                OnReputationChanged?.Invoke(reputation);
            }
        }

        public void PenaltyDefectiveShipment()
        {
            AddReputation(-8f); // 불량 택배 발송 시 큰 평판 감점
            SoundManager.Instance?.PlayAlert();
        }

        public void PenaltyWorkerDesertion()
        {
            AddReputation(-5f); // 직원 탈주 시 감점
            SoundManager.Instance?.PlayAlert();
        }

        public void BonusPerfectLoad()
        {
            AddReputation(+6f); // 100% 퍼펙트 적재 보너스
            SoundManager.Instance?.PlaySuccess();
        }

        public void BonusEventSuccess()
        {
            AddReputation(+8f); // 이벤트 완수 보너스
            SoundManager.Instance?.PlaySuccess();
        }

        public void BonusOnTimeDelivery()
        {
            AddReputation(+2f); // 일반 정시 적재 출고 보너스
        }

        public float GetPriceMultiplier()
        {
            // 평판 50% = 1.0배, 100% = 1.5배, 0% = 0.6배
            return 0.6f + (reputation / 100f) * 0.9f;
        }

        public float GetOrderVolumeMultiplier()
        {
            // 평판에 따른 트럭 하차 물량 배율 (0.7배 ~ 1.5배)
            return 0.7f + (reputation / 100f) * 0.8f;
        }

        public int GetStarRating()
        {
            if (reputation >= 85f) return 5;
            if (reputation >= 70f) return 4;
            if (reputation >= 50f) return 3;
            if (reputation >= 30f) return 2;
            return 1;
        }
    }
}
