using System;
using UnityEngine;

namespace APCenter.Player
{
    public class CameraController : MonoBehaviour
    {
        [Header("Target Tracking")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 15f, -12f);
        [SerializeField] private float smoothSpeed = 8f;

        private void Start()
        {
            if (target == null && PlayerController.Instance != null)
            {
                target = PlayerController.Instance.transform;
            }
            transform.rotation = Quaternion.Euler(50f, 0f, 0f);
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                if (PlayerController.Instance != null)
                {
                    target = PlayerController.Instance.transform;
                }
                return;
            }

            Vector3 targetPos = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * smoothSpeed);
        }
    }
}
