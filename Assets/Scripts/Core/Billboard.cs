using UnityEngine;
using TMPro;

namespace APCenter.Core
{
    /// <summary>
    /// 3D 월드 공간의 텍스트(TextMesh / TextMeshPro)가 부모 오브젝트의 회전과 무관하게
    /// 항상 메인 카메라 정면을 바라보도록 회전 동기화하고,
    /// 폰트 크기를 45pt로 통일하며, 지역 표시(서울/수도권, 부산/영남, 대구/충청)를 제외한
    /// 모든 3D 텍스트 색상을 검정색(Color.black)으로 통일하는 빌보드 컴포넌트입니다.
    /// (Canvas 산하의 Screen Space UI 텍스트는 적용 대상에서 제외됩니다.)
    /// </summary>
    [DisallowMultipleComponent]
    public class Billboard : MonoBehaviour
    {
        public const int StandardFontSizeInt = 45;
        public const float StandardFontSize = 45f;

        [SerializeField] private float targetFontSize = StandardFontSize;

        private TextMesh legacyTextMesh;
        private TMP_Text tmpText;
        private bool isScreenSpaceUI = false;

        private void Awake()
        {
            ApplyStandardSpec();
        }

        private void Start()
        {
            ApplyStandardSpec();
        }

        private void OnEnable()
        {
            AlignToMainCamera();
        }

        public static bool IsRegionalHeaderText(string text, string objName = null)
        {
            if (!string.IsNullOrEmpty(text))
            {
                if (text.Contains("서울/수도권") || text.Contains("부산/영남") || text.Contains("대구/충청"))
                {
                    return true;
                }
            }
            if (!string.IsNullOrEmpty(objName))
            {
                if (objName.Contains("서울/수도권") || objName.Contains("부산/영남") || objName.Contains("대구/충청"))
                {
                    return true;
                }
            }
            return false;
        }

        public void ApplyStandardSpec()
        {
            Canvas parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas != null && parentCanvas.renderMode != RenderMode.WorldSpace)
            {
                isScreenSpaceUI = true;
                return;
            }

            isScreenSpaceUI = false;
            targetFontSize = StandardFontSize;

            if (legacyTextMesh == null)
            {
                legacyTextMesh = GetComponent<TextMesh>();
            }
            if (legacyTextMesh != null)
            {
                legacyTextMesh.fontSize = StandardFontSizeInt;
                if (!IsRegionalHeaderText(legacyTextMesh.text, gameObject.name))
                {
                    legacyTextMesh.color = Color.black;
                }
            }

            if (tmpText == null)
            {
                tmpText = GetComponent<TMP_Text>();
            }
            if (tmpText != null)
            {
                tmpText.enableAutoSizing = false;
                tmpText.fontSize = StandardFontSize;
                if (!IsRegionalHeaderText(tmpText.text, gameObject.name))
                {
                    tmpText.color = Color.black;
                }
            }
        }

        private void LateUpdate()
        {
            if (isScreenSpaceUI) return;

            if (legacyTextMesh != null)
            {
                if (legacyTextMesh.fontSize != StandardFontSizeInt)
                {
                    legacyTextMesh.fontSize = StandardFontSizeInt;
                }
                if (!IsRegionalHeaderText(legacyTextMesh.text, gameObject.name) && legacyTextMesh.color != Color.black)
                {
                    legacyTextMesh.color = Color.black;
                }
            }

            if (tmpText != null)
            {
                if (Mathf.Abs(tmpText.fontSize - StandardFontSize) > 0.01f)
                {
                    tmpText.enableAutoSizing = false;
                    tmpText.fontSize = StandardFontSize;
                }
                if (!IsRegionalHeaderText(tmpText.text, gameObject.name) && tmpText.color != Color.black)
                {
                    tmpText.color = Color.black;
                }
            }

            AlignToMainCamera();
        }

        private void AlignToMainCamera()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                // 부모 오브젝트의 회전과 무관하게 항상 메인 카메라 정면과 평행하도록 월드 회전 동기화
                transform.rotation = cam.transform.rotation;
            }
        }

        /// <summary>
        /// 동적으로 생성된 3D 텍스트 오브젝트에 Billboard 컴포넌트를 부착하고 폰트 크기(45pt) 및 색상 규격을 즉시 적용합니다.
        /// </summary>
        public static Billboard Attach(GameObject targetObj)
        {
            if (targetObj == null) return null;

            Canvas parentCanvas = targetObj.GetComponentInParent<Canvas>();
            if (parentCanvas != null && parentCanvas.renderMode != RenderMode.WorldSpace)
            {
                return null;
            }

            Billboard bb = targetObj.GetComponent<Billboard>();
            if (bb == null)
            {
                bb = targetObj.AddComponent<Billboard>();
            }
            bb.ApplyStandardSpec();
            bb.AlignToMainCamera();
            return bb;
        }

        public static Billboard Attach(Component targetComp)
        {
            return (targetComp != null) ? Attach(targetComp.gameObject) : null;
        }

        /// <summary>
        /// 씬 로드 시 3D 월드에 배치된 모든 3D 텍스트(TextMesh / TextMeshPro)를 전수 점검하여
        /// Screen Space Canvas UI를 제외한 모든 월드 텍스트에 45pt, 색상 규격(지역 표시 외 검정색) 및 Billboard를 일괄 적용합니다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void StandardizeAllSceneWorldTexts()
        {
            var textMeshes = Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var tm in textMeshes)
            {
                if (tm == null) continue;
                tm.fontSize = StandardFontSizeInt;
                if (!IsRegionalHeaderText(tm.text, tm.gameObject.name))
                {
                    tm.color = Color.black;
                }
                Attach(tm.gameObject);
            }

            var tmpTexts = Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var tmp in tmpTexts)
            {
                if (tmp == null) continue;

                Canvas canvas = tmp.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
                {
                    // Canvas 산하의 Screen Space UI 텍스트는 제외
                    continue;
                }

                tmp.enableAutoSizing = false;
                tmp.fontSize = StandardFontSize;
                if (!IsRegionalHeaderText(tmp.text, tmp.gameObject.name))
                {
                    tmp.color = Color.black;
                }
                Attach(tmp.gameObject);
            }
        }
    }
}
