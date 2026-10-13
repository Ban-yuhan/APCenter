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
        private TextMesh stackCountLabel;
        private PlayerController ownerPlayer;

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

            ownerPlayer = GetComponent<PlayerController>();
            if (ownerPlayer != null && stackCountLabel == null)
            {
                GameObject lblObj = new GameObject("StackCountLabel");
                lblObj.transform.SetParent(stackRoot, false);
                lblObj.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                stackCountLabel = lblObj.AddComponent<TextMesh>();
                stackCountLabel.fontSize = 45;
                stackCountLabel.characterSize = 0.08f;
                stackCountLabel.alignment = TextAlignment.Center;
                stackCountLabel.anchor = TextAnchor.MiddleCenter;
                stackCountLabel.color = Color.black;
                Billboard.Attach(lblObj);
                lblObj.SetActive(false);
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

            if (stackCountLabel != null && ownerPlayer != null)
            {
                int count = stackedPackages.Count;
                if (count > 0 && !ownerPlayer.IsDrivingForklift)
                {
                    if (!stackCountLabel.gameObject.activeSelf)
                    {
                        stackCountLabel.gameObject.SetActive(true);
                    }

                    int maxCap = (GameManager.Instance != null) ? GameManager.Instance.GetPlayerMaxStack() : 4;
                    bool isMax = count >= maxCap;
                    stackCountLabel.text = isMax ? $"MAX ({count}/{maxCap})" : $"{count}/{maxCap}";
                    stackCountLabel.color = Color.black;
                    stackCountLabel.transform.localPosition = new Vector3(0f, count * boxHeight + 0.35f, 0f);
                }
                else if (stackCountLabel.gameObject.activeSelf)
                {
                    stackCountLabel.gameObject.SetActive(false);
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

        public PalletItem GetHeldPallet()
        {
            for (int i = stackedPackages.Count - 1; i >= 0; i--)
            {
                if (stackedPackages[i] is PalletItem pallet)
                {
                    return pallet;
                }
            }
            return null;
        }

        public bool HasPallet()
        {
            return GetHeldPallet() != null;
        }

        public bool HasMatching(PackageDestination dest)
        {
            for (int i = stackedPackages.Count - 1; i >= 0; i--)
            {
                var pkg = stackedPackages[i];
                if (pkg == null) continue;
                if (pkg is PalletItem pallet && (!pallet.IsMerged || pallet.IsMergingInProgress)) continue;
                if (dest == PackageDestination.None || pkg.Destination == dest) return true;
            }
            return false;
        }

        public int CountMatching(PackageDestination dest)
        {
            int count = 0;
            for (int i = 0; i < stackedPackages.Count; i++)
            {
                var pkg = stackedPackages[i];
                if (pkg == null) continue;
                if (dest == PackageDestination.None || pkg.Destination == dest) count++;
            }
            return count;
        }

        public PackageItem FindMatching(PackageDestination dest)
        {
            if (stackedPackages.Count == 0) return null;

            for (int i = stackedPackages.Count - 1; i >= 0; i--)
            {
                var pkg = stackedPackages[i];
                if (pkg == null) continue;

                // 아직 택배 9개가 합체되지 않은 빈 파렛트는 상차/분류 대상에서 제외
                if (pkg is PalletItem pallet && (!pallet.IsMerged || pallet.IsMergingInProgress))
                {
                    continue;
                }

                if (dest == PackageDestination.None || pkg.Destination == dest)
                {
                    stackedPackages.RemoveAt(i);
                    wobbleOffsets.RemoveAt(i);
                    return pkg;
                }
            }
            return null;
        }

        public PackageItem FindDefective()
        {
            for (int i = stackedPackages.Count - 1; i >= 0; i--)
            {
                if (stackedPackages[i] != null && stackedPackages[i].IsDefective)
                {
                    // 파렛트는 반품대에 넣지 않음
                    if (stackedPackages[i] is PalletItem) continue;

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
