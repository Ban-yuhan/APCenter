using System;
using UnityEngine;
using APCenter.Core;

namespace APCenter.Worker
{
    [Serializable]
    public class WorkerData
    {
        public int workerId;
        public string workerName;
        public WorkerType workerType;     // 일반 / 단기알바
        public WorkerRole specialtyRole;  // 타고난 적합 분야 (주특기)
        public WorkerRole currentRole;    // 현재 배치된 분야
        public WorkerRank rank;           // 신입, 일반, 숙련, 베테랑

        // 알바 개별 최대 적재 용량 스탯
        public int maxCapacity = 2;

        public float experiencePoints = 0f; // 근무 경험치
        public float workHoursAccumulated = 0f;

        // 탈주 경고 시스템
        public bool isMismatched = false;         // 부적합 배치 여부
        public bool isDesertionWarning = false;   // 탈주 경고 발동 중
        public float desertionGraceTimer = 45f;   // 유예시간 45초

        public WorkerData(int id, string name, WorkerType type, WorkerRole specialty, WorkerRank initialRank = WorkerRank.Rookie)
        {
            workerId = id;
            workerName = name;
            workerType = type;
            specialtyRole = specialty;
            currentRole = specialty; // 기본적으로 특화 분야로 초기 배치
            rank = (type == WorkerType.Temporary) ? WorkerRank.Senior : initialRank;
            SyncMaxCapacityWithRank();
        }

        public void SyncMaxCapacityWithRank()
        {
            if (workerType == WorkerType.Temporary)
            {
                maxCapacity = 8;
                return;
            }

            switch (rank)
            {
                case WorkerRank.Rookie:  maxCapacity = 2; break;
                case WorkerRank.Junior:  maxCapacity = 3; break;
                case WorkerRank.Senior:  maxCapacity = 5; break;
                case WorkerRank.Veteran: maxCapacity = 7; break;
                default:                 maxCapacity = 2; break;
            }
        }

        public float GetSpeed()
        {
            float baseSpeed = 4.5f;
            switch (rank)
            {
                case WorkerRank.Rookie:  baseSpeed = 4.5f; break;
                case WorkerRank.Junior:  baseSpeed = 5.2f; break;
                case WorkerRank.Senior:  baseSpeed = 6.0f; break;
                case WorkerRank.Veteran: baseSpeed = 7.0f; break;
            }

            if (workerType == WorkerType.Temporary) baseSpeed *= 1.4f; // 대타는 초고속
            if (isMismatched) baseSpeed *= 0.7f; // 부적합 배치 시 30% 감속

            return baseSpeed;
        }

        public int GetMaxStack()
        {
            if (maxCapacity <= 0)
            {
                SyncMaxCapacityWithRank();
            }

            int cap = maxCapacity;
            if (isMismatched) cap = Math.Max(1, cap - 1);
            return cap;
        }

        public float GetHourlyWage()
        {
            float wage = 100f;
            switch (rank)
            {
                case WorkerRank.Rookie:  wage = 100f; break;
                case WorkerRank.Junior:  wage = 160f; break;
                case WorkerRank.Senior:  wage = 240f; break;
                case WorkerRank.Veteran: wage = 350f; break;
            }

            if (workerType == WorkerType.Temporary) wage *= 3.0f; // 대타는 3배 임금
            return wage;
        }

        public float GetAccuracyRate()
        {
            switch (rank)
            {
                case WorkerRank.Rookie:  return 0.60f;
                case WorkerRank.Junior:  return 0.80f;
                case WorkerRank.Senior:  return 0.95f;
                case WorkerRank.Veteran: return 1.00f;
                default:                 return 0.80f;
            }
        }

        public void CheckRoleMatch()
        {
            // 단기 알바는 만능이라 불만 없음
            if (workerType == WorkerType.Temporary)
            {
                isMismatched = false;
                isDesertionWarning = false;
                return;
            }

            isMismatched = (specialtyRole != currentRole);
        }
    }
}
