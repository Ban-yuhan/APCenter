using System;
using UnityEngine;
using APCenter.Core;
using APCenter.Player;

namespace APCenter.Facilities
{
    public class WeatherTaskZone : InteractionZone
    {
        [SerializeField] private GameObject visualIndicator;

        private void Start()
        {
            if (visualIndicator != null) visualIndicator.SetActive(false);

            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.OnEventStarted += HandleEventStarted;
                EventSystem.Instance.OnEventEnded += HandleEventEnded;
            }
        }

        private void OnDestroy()
        {
            if (EventSystem.Instance != null)
            {
                EventSystem.Instance.OnEventStarted -= HandleEventStarted;
                EventSystem.Instance.OnEventEnded -= HandleEventEnded;
            }
        }

        private void HandleEventStarted(GameEventType type, string title, float duration)
        {
            bool isWeather = (type == GameEventType.BadWeather_Snow ||
                              type == GameEventType.BadWeather_Rain ||
                              type == GameEventType.BadWeather_Blackout);
            if (visualIndicator != null) visualIndicator.SetActive(isWeather);
        }

        private void HandleEventEnded(GameEventType type, bool isSuccess)
        {
            if (visualIndicator != null) visualIndicator.SetActive(false);
        }

        protected override void OnTick(PlayerController p)
        {
            if (EventSystem.Instance == null) return;

            var current = EventSystem.Instance.ActiveEvent;
            if (current == GameEventType.BadWeather_Snow ||
                current == GameEventType.BadWeather_Rain ||
                current == GameEventType.BadWeather_Blackout)
            {
                EventSystem.Instance.AdvanceWeatherTask();
            }
        }
    }
}
