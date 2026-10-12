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

            // 시작 기본 알바 1명 무료 고용 (상하차 특기)
            HireWorker(WorkerRole.Unload, WorkerType.Normal, true);
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

            Vector3 sPos = (spawnPoint != null) ? spawnPoint.position : new Vector3(0f, 0f, 0f);
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

        private GameObject CreateWorkerGameObject(Vector3 pos)
        {
            var root = new GameObject("Worker");
            root.transform.position = pos;

            // 몸통
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Visual";
            body.transform.SetParent(root.transform);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(0.7f, 1f, 0.7f);

            var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse"));
            mat.color = new Color(0.95f, 0.65f, 0.2f); // 주황색 작업 조끼 색상
            body.GetComponent<Renderer>().material = mat;

            // 이름표 3D 텍스트
            var nameObj = new GameObject("NameTag");
            nameObj.transform.SetParent(root.transform);
            nameObj.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            var tm = nameObj.AddComponent<TextMesh>();
            tm.fontSize = 24;
            tm.characterSize = 0.08f;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.white;

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

            // 분당 인건비 = 시간당 인건비 / 60
            float perMin = totalHourly / 60f;
            EconomyManager.Instance?.SetLaborCost(perMin);
        }

        private void HandleDayCompleted(int newDay)
        {
            // 하루 단위 숙련도 정산
            List<string> promotions = new List<string>();

            foreach (var w in activeWorkers)
            {
                if (w == null || w.Data == null) continue;

                // 하루 동안 일한 시간으로 경험치 지급
                w.Data.experiencePoints += (w.Data.workHoursAccumulated * 10f);
                w.Data.workHoursAccumulated = 0f;

                // 승급 판정 (단기 알바 제외)
                if (w.Data.workerType == WorkerType.Normal)
                {
                    if (w.Data.rank == WorkerRank.Rookie && w.Data.experiencePoints >= 60f)
                    {
                        w.Data.rank = WorkerRank.Junior;
                        promotions.Add($"🎉 [{w.Data.workerName}] 신입 ➔ 일반 승급! (속도/정확도 UP, 시급 160원)");
                    }
                    else if (w.Data.rank == WorkerRank.Junior && w.Data.experiencePoints >= 160f)
                    {
                        w.Data.rank = WorkerRank.Senior;
                        promotions.Add($"⭐ [{w.Data.workerName}] 일반 ➔ 숙련 승급! (스택/속도 대폭 UP, 시급 240원)");
                    }
                    else if (w.Data.rank == WorkerRank.Senior && w.Data.experiencePoints >= 300f)
                    {
                        w.Data.rank = WorkerRank.Veteran;
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
