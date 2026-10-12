using System;
using UnityEngine;

namespace APCenter.Core
{
    public class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        private AudioSource sfxSource;
        private AudioSource musicSource;

        private AudioClip pickupClip;
        private AudioClip dropClip;
        private AudioClip coinClip;
        private AudioClip truckHornClip;
        private AudioClip alertClip;
        private AudioClip successClip;
        private AudioClip clickClip;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            sfxSource = gameObject.AddComponent<AudioSource>();
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.volume = 0.25f;

            GenerateProceduralClips();
        }

        private void GenerateProceduralClips()
        {
            pickupClip = CreateBeepClip(580f, 0.08f, 0.4f, 1200f);
            dropClip = CreateBeepClip(320f, 0.08f, 0.35f, 200f);
            coinClip = CreateCoinClip();
            truckHornClip = CreateHornClip();
            alertClip = CreateAlertClip();
            successClip = CreateSuccessClip();
            clickClip = CreateBeepClip(800f, 0.04f, 0.2f, 900f);
        }

        public void PlayPickup() => PlayClip(pickupClip, 0.5f);
        public void PlayDrop() => PlayClip(dropClip, 0.5f);
        public void PlayCoin() => PlayClip(coinClip, 0.6f);
        public void PlayTruckHorn() => PlayClip(truckHornClip, 0.7f);
        public void PlayAlert() => PlayClip(alertClip, 0.8f);
        public void PlaySuccess() => PlayClip(successClip, 0.8f);
        public void PlayClick() => PlayClip(clickClip, 0.4f);

        private void PlayClip(AudioClip clip, float vol)
        {
            if (clip != null && sfxSource != null)
            {
                sfxSource.PlayOneShot(clip, vol);
            }
        }

        private AudioClip CreateBeepClip(float startFreq, float duration, float gain, float endFreq)
        {
            int sampleRate = 44100;
            int samples = (int)(sampleRate * duration);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float freq = Mathf.Lerp(startFreq, endFreq, t);
                float envelope = 1f - t;
                data[i] = Mathf.Sin(2 * Mathf.PI * freq * (i / (float)sampleRate)) * gain * envelope;
            }

            AudioClip clip = AudioClip.Create("Beep", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip CreateCoinClip()
        {
            int sampleRate = 44100;
            float duration = 0.25f;
            int samples = (int)(sampleRate * duration);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float freq = (t < 0.4f) ? 987.77f : 1318.51f; // B5 to E6 chime
                float envelope = Mathf.Exp(-t * 6f);
                data[i] = Mathf.Sin(2 * Mathf.PI * freq * (i / (float)sampleRate)) * 0.45f * envelope;
            }

            AudioClip clip = AudioClip.Create("Coin", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip CreateHornClip()
        {
            int sampleRate = 44100;
            float duration = 0.5f;
            int samples = (int)(sampleRate * duration);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float envelope = (t < 0.1f) ? (t / 0.1f) : (1f - (t - 0.1f) / 0.9f);
                float wave = Mathf.Sin(2 * Mathf.PI * 180f * (i / (float)sampleRate)) * 0.4f
                           + Mathf.Sin(2 * Mathf.PI * 225f * (i / (float)sampleRate)) * 0.3f;
                data[i] = wave * envelope * 0.5f;
            }

            AudioClip clip = AudioClip.Create("TruckHorn", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip CreateAlertClip()
        {
            int sampleRate = 44100;
            float duration = 0.3f;
            int samples = (int)(sampleRate * duration);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float freq = (i % 2000 < 1000) ? 650f : 880f;
                float envelope = 1f - t;
                data[i] = Mathf.Sin(2 * Mathf.PI * freq * (i / (float)sampleRate)) * 0.5f * envelope;
            }

            AudioClip clip = AudioClip.Create("Alert", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip CreateSuccessClip()
        {
            int sampleRate = 44100;
            float duration = 0.7f;
            int samples = (int)(sampleRate * duration);
            float[] data = new float[samples];

            float[] notes = new float[] { 523.25f, 659.25f, 783.99f, 1046.50f }; // C5, E5, G5, C6
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                int noteIndex = Mathf.Clamp((int)(t * 4), 0, 3);
                float freq = notes[noteIndex];
                float segT = (t * 4) - noteIndex;
                float env = 1f - segT;
                data[i] = Mathf.Sin(2 * Mathf.PI * freq * (i / (float)sampleRate)) * 0.4f * env;
            }

            AudioClip clip = AudioClip.Create("Success", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
