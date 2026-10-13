using System;
using UnityEngine;

namespace APCenter.Core
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Facility Tier")]
        public FacilityTier CurrentTier = FacilityTier.SmallHub;

        [Header("Player Upgrades")]
        public int playerMoveSpeedLevel = 1;     // 속도 레벨 (1~5)
        public int playerStackCapacityLevel = 1; // 스택 용량 레벨 (기본 4개, 레벨당 +2)
        public bool hasCartUnlocked = true;      // 손수레 (Tier 1)
        public bool hasElectricCart = false;     // 전동 카트 (Tier 2)
        public bool hasForklift = false;         // 지게차 (Tier 3)
        public bool hasPalletSystem = false;     // 파렛트 압축 적재 (6개 묶음)
        public bool hasAutoSorter = false;       // 자동 분류기 (Tier 3)

        [Header("Worker Upgrades")]
        public int maxWorkersAllowed = 3;        // 고용 가능 인원 (Tier 1: 3, Tier 2: 6, Tier 3: 12)

        public event Action<FacilityTier> OnFacilityTierUpgraded;
        public event Action OnUpgradeChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Application.targetFrameRate = 60;
        }

        private void Start()
        {
            UpdateFacilityTierSpecs();
        }

        public float GetPlayerMoveSpeed()
        {
            float baseSpeed = 7.0f + (playerMoveSpeedLevel - 1) * 1.2f;
            if (hasElectricCart) return baseSpeed * 1.3f;
            if (hasCartUnlocked) return baseSpeed * 1.15f;
            return baseSpeed;
        }

        public int GetPlayerMaxStack()
        {
            // 플레이어 기본 최대 적재량 4개, 스택 확장 구매 시 1레벨당 +1개씩 순차 확장 (4 -> 5 -> 6...)
            return 4 + (playerStackCapacityLevel - 1);
        }

        public bool UpgradePlayerSpeed()
        {
            long cost = GetUpgradeSpeedCost();
            if (EconomyManager.Instance.SpendMoney(cost))
            {
                playerMoveSpeedLevel++;
                SoundManager.Instance?.PlaySuccess();
                OnUpgradeChanged?.Invoke();
                return true;
            }
            return false;
        }

        public bool UpgradePlayerCapacity()
        {
            long cost = GetUpgradeCapacityCost();
            if (EconomyManager.Instance.SpendMoney(cost))
            {
                playerStackCapacityLevel++;
                SoundManager.Instance?.PlaySuccess();
                OnUpgradeChanged?.Invoke();
                return true;
            }
            return false;
        }

        public bool UnlockForkliftSystem()
        {
            long cost = GetPalletCost();
            if (!hasForklift && EconomyManager.Instance.SpendMoney(cost))
            {
                hasForklift = true;
                hasPalletSystem = true;
                SoundManager.Instance?.PlaySuccess();
                OnUpgradeChanged?.Invoke();
                return true;
            }
            return false;
        }

        public bool UnlockPalletSystem()
        {
            return UnlockForkliftSystem();
        }

        public void ActivateForkliftForPallet()
        {
            hasForklift = true;
            hasPalletSystem = true;
            SoundManager.Instance?.PlaySuccess();
            OnUpgradeChanged?.Invoke();
        }

        public bool UpgradeFacilityTier()
        {
            if (CurrentTier == FacilityTier.SmallHub)
            {
                long cost = GetTierUpgradeCost();
                if (EconomyManager.Instance.SpendMoney(cost))
                {
                    CurrentTier = FacilityTier.MediumCenter;
                    hasElectricCart = true;
                    maxWorkersAllowed = 6;
                    UpdateFacilityTierSpecs();
                    SoundManager.Instance?.PlaySuccess();
                    OnFacilityTierUpgraded?.Invoke(CurrentTier);
                    OnUpgradeChanged?.Invoke();
                    return true;
                }
            }
            else if (CurrentTier == FacilityTier.MediumCenter)
            {
                long cost = GetTierUpgradeCost();
                if (EconomyManager.Instance.SpendMoney(cost))
                {
                    CurrentTier = FacilityTier.MegaHub;
                    hasAutoSorter = true;
                    maxWorkersAllowed = 12;
                    UpdateFacilityTierSpecs();
                    SoundManager.Instance?.PlaySuccess();
                    OnFacilityTierUpgraded?.Invoke(CurrentTier);
                    OnUpgradeChanged?.Invoke();
                    return true;
                }
            }
            return false;
        }

        private void UpdateFacilityTierSpecs()
        {
            if (CurrentTier == FacilityTier.SmallHub)
            {
                EconomyManager.Instance?.SetUpkeepCost(40f);
            }
            else if (CurrentTier == FacilityTier.MediumCenter)
            {
                EconomyManager.Instance?.SetUpkeepCost(120f);
            }
            else if (CurrentTier == FacilityTier.MegaHub)
            {
                EconomyManager.Instance?.SetUpkeepCost(300f);
            }
        }

        public long GetUpgradeSpeedCost()
        {
            long cost = (long)(300 * Math.Pow(1.8, playerMoveSpeedLevel - 1));
            return ApplyDiscount(cost);
        }

        public long GetUpgradeCapacityCost()
        {
            long cost = (long)(400 * Math.Pow(2.0, playerStackCapacityLevel - 1));
            return ApplyDiscount(cost);
        }

        public long GetPalletCost()
        {
            return ApplyDiscount(1200);
        }

        public long GetTierUpgradeCost()
        {
            if (CurrentTier == FacilityTier.SmallHub) return ApplyDiscount(2500);
            if (CurrentTier == FacilityTier.MediumCenter) return ApplyDiscount(7000);
            return 999999;
        }

        private long ApplyDiscount(long cost)
        {
            if (EventSystem.Instance != null && EventSystem.Instance.IsUpgradeDiscountActive)
            {
                return (long)(cost * 0.8f); // 20% 할인
            }
            return cost;
        }
    }
}
