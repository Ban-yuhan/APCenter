using System;
using System.Collections;
using UnityEngine;
using APCenter.Core;

namespace APCenter.Delivery
{
    public class PackageItem : MonoBehaviour
    {
        public int PackageId { get; private set; }
        public PackageDestination Destination { get; private set; }
        public PackageState State { get; set; } = PackageState.Unloaded;
        public bool IsDefective { get; set; } = false; // 파손/오분류/불량 택배
        public bool IsUrgent { get; set; } = false;    // VIP 긴급배송 택배
        public bool IsPalletized { get; set; } = false; // 파렛트 묶음

        [Header("Visual Components")]
        [SerializeField] private MeshRenderer boxRenderer;
        [SerializeField] private GameObject urgentRibbon;
        [SerializeField] private GameObject defectMarker;

        private static int nextId = 1;

        public void UpdateAppearance()
        {
            if (boxRenderer == null)
            {
                boxRenderer = GetComponentInChildren<MeshRenderer>();
            }

            if (boxRenderer != null)
            {
                // 목적지별 고유 색상 머티리얼 적용
                Color destColor = GetDestinationColor(Destination);
                if (IsUrgent)
                {
                    destColor = new Color(1.0f, 0.84f, 0.0f); // 빛나는 황금색
                }

                // 💡 new Material(...) 생성 대신 기존 material의 color만 수정하여 메모리 누수 및 에러 방지
                boxRenderer.material.color = destColor;
            }

            if (urgentRibbon != null) urgentRibbon.SetActive(IsUrgent);
            if (defectMarker != null) defectMarker.SetActive(IsDefective);
        }

        public static Color GetDestinationColor(PackageDestination dest)
        {
            switch (dest)
            {
                case PackageDestination.Seoul:
                    return new Color(0.92f, 0.25f, 0.25f); // Red
                case PackageDestination.Busan:
                    return new Color(0.18f, 0.49f, 0.96f); // Blue
                case PackageDestination.Daegu:
                    return new Color(0.22f, 0.78f, 0.35f); // Green
                case PackageDestination.None:
                default:
                    return new Color(0.7f, 0.7f, 0.7f);   // 회색 (미정 또는 기본값)
            }
        }

        public void Initialize(PackageDestination destination, bool isDefective = false, bool isUrgent = false)
        {
            PackageId = nextId++;
            Destination = destination;
            IsDefective = isDefective;
            IsUrgent = isUrgent;
            State = PackageState.Unloaded;

            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            UpdateAppearance();
        }

        public void FlyTo(Transform targetParent, Vector3 targetLocalPos, float duration, Action onComplete = null)
        {
            StartCoroutine(FlyCoroutine(targetParent, targetLocalPos, duration, onComplete));
        }

        private IEnumerator FlyCoroutine(Transform targetParent, Vector3 targetLocalPos, float duration, Action onComplete)
        {
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            transform.SetParent(targetParent);

            Quaternion targetWorldRot = (targetParent != null) ? targetParent.rotation : Quaternion.identity;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                Vector3 currentTargetWorld = (targetParent != null) ? targetParent.TransformPoint(targetLocalPos) : targetLocalPos;
                Vector3 linearPos = Vector3.Lerp(startPos, currentTargetWorld, smoothT);
                float arcHeight = Mathf.Sin(t * Mathf.PI) * 1.5f;

                transform.position = linearPos + Vector3.up * arcHeight;
                transform.rotation = Quaternion.Slerp(startRot, targetWorldRot, smoothT);
                yield return null;
            }

            if (targetParent != null)
            {
                transform.localPosition = targetLocalPos;
                transform.localRotation = Quaternion.identity;
            }
            else
            {
                transform.position = targetLocalPos;
                transform.rotation = Quaternion.identity;
            }

            onComplete?.Invoke();
        }
    }
}