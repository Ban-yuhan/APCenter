using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using APCenter.Core;
using APCenter.Worker;

namespace APCenter.UI
{
    /// <summary>
    /// 알바 3택 1 고용 카드 UI 시스템 (RecruitCardUI.cs):
    /// - 3D 월드 바닥의 알바 고용 발판을 제거하고, 화면 HUD의 기존 [알바 관리/고용] 버튼 클릭 시 작동합니다.
    /// - 버튼 클릭 시 화면 중앙에 알바 채용 카드 3장 팝업 (Time.timeScale = 0 일시정지)
    /// - 각 카드에는 알바 이름, 등급, 시급, 개별 용량(maxCapacity) 및 추천 적합 구역 뱃지(운반, 적재, 검수 중 1개)가 랜덤 표시됨
    /// - 카드 1장을 클릭하면 비용 차감 후 해당 구역에 알바가 1명 스폰되고 창이 닫히며 게임이 재개됩니다.
    /// </summary>
    public class RecruitCardUI : MonoBehaviour
    {
        public static RecruitCardUI Instance { get; private set; }

        [Serializable]
        public struct CandidateData
        {
            public string workerName;
            public WorkerRank rank;
            public WorkerRole recommendedRole; // 운반(Transport), 적재(Load), 검수(Inspection) 중 1개
            public int hourlyWage;
            public long hireCost;
        }

        private class CardViewWidgets
        {
            public Button cardButton;
            public Image cardBg;
            public Image rankHeaderBg;
            public Text rankTitleText;
            public Text nameText;
            public Image badgeBg;
            public Text badgeText;
            public Text statsText;
            public Text costButtonText;
        }

        private GameObject modalRoot;
        private Text headerStatusText;
        private readonly List<CardViewWidgets> cardWidgets = new List<CardViewWidgets>();
        private readonly CandidateData[] currentCandidates = new CandidateData[3];

        public bool IsOpen => modalRoot != null && modalRoot.activeSelf;

        private static readonly string[] CandidateNamePool = new string[]
        {
            "김민수", "이지은", "박준혁", "최유나", "정성우", "강민경",
            "윤도현", "송하영", "한지민", "서강준", "백승호", "오하늘"
        };

        // 추천 적합 구역 뱃지 후보: 운반(Transport), 적재(Load), 검수(Inspection) 중 1개
        private static readonly WorkerRole[] AllowedBadgeRoles = new WorkerRole[]
        {
            WorkerRole.Transport,
            WorkerRole.Load,
            WorkerRole.Inspection
        };

        public static void EnsureRecruitSystemExists()
        {
            // 혹시 씬에 이전 3D 고용 발판 오브젝트가 남아있다면 즉시 삭제
            var oldPad = GameObject.Find("HireWorkerPad_Zone");
            if (oldPad != null)
            {
                Destroy(oldPad);
            }

            if (Instance == null)
            {
                var existing = FindAnyObjectByType<RecruitCardUI>();
                if (existing != null)
                {
                    Instance = existing;
                }
                else
                {
                    Canvas canvas = FindAnyObjectByType<Canvas>();
                    if (canvas != null)
                    {
                        Instance = canvas.gameObject.AddComponent<RecruitCardUI>();
                        Instance.BuildModalUI(canvas.transform);
                    }
                }
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            var oldPad = GameObject.Find("HireWorkerPad_Zone");
            if (oldPad != null)
            {
                Destroy(oldPad);
            }

            Canvas canvas = GetComponentInParent<Canvas>() ?? FindAnyObjectByType<Canvas>();
            if (canvas != null)
            {
                BuildModalUI(canvas.transform);
            }
        }

        private void OnDestroy()
        {
            if (Time.timeScale == 0f)
            {
                Time.timeScale = 1f;
            }
        }

        public void BuildModalUI(Transform canvasTransform)
        {
            if (modalRoot != null) return;

            Transform existing = canvasTransform.Find("RecruitCardModalPanel");
            if (existing != null)
            {
                modalRoot = existing.gameObject;
                return;
            }

            Font defaultFont = null;
            try { defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }

            modalRoot = new GameObject("RecruitCardModalPanel");
            modalRoot.transform.SetParent(canvasTransform, false);
            RectTransform rootRect = modalRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image dimImg = modalRoot.AddComponent<Image>();
            dimImg.color = new Color(0.04f, 0.06f, 0.10f, 0.82f);

            GameObject frameObj = new GameObject("ModalFrame");
            frameObj.transform.SetParent(modalRoot.transform, false);
            RectTransform frameRect = frameObj.AddComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.5f, 0.5f);
            frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            frameRect.anchoredPosition = Vector2.zero;
            frameRect.sizeDelta = new Vector2(980f, 580f);

            Image frameImg = frameObj.AddComponent<Image>();
            frameImg.color = new Color(0.13f, 0.16f, 0.22f, 0.98f);

            Text titleTxt = CreateText("TitleText", frameObj.transform,
                "👥 알바 채용 (3택 1 카드 선택)", 30, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.91f), new Vector2(860f, 48f), Color.white, defaultFont);
            titleTxt.fontStyle = FontStyle.Bold;

            headerStatusText = CreateText("SubTitleText", frameObj.transform,
                "카드를 1장 클릭하면 해당 구역에 알바가 1명 즉시 스폰되며 게임이 재개됩니다.", 18, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.83f), new Vector2(860f, 34f), new Color(0.75f, 0.85f, 1.0f), defaultFont);

            cardWidgets.Clear();
            float[] cardXs = { -305f, 0f, 305f };

            for (int i = 0; i < 3; i++)
            {
                int capturedIdx = i;
                CardViewWidgets cw = CreateSingleCardUI(frameObj.transform, capturedIdx, new Vector2(cardXs[i], -15f), defaultFont);
                cardWidgets.Add(cw);
            }

            // 하단 닫기 버튼
            GameObject closeBtnObj = new GameObject("CancelCloseButton");
            closeBtnObj.transform.SetParent(frameObj.transform, false);
            RectTransform closeRect = closeBtnObj.AddComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(0.5f, 0.07f);
            closeRect.anchorMax = new Vector2(0.5f, 0.07f);
            closeRect.pivot = new Vector2(0.5f, 0.5f);
            closeRect.anchoredPosition = Vector2.zero;
            closeRect.sizeDelta = new Vector2(240f, 46f);

            Image closeBg = closeBtnObj.AddComponent<Image>();
            closeBg.color = new Color(0.42f, 0.45f, 0.52f, 1f);
            Button closeBtn = closeBtnObj.AddComponent<Button>();
            closeBtn.onClick.AddListener(CloseRecruitCards);

            CreateText("Label", closeBtnObj.transform, "닫기 (게임 재개)", 19, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(220f, 40f), Color.white, defaultFont);

            modalRoot.SetActive(false);
        }

        private CardViewWidgets CreateSingleCardUI(Transform parent, int index, Vector2 anchoredPos, Font font)
        {
            CardViewWidgets w = new CardViewWidgets();

            GameObject cardObj = new GameObject($"RecruitCard_{index}");
            cardObj.transform.SetParent(parent, false);
            RectTransform cardRect = cardObj.AddComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = anchoredPos;
            cardRect.sizeDelta = new Vector2(280f, 370f);

            w.cardBg = cardObj.AddComponent<Image>();
            w.cardBg.color = new Color(0.20f, 0.24f, 0.32f, 1f);

            w.cardButton = cardObj.AddComponent<Button>();
            ColorBlock cb = w.cardButton.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            cb.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            w.cardButton.colors = cb;
            w.cardButton.onClick.AddListener(() => OnSelectCandidateCard(index));

            GameObject headerObj = new GameObject("RankHeader");
            headerObj.transform.SetParent(cardObj.transform, false);
            RectTransform headerRect = headerObj.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, 52f);
            w.rankHeaderBg = headerObj.AddComponent<Image>();
            w.rankHeaderBg.color = new Color(0.3f, 0.5f, 0.85f, 1f);

            w.rankTitleText = CreateText("RankText", headerObj.transform, "★ 일반 등급 ★", 20, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(260f, 44f), Color.white, font);
            w.rankTitleText.fontStyle = FontStyle.Bold;

            w.nameText = CreateText("WorkerNameText", cardObj.transform, "김민수", 26, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.74f), new Vector2(250f, 44f), Color.white, font);
            w.nameText.fontStyle = FontStyle.Bold;

            GameObject badgeObj = new GameObject("RecommendedRoleBadge");
            badgeObj.transform.SetParent(cardObj.transform, false);
            RectTransform badgeRect = badgeObj.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0.5f, 0.56f);
            badgeRect.anchorMax = new Vector2(0.5f, 0.56f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            badgeRect.anchoredPosition = Vector2.zero;
            badgeRect.sizeDelta = new Vector2(236f, 48f);

            w.badgeBg = badgeObj.AddComponent<Image>();
            w.badgeBg.color = new Color(0.18f, 0.65f, 0.45f, 1f);

            w.badgeText = CreateText("BadgeLabel", badgeObj.transform, "🚚 추천 구역: 운반", 19, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(226f, 42f), Color.white, font);
            w.badgeText.fontStyle = FontStyle.Bold;

            w.statsText = CreateText("StatsText", cardObj.transform, "시급: ₩160 / 시간\n이동속도: 5.2 | 최대용량: 3개", 17, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.34f), new Vector2(250f, 80f), new Color(0.90f, 0.92f, 0.96f), font);

            GameObject actionBar = new GameObject("HireActionBar");
            actionBar.transform.SetParent(cardObj.transform, false);
            RectTransform actionRect = actionBar.AddComponent<RectTransform>();
            actionRect.anchorMin = new Vector2(0.5f, 0.11f);
            actionRect.anchorMax = new Vector2(0.5f, 0.11f);
            actionRect.pivot = new Vector2(0.5f, 0.5f);
            actionRect.anchoredPosition = Vector2.zero;
            actionRect.sizeDelta = new Vector2(240f, 48f);

            Image actionImg = actionBar.AddComponent<Image>();
            actionImg.color = new Color(0.95f, 0.62f, 0.15f, 1f);

            w.costButtonText = CreateText("CostText", actionBar.transform, "고용하기 (₩350)", 20, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(230f, 42f), Color.white, font);
            w.costButtonText.fontStyle = FontStyle.Bold;

            return w;
        }

        public void OpenRecruitCards()
        {
            if (modalRoot == null)
            {
                Canvas canvas = FindAnyObjectByType<Canvas>();
                if (canvas != null) BuildModalUI(canvas.transform);
            }
            if (modalRoot == null) return;

            UIManager.Instance?.CloseAllPanels();

            GenerateRandomThreeCandidates();
            RefreshCardVisuals();

            modalRoot.SetActive(true);
            modalRoot.transform.SetAsLastSibling();

            Time.timeScale = 0f;
            SoundManager.Instance?.PlayClick();
        }

        public void CloseRecruitCards()
        {
            if (modalRoot != null)
            {
                modalRoot.SetActive(false);
            }

            Time.timeScale = 1f;
        }

        private void GenerateRandomThreeCandidates()
        {
            List<string> pool = new List<string>(CandidateNamePool);
            for (int i = 0; i < 3; i++)
            {
                int nameIdx = UnityEngine.Random.Range(0, pool.Count);
                string chosenName = pool[nameIdx];
                pool.RemoveAt(nameIdx);

                float roll = UnityEngine.Random.value;
                WorkerRank rank = (roll < 0.35f) ? WorkerRank.Rookie
                                : (roll < 0.70f) ? WorkerRank.Junior
                                : (roll < 0.90f) ? WorkerRank.Senior
                                : WorkerRank.Veteran;

                WorkerRole recRole = AllowedBadgeRoles[UnityEngine.Random.Range(0, AllowedBadgeRoles.Length)];

                int wage = GetWageForRank(rank);
                long cost = GetHireCostForRank(rank);

                currentCandidates[i] = new CandidateData
                {
                    workerName = chosenName,
                    rank = rank,
                    recommendedRole = recRole,
                    hourlyWage = wage,
                    hireCost = cost
                };
            }
        }

        private void RefreshCardVisuals()
        {
            int curWorkers = (WorkerManager.Instance != null) ? WorkerManager.Instance.ActiveWorkers.Count : 0;
            int maxWorkers = (GameManager.Instance != null) ? GameManager.Instance.maxWorkersAllowed : 3;
            long curMoney = (EconomyManager.Instance != null) ? EconomyManager.Instance.CurrentMoney : 0;

            if (headerStatusText != null)
            {
                headerStatusText.text = $"현재 인원: {curWorkers}/{maxWorkers}명  |  보유 자금: ₩{curMoney:N0}  (카드 클릭 시 해당 구역에 즉시 스폰)";
            }

            for (int i = 0; i < cardWidgets.Count && i < 3; i++)
            {
                var w = cardWidgets[i];
                var c = currentCandidates[i];

                w.rankTitleText.text = GetRankTitleString(c.rank);
                w.rankHeaderBg.color = GetRankHeaderColor(c.rank);
                w.nameText.text = c.workerName;

                w.badgeText.text = GetRoleBadgeString(c.recommendedRole);
                w.badgeBg.color = GetRoleBadgeColor(c.recommendedRole);

                float spd = (c.rank == WorkerRank.Rookie) ? 4.5f
                          : (c.rank == WorkerRank.Junior) ? 5.2f
                          : (c.rank == WorkerRank.Senior) ? 6.0f : 7.0f;
                int cap = (c.rank == WorkerRank.Rookie) ? 2
                        : (c.rank == WorkerRank.Junior) ? 3
                        : (c.rank == WorkerRank.Senior) ? 5 : 7;

                w.statsText.text = $"💰 시급: <color=#ffeaa7>₩{c.hourlyWage}/h</color>\n⚡ 이동 속도: {spd:F1}  |  📦 최대 용량: {cap}개";
                w.costButtonText.text = $"채용하기 (₩{c.hireCost:N0})";
            }
        }

        private void OnSelectCandidateCard(int index)
        {
            if (index < 0 || index >= 3) return;
            if (WorkerManager.Instance == null) return;

            var c = currentCandidates[index];

            if (!WorkerManager.Instance.CanHireWorker())
            {
                CloseRecruitCards();
                UIManager.Instance?.ShowNotice("⚠️ 최대 고용 인원 한도에 도달했습니다! (시설 등급 업그레이드 필요)");
                return;
            }

            var hired = WorkerManager.Instance.HireWorkerWithCandidate(c.workerName, c.rank, c.recommendedRole, c.hireCost);
            if (hired != null)
            {
                string roleName = GetRoleShortKor(c.recommendedRole);
                string rankName = GetRankShortKor(c.rank);
                CloseRecruitCards();
                UIManager.Instance?.ShowNotice($"🎉 [{c.workerName} ({rankName} / {roleName} 구역)] 채용 완료! 해당 구역에 스폰되었습니다.");
            }
            else
            {
                CloseRecruitCards();
                UIManager.Instance?.ShowNotice("❌ 채용 비용(자금)이 부족합니다!");
            }
        }

        private static int GetWageForRank(WorkerRank rank)
        {
            switch (rank)
            {
                case WorkerRank.Rookie: return 100;
                case WorkerRank.Junior: return 160;
                case WorkerRank.Senior: return 240;
                case WorkerRank.Veteran: return 350;
                default: return 100;
            }
        }

        private static long GetHireCostForRank(WorkerRank rank)
        {
            switch (rank)
            {
                case WorkerRank.Rookie: return 300;
                case WorkerRank.Junior: return 420;
                case WorkerRank.Senior: return 580;
                case WorkerRank.Veteran: return 800;
                default: return 350;
            }
        }

        private static string GetRankShortKor(WorkerRank rank)
        {
            switch (rank)
            {
                case WorkerRank.Rookie: return "신입";
                case WorkerRank.Junior: return "일반";
                case WorkerRank.Senior: return "숙련";
                case WorkerRank.Veteran: return "베테랑";
                default: return "신입";
            }
        }

        private static string GetRankTitleString(WorkerRank rank)
        {
            switch (rank)
            {
                case WorkerRank.Rookie:  return "🌱 등급: 신입 (Rookie)";
                case WorkerRank.Junior:  return "⭐ 등급: 일반 (Junior)";
                case WorkerRank.Senior:  return "🌟 등급: 숙련 (Senior)";
                case WorkerRank.Veteran: return "👑 등급: 베테랑 (Veteran)";
                default:                 return "🌱 등급: 신입";
            }
        }

        private static Color GetRankHeaderColor(WorkerRank rank)
        {
            switch (rank)
            {
                case WorkerRank.Rookie:  return new Color(0.32f, 0.55f, 0.38f, 1f);
                case WorkerRank.Junior:  return new Color(0.24f, 0.52f, 0.86f, 1f);
                case WorkerRank.Senior:  return new Color(0.62f, 0.32f, 0.88f, 1f);
                case WorkerRank.Veteran: return new Color(0.88f, 0.52f, 0.14f, 1f);
                default:                 return new Color(0.35f, 0.45f, 0.6f, 1f);
            }
        }

        private static string GetRoleShortKor(WorkerRole role)
        {
            switch (role)
            {
                case WorkerRole.Transport:  return "운반";
                case WorkerRole.Load:       return "적재";
                case WorkerRole.Inspection: return "검수";
                default:                    return "운반";
            }
        }

        private static string GetRoleBadgeString(WorkerRole role)
        {
            switch (role)
            {
                case WorkerRole.Transport:  return "🚚 추천 적합 구역: [운반]";
                case WorkerRole.Load:       return "📦 추천 적합 구역: [적재]";
                case WorkerRole.Inspection: return "🔍 추천 적합 구역: [검수]";
                default:                    return "🚚 추천 적합 구역: [운반]";
            }
        }

        private static Color GetRoleBadgeColor(WorkerRole role)
        {
            switch (role)
            {
                case WorkerRole.Transport:  return new Color(0.16f, 0.62f, 0.78f, 1f);
                case WorkerRole.Load:       return new Color(0.85f, 0.38f, 0.22f, 1f);
                case WorkerRole.Inspection: return new Color(0.55f, 0.32f, 0.78f, 1f);
                default:                    return new Color(0.2f, 0.6f, 0.5f, 1f);
            }
        }

        private static Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor anchor, Vector2 normPos, Vector2 size, Color color, Font font)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = normPos;
            rt.anchorMax = normPos;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;

            Text t = go.AddComponent<Text>();
            if (font != null) t.font = font;
            t.text = content;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = color;
            return t;
        }
    }
}
