using System;
using UnityEngine;
using APCenter.Player;

namespace APCenter.Facilities
{
    /// <summary>
    /// 기존 씬(MainGame.unity)의 PalletStation 참조 호환용 컴포넌트:
    /// - 바닥에서 9개를 모으던 파렛트 작업대(PalletZone) 오브젝트는 폐기/비활성화합니다.
    /// - 창고 북동쪽 구석(North-East: 13.5, 0, 12.5)에 기본 팔렛트가 장착된 지게차 및 탑승/하차 구역(ForkliftZone)을 배치합니다.
    /// </summary>
    public class PalletStation : MonoBehaviour
    {
        public static PalletStation Instance { get; private set; }
        public Vector3 stationPosition = new Vector3(13.5f, 0f, 12.5f);

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // 혹시 이전 파렛트 작업대 오브젝트가 남아있다면 즉시 제거
            var oldWorkbench = GameObject.Find("PalletZone_Workbench");
            if (oldWorkbench != null) Destroy(oldWorkbench);

            var selfRenderer = GetComponent<Renderer>();
            if (selfRenderer != null) selfRenderer.enabled = false;
            var selfCol = GetComponent<Collider>();
            if (selfCol != null) selfCol.enabled = false;

            EnsurePalletAndForkliftZonesExist();
        }

        public static void EnsurePalletAndForkliftZonesExist()
        {
            var oldWorkbench = GameObject.Find("PalletZone_Workbench");
            if (oldWorkbench != null) Destroy(oldWorkbench);

            // 창고 북동쪽 구석(North-East)에 지게차 구역 및 기본 팔렛트 일체형 지게차 스폰
            if (ForkliftZone.Instance == null && FindAnyObjectByType<ForkliftZone>() == null)
            {
                GameObject fzObj = new GameObject("ForkliftZone_NorthEast");
                var fz = fzObj.AddComponent<ForkliftZone>();
                fz.zonePosition = new Vector3(13.5f, 0f, 12.5f);
            }
        }
    }
}
