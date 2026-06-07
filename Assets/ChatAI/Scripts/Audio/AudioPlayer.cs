using UnityEngine;
using System;
using System.Collections.Generic;
using ChatAI.Core;

namespace ChatAI.Audio
{
    /// <summary>
    /// 音频播放器 - 支持音频队列顺序播放，驱动 Live2D 口型同步
    /// </summary>
    public class AudioPlayer : MonoBehaviour
    {
        [Header("播放配置")]
        [SerializeField] private AudioSource audioSource;

        // 播放状态
        public bool IsPlaying => audioSource != null && audioSource.isPlaying;
        public float CurrentAmplitude { get; private set; }

        // 事件
        public event Action OnPlaybackStarted;
        public event Action OnPlaybackCompleted;
        public event Action<float> OnAmplitudeUpdate;

        // 音频队列
        private readonly Queue<AudioClip> _audioQueue = new Queue<AudioClip>();
        private bool _isProcessing;

        private void Awake()
        {
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0; // 2D 音效
        }

        private void Start()
        {
            EventCenter.Instance?.Subscribe<InterruptEvent>(OnInterrupt);
        }

        /// <summary>
        /// 将音频加入播放队列
        /// </summary>
        public void Enqueue(AudioClip clip)
        {
            if (clip == null) return;
            _audioQueue.Enqueue(clip);

            if (!IsPlaying && !_isProcessing)
                PlayNext();
        }

        /// <summary>
        /// 播放下一个音频
        /// </summary>
        private void PlayNext()
        {
            if (_audioQueue.Count == 0)
            {
                _isProcessing = false;
                OnPlaybackCompleted?.Invoke();
                GameManager.Instance?.OnAIResponseComplete();
                return;
            }

            _isProcessing = true;
            var clip = _audioQueue.Dequeue();
            audioSource.clip = clip;
            audioSource.Play();

            OnPlaybackStarted?.Invoke();
            GameManager.Instance?.OnAIResponseStart();
        }

        private void Update()
        {
            if (!IsPlaying) return;

            // 实时提取振幅（用于口型同步）
            CurrentAmplitude = GetAmplitude();
            OnAmplitudeUpdate?.Invoke(CurrentAmplitude);

            // 当前音频播完，播放下一个
            if (!audioSource.isPlaying && _isProcessing)
            {
                PlayNext();
            }
        }

        /// <summary>
        /// 获取当前音频的实时振幅
        /// </summary>
        private float GetAmplitude()
        {
            if (!audioSource.isPlaying) return 0;

            float[] samples = new float[256];
            audioSource.GetOutputData(samples, 0);

            float sum = 0;
            for (int i = 0; i < samples.Length; i++)
                sum += samples[i] * samples[i];

            return Mathf.Sqrt(sum / samples.Length);
        }

        /// <summary>
        /// 停止播放并清空队列
        /// </summary>
        public void StopAndClear()
        {
            audioSource.Stop();
            audioSource.clip = null;
            _audioQueue.Clear();
            _isProcessing = false;
            CurrentAmplitude = 0;
        }

        /// <summary>
        /// 从 PCM 字节数组创建 AudioClip
        /// </summary>
        public static AudioClip PCMToAudioClip(byte[] pcmData, int sampleRate, int channels, string clipName = "TTS")
        {
            int sampleCount = pcmData.Length / 2 / channels; // 16-bit PCM
            float[] samples = new float[sampleCount * channels];

            for (int i = 0; i < sampleCount * channels; i++)
            {
                short sample = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
                samples[i] = sample / (float)short.MaxValue;
            }

            var clip = AudioClip.Create(clipName, sampleCount, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnInterrupt(InterruptEvent evt)
        {
            StopAndClear();
        }

        private void OnDestroy()
        {
            EventCenter.Instance?.Unsubscribe<InterruptEvent>(OnInterrupt);
        }
    }
}
