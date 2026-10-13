using System;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;

namespace APCenter.Worker
{
    public class WorkerManager : MonoBehaviour
    {
        public static WorkerManager Instance { get; private set; }

        [Header("Prefabs & Spawning")]
        [SerializeField] private GameObject workerPrefab;
        [SerializeField] private Transform spawnPoint;

        private List<WorkerController> activeWorkers = new List<WorkerController>();
        public IReadOnlyList<WorkerController> ActiveWorkers => activeWorkers;

        private int workerIdCounter = 1;

        public event Action<WorkerController> OnWorkerHired;
        public event Action<WorkerController> OnWorkerRemoved;
        public event Action<string> OnDailyPromotionReport;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            if (TimeSystem.Instance != null)
            {
                TimeSystem.Instance.OnDayCompleted += HandleDayCompleted;
            }

            // 초기 인력 0명 시작: 게임 시작 시 기본 스폰되는 알바 없음 (UI 버튼으로 카드를 뽑았을 때만 1명씩 스폰)
        }

        private void OnDestroy()
        {
            if (TimeSystem.Instance != null)
            {
                TimeSystem.Instance.OnDayCompleted -= HandleDayCompleted;
            }
        }

        private void Update()
        {
            UpdateTotalLaborCost();
        }

        public bool CanHireWorker()
        {
            int maxCap = (GameManager.Instance != null) ? GameManager.Instance.maxWorkersAllowed : 3;
            return activeWorkers.Count < maxCap;
        }

        public WorkerController HireWorker(WorkerRole specialty, WorkerType type, bool isFree = false)
        {
            if (!isFree && !CanHireWorker()) return null;

            long hireCost = (type == WorkerType.Temporary) ? 800 : 350;
            if (!isFree)
            {
                if (!EconomyManager.Instance.SpendMoney(hireCost)) return null;
            }

            string[] names = new string[] { "김민수", "이지은", "박준혁", "최유나", "정성우", "강민경", "윤도현", "송하영" };
            string wName = names[(workerIdCounter - 1) % names.Length];
            if (type == WorkerType.Temporary) wName += "(대타)";

            var data = new WorkerData(workerIdCounter++, wName, type, specialty);

            Vector3 sPos = GetSpawnPositionForRole(specialty);
            GameObject obj;
            if (workerPrefab != null)
            {
                obj = Instantiate(workerPrefab, sPos, Quaternion.identity);
            }
            else
            {
                obj = CreateWorkerGameObject(sPos);
            }

            var ctrl = obj.GetComponent<WorkerController>() ?? obj.AddComponent<WorkerController>();
            ctrl.Initialize(data);
            ctrl.OnDeserted += HandleWorkerDeserted;

            activeWorkers.Add(ctrl);
            OnWorkerHired?.Invoke(ctrl);

            SoundManager.Instance?.PlaySuccess();
            return ctrl;
        }

        /// <summary>
        /// 알바 3택 1 채용 카드(RecruitCardUI)에서 선택한 후보자의 속성(이름, 등급, 적합 구역, 비용)으로
        /// 해당 구역에 알바를 1명 스폰합니다.
        /// </summary>
        public WorkerController HireWorkerWithCandidate(string candidateName, WorkerRank candidateRank, WorkerRole specialtyRole, long hireCost)
        {
            if (!CanHireWorker()) return null;
            if (EconomyManager.Instance != null && !EconomyManager.Instance.SpendMoney(hireCost)) return null;

            var data = new WorkerData(workerIdCounter++, candidateName, WorkerType.Normal, specialtyRole, candidateRank)
            {
                rank = candidateRank,
                specialtyRole = specialtyRole,
                currentRole = specialtyRole
            };
            data.SyncMaxCapacityWithRank();
            data.CheckRoleMatch();

            Vector3 sPos = GetSpawnPositionForRole(specialtyRole);
            sPos += new Vector3(UnityEngine.Random.Range(-1.0f, 1.0f), 0f, UnityEngine.Random.Range(-0.8f, 0.8f));

            GameObject obj;
            if (workerPrefab != null)
            {
                obj = Instantiate(workerPrefab, sPos, Quaternion.identity);
            }
            else
            {
                obj = CreateWorkerGameObject(sPos);
            }

            var ctrl = obj.GetComponent<WorkerController>() ?? obj.AddComponent<WorkerController>();
            ctrl.Initialize(data);
            ctrl.OnDeserted += HandleWorkerDeserted;

            activeWorkers.Add(ctrl);
            OnWorkerHired?.Invoke(ctrl);

            SoundManager.Instance?.PlaySuccess();
            return ctrl;
        }

        private Vector3 GetSpawnPositionForRole(WorkerRole role)
        {
            switch (role)
            {
                case WorkerRole.Unload:
                case WorkerRole.Transport:
                case WorkerRole.Sort:
                    // 입고장 ~ 분류대 사이 구역 스폰
                    return new Vector3(-4f, 0f, 7.5f);
                case WorkerRole.Load:
                    // 분류대 ~ 출고 도크 사이 구역 스폰
                    return new Vector3(0f, 0f, -4.5f);
                case WorkerRole.Inspection:
                    // 반품/검수 구역 앞 스폰
                    return new Vector3(-9.5f, 0f, -4.0f);
                default:
                    return (spawnPoint != null) ? spawnPoint.position : new Vector3(0f, 0f, -2f);
            }
        }

        private GameObject CreateWorkerGameObject(Vector3 pos)
        {
            var root = new GameObject("Worker");
            root.transform.position = pos;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Visual";
            body.transform.SetParent(root.transform);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(0.7f, 1f, 0.7f);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            mat.color = new Color(0.95f, 0.65f, 0.2f);
            body.GetComponent<Renderer>().material = mat;

            var nameObj = new GameObject("NameTag");
            nameObj.transform.SetParent(root.transform);
            nameObj.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            var tm = nameObj.AddComponent<TextMesh>();
            tm.fontSize = 45;
            tm.characterSize = 0.08f;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.black;
            Billboard.Attach(nameObj);

            return root;
        }

        public void ReassignRole(WorkerController worker, WorkerRole newRole)
        {
            if (worker == null || worker.Data == null) return;

            worker.Data.currentRole = newRole;
            worker.Data.CheckRoleMatch();
            worker.UpdateVisualAppearance();

            SoundManager.Instance?.PlayClick();
        }

        private void HandleWorkerDeserted(WorkerController worker)
        {
            activeWorkers.Remove(worker);
            OnWorkerRemoved?.Invoke(worker);
        }

        private void UpdateTotalLaborCost()
        {
            float totalHourly = 0f;
            foreach (var w in activeWorkers)
            {
                if (w != null && w.Data != null)
                {
                    totalHourly += w.Data.GetHourlyWage();
                }
            }

            float perMin = totalHourly / 60f;
            EconomyManager.Instance?.SetLaborCost(perMin);
        }

        private void HandleDayCompleted(int newDay)
        {
            List<string> promotions = new List<string>();

            foreach (var w in activeWorkers)
            {
                if (w == null || w.Data == null) continue;

                w.Data.experiencePoints += (w.Data.workHoursAccumulated * 10f);
                w.Data.workHoursAccumulated = 0f;

                if (w.Data.workerType == WorkerType.Normal)
                {
                    if (w.Data.rank == WorkerRank.Rookie && w.Data.experiencePoints >= 60f)
                    {
                        w.Data.rank = WorkerRank.Junior;
                        w.Data.SyncMaxCapacityWithRank();
                        promotions.Add($"🎉 [{w.Data.workerName}] 신입 ➔ 일반 승급! (속도/용량 UP, 시급 160원)");
                    }
                    else if (w.Data.rank == WorkerRank.Junior && w.Data.experiencePoints >= 160f)
                    {
                        w.Data.rank = WorkerRank.Senior;
                        w.Data.SyncMaxCapacityWithRank();
                        promotions.Add($"⭐ [{w.Data.workerName}] 일반 ➔ 숙련 승급! (스택/속도 대폭 UP, 시급 240원)");
                    }
                    else if (w.Data.rank == WorkerRank.Senior && w.Data.experiencePoints >= 300f)
                    {
                        w.Data.rank = WorkerRank.Veteran;
                        w.Data.SyncMaxCapacityWithRank();
                        promotions.Add($"👑 [{w.Data.workerName}] 숙련 ➔ 베테랑 승급! (검수 100% 무결점, 시급 350원)");
                    }
                }

                w.UpdateVisualAppearance();
            }

            if (promotions.Count > 0)
            {
                string report = string.Join("\n", promotions);
                OnDailyPromotionReport?.Invoke(report);
                SoundManager.Instance?.PlaySuccess();
            }
        }
    }
}
