using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace APCenter.Player
{
    public class VirtualJoystick : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform background;
        [SerializeField] private RectTransform handle;
        [SerializeField] private float handleRange = 60f;

        private Vector2 inputVector = Vector2.zero;

        private void Start()
        {
            if (background == null) background = GetComponent<RectTransform>();
            if (handle == null && transform.childCount > 0) handle = transform.GetChild(0).GetComponent<RectTransform>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            Vector2 position = RectTransformUtility.WorldToScreenPoint(eventData.pressEventCamera, background.position);
            Vector2 radius = background.sizeDelta / 2f;
            inputVector = (eventData.position - position) / (radius.magnitude);
            inputVector = (inputVector.magnitude > 1.0f) ? inputVector.normalized : inputVector;

            if (handle != null)
            {
                handle.anchoredPosition = inputVector * handleRange;
            }

            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.JoystickInput = inputVector;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            inputVector = Vector2.zero;
            if (handle != null)
            {
                handle.anchoredPosition = Vector2.zero;
            }

            if (PlayerController.Instance != null)
            {
                PlayerController.Instance.JoystickInput = Vector2.zero;
            }
        }
    }
}
