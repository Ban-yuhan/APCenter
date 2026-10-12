using System;
using UnityEngine;

namespace APCenter.Core
{
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager Instance { get; private set; }

        [Header("Starting Funds")]
        [SerializeField] private long currentMoney = 1500; // 초기 자금

        [Header("Per Minute Stats")]
        public float revenuePerMinute = 0f;
        public float laborCostPerMinute = 0f;
        public float upkeepCostPerMinute = 50f; // 기본 시설 유지비
        public float netProfitPerMinute = 0f;

        [Header("Tracking Rolling Window (60 seconds)")]
        private float[] revenueBuckets = new float[60];
        private int currentBucketIndex = 0;
        private float bucketTimer = 0f;

        // 야간 할증 배율 (22:00 ~ 06:00 야간 수당 및 출고 단가)
        public float currentWageMultiplier = 1.0f;
        public float currentOutboundMultiplier = 1.0f;

        public event Action<long> OnMoneyChanged;
        public event Action<float, float, float, float> OnProfitStatsUpdated; // rev, labor, upkeep, net

        public long CurrentMoney => currentMoney;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            bucketTimer += Time.deltaTime;
            if (bucketTimer >= 1.0f)
            {
                bucketTimer -= 1.0f;
                currentBucketIndex = (currentBucketIndex + 1) % 60;
                revenueBuckets[currentBucketIndex] = 0f; // 리셋

                // 분당 매출 합산 계산
                float sum = 0f;
                for (int i = 0; i < 60; i++) sum += revenueBuckets[i];
                revenuePerMinute = sum;

                // 순이익 = 분당 매출 - (인건비 + 유지비)
                netProfitPerMinute = revenuePerMinute - (laborCostPerMinute + upkeepCostPerMinute);
                OnProfitStatsUpdated?.Invoke(revenuePerMinute, laborCostPerMinute, upkeepCostPerMinute, netProfitPerMinute);

                // 1초 단위로 분당 인건비 및 유지비 차감 (60분의 1씩 실시간 반영)
                float secCost = (laborCostPerMinute + upkeepCostPerMinute) / 60f;
                if (secCost > 0f)
                {
                    currentMoney = Math.Max(0, currentMoney - (long)Math.Ceiling(secCost));
                    OnMoneyChanged?.Invoke(currentMoney);
                }
            }
        }

        public void AddMoney(long amount, bool countAsRevenue = true)
        {
            if (amount <= 0) return;
            currentMoney += amount;
            if (countAsRevenue)
            {
                revenueBuckets[currentBucketIndex] += amount;
            }
            OnMoneyChanged?.Invoke(currentMoney);
            SoundManager.Instance?.PlayCoin();
        }

        public bool SpendMoney(long amount)
        {
            if (amount <= 0) return true;
            if (currentMoney >= amount)
            {
                currentMoney -= amount;
                OnMoneyChanged?.Invoke(currentMoney);
                return true;
            }
            return false;
        }

        public void SetLaborCost(float perMinute)
        {
            laborCostPerMinute = perMinute * currentWageMultiplier;
        }

        public void SetUpkeepCost(float perMinute)
        {
            upkeepCostPerMinute = perMinute;
        }

        public void UpdateNightShift(bool isNight)
        {
            if (isNight)
            {
                currentWageMultiplier = 1.5f;     // 야간 수당 +50%
                currentOutboundMultiplier = 1.5f; // 야간 배송 단가 +50%
            }
            else
            {
                currentWageMultiplier = 1.0f;
                currentOutboundMultiplier = 1.0f;
            }
        }

        public long CalculateOutboundSettlement(int packageCount, int basePricePerPackage, float bonusRate, int fuelCost)
        {
            // 정산 = (택배 수 * 단가 * 배율) - 기름값
            float total = (packageCount * basePricePerPackage * bonusRate * currentOutboundMultiplier);
            long netSettlement = (long)Math.Max(0, Math.Round(total) - fuelCost);
            return netSettlement;
        }
    }
}
