using System;
using System.Collections.Generic;
using UnityEngine;
using APCenter.Core;
using APCenter.Delivery;

namespace APCenter.Player
{
    public class PackageStack : MonoBehaviour
    {
        [Header("Stack Settings")]
        [SerializeField] private Transform stackRoot;
        [SerializeField] private float boxHeight = 0.55f;
        [SerializeField] private float wobbleStrength = 4.0f;
        [SerializeField] private float wobbleRecovery = 6.0f;

        private List<PackageItem> stackedPackages = new List<PackageItem>();
        private List<Vector3> wobbleOffsets = new List<Vector3>();

        public int Count => stackedPackages.Count;

        private void Start()
        {
            if (stackRoot == null)
            {
                var root = new GameObject("StackRoot");
                root.transform.SetParent(transform);
                root.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                stackRoot = root.transform;
            }
        }

        private void LateUpdate()
        {
            // 상단으로 갈수록 자연스럽게 흔들리는 물리적 스프링 시뮬레이션
            for (int i = 0; i < stackedPackages.Count; i++)
            {
                if (stackedPackages[i] == null) continue;

                Vector3 targetLocalPos = new Vector3(0f, i * boxHeight, 0f);
                if (i < wobbleOffsets.Count)
                {
                    wobbleOffsets[i] = Vector3.Lerp(wobbleOffsets[i], Vector3.zero, Time.deltaTime * wobbleRecovery);
                    stackedPackages[i].transform.localPosition = targetLocalPos + wobbleOffsets[i];
                }
                else
                {
                    stackedPackages[i].transform.localPosition = targetLocalPos;
                }
            }
        }

        public void AddWobbleImpulse(Vector3 moveDir)
        {
            Vector3 localDir = transform.InverseTransformDirection(moveDir);
            for (int i = 0; i < wobbleOffsets.Count; i++)
            {
                float factor = (i + 1) * 0.05f * wobbleStrength;
                wobbleOffsets[i] = Vector3.ClampMagnitude(wobbleOffsets[i] - localDir * factor, 0.45f);
            }
        }

        public bool CanAdd(int maxCapacity)
        {
            return stackedPackages.Count < maxCapacity;
        }

        public void Push(PackageItem package, Action onComplete = null)
        {
            if (package == null) return;

            int index = stackedPackages.Count;
            stackedPackages.Add(package);
            wobbleOffsets.Add(Vector3.zero);

            package.State = PackageState.Carried;
            Vector3 targetLocal = new Vector3(0f, index * boxHeight, 0f);

            package.FlyTo(stackRoot, targetLocal, 0.2f, () =>
            {
                SoundManager.Instance?.PlayPickup();
                onComplete?.Invoke();
            });
        }

        public PackageItem Pop()
        {
            if (stackedPackages.Count == 0) return null;

            int lastIdx = stackedPackages.Count - 1;
            PackageItem top = stackedPackages[lastIdx];
            stackedPackages.RemoveAt(lastIdx);
            wobbleOffsets.RemoveAt(lastIdx);
            return top;
        }

        public PackageItem Peek()
        {
            if (stackedPackages.Count == 0) return null;
            return stackedPackages[stackedPackages.Count - 1];
        }

        public PackageItem FindMatching(PackageDestination dest)
        {
            if (stackedPackages.Count == 0) return null;

            // 💡 [핵심 수정] 트럭 목적지가 미정(None)인 경우:
            // 손에 들고 있는 택배 중 맨 위(Top) 택배를 그대로 꺼내어 첫 목적지로 확정시킵니다!
            if (dest == PackageDestination.None)
            {
                return Pop();
            }

            // 트럭 목적지가 특정 도시(서울, 부산, 대구)로 확정되어 있는 경우:
            // 해당 목적지와 일치하는 택배를 스택에서 찾아 꺼냅니다.
            for (int i = stackedPackages.Count - 1; i >= 0; i--)
            {
                if (stackedPackages[i].Destination == dest)
                {
                    PackageItem item = stackedPackages[i];
                    stackedPackages.RemoveAt(i);
                    wobbleOffsets.RemoveAt(i);
                    return item;
                }
            }
            return null;
        }

        public PackageItem FindDefective()
        {
            for (int i = stackedPackages.Count - 1; i >= 0; i--)
            {
                if (stackedPackages[i].IsDefective)
                {
                    PackageItem item = stackedPackages[i];
                    stackedPackages.RemoveAt(i);
                    wobbleOffsets.RemoveAt(i);
                    return item;
                }
            }
            return null;
        }
    }
}
