using UnityEngine;
using System;
using ChatAI.Core;

namespace ChatAI.Audio
{
    /// <summary>
    /// 麦克风控制器 - 管理麦克风录音的启动、停止和音频数据获取
    /// </summary>
    public class MicrophoneController : MonoBehaviour
    {
        [Header("录音配置")]
        [SerializeField] private int sampleRate = 16000;
        [SerializeField] private int maxRecordSeconds = 30;
        [SerializeField] private string deviceName = null; // null 使用默认麦克风

        // 录音状态
        public bool IsRecording { get; private set; }
        public float CurrentRecordTime { get; private set; }

        // 录音完成事件
        public event Action<AudioClip> OnRecordingComplete;
        public event Action<float[]> OnAudioDataReady;

        private AudioClip _recordingClip;
        private int _lastSamplePosition;

        // VAD 相关
        [Header("VAD 配置")]
        [SerializeField] private float vadThreshold = 0.02f;
        [SerializeField] private float silenceTimeout = 2f;
        private float _silenceTimer;
        private bool _hasDetectedVoice;

        private void Start()
        {
            // 获取默认麦克风设备
            if (Microphone.devices.Length > 0)
            {
                deviceName = Microphone.devices[0];
                Debug.Log($"[Microphone] 使用设备: {deviceName}");
            }
            else
            {
                Debug.LogError("[Microphone] 未检测到麦克风设备！");
            }

            // 订阅事件
            EventCenter.Instance?.Subscribe<InterruptEvent>(OnInterrupt);
        }

        /// <summary>
        /// 开始录音
        /// </summary>
        public void StartRecording()
        {
            if (IsRecording) return;
            if (string.IsNullOrEmpty(deviceName))
            {
                Debug.LogError("[Microphone] 无可用麦克风设备");
                return;
            }

            _recordingClip = Microphone.Start(deviceName, false, maxRecordSeconds, sampleRate);
            IsRecording = true;
            CurrentRecordTime = 0;
            _lastSamplePosition = 0;
            _silenceTimer = 0;
            _hasDetectedVoice = false;

            Debug.Log("[Microphone] 开始录音");
        }

        /// <summary>
        /// 停止录音并返回录音数据
        /// </summary>
        public AudioClip StopRecording()
        {
            if (!IsRecording) return null;

            Microphone.End(deviceName);
            IsRecording = false;

            // 截取实际录音长度的音频
            int actualLength = Mathf.Min(
                Microphone.GetPosition(deviceName),
                _recordingClip.samples
            );

            if (actualLength <= 0)
            {
                Debug.LogWarning("[Microphone] 录音数据为空");
                return null;
            }

            float[] data = new float[actualLength * _recordingClip.channels];
            _recordingClip.GetData(data, 0);

            Debug.Log($"[Microphone] 录音完成，时长: {CurrentRecordTime:F1}秒");

            // 触发事件
            OnAudioDataReady?.Invoke(data);
            OnRecordingComplete?.Invoke(_recordingClip);

            return _recordingClip;
        }

        /// <summary>
        /// 实时检测音频活动（VAD）
        /// </summary>
        private void Update()
        {
            if (!IsRecording) return;

            // 更新录音时间
            int currentPos = Microphone.GetPosition(deviceName);
            if (currentPos > _lastSamplePosition)
            {
                CurrentRecordTime = (float)currentPos / sampleRate;
                _lastSamplePosition = currentPos;
            }

            // VAD 检测
            float amplitude = GetCurrentAmplitude();

            if (amplitude > vadThreshold)
            {
                _hasDetectedVoice = true;
                _silenceTimer = 0;
            }
            else if (_hasDetectedVoice)
            {
                _silenceTimer += Time.deltaTime;
            }

            // 检测到语音后静默超时，自动结束录音
            if (_hasDetectedVoice && _silenceTimer >= silenceTimeout)
            {
                Debug.Log("[Microphone] VAD 检测到语音结束");
                var clip = StopRecording();
                if (clip != null)
                {
                    GameManager.Instance?.OnUserSpeechComplete("[待ASR识别]");
                }
            }

            // 录音超时保护
            if (CurrentRecordTime >= maxRecordSeconds)
            {
                Debug.Log("[Microphone] 录音达到最大时长限制");
                StopRecording();
            }
        }

        /// <summary>
        /// 获取当前麦克风音频振幅（用于 VAD）
        /// </summary>
        private float GetCurrentAmplitude()
        {
            if (_recordingClip == null || !IsRecording) return 0;

            int pos = Microphone.GetPosition(deviceName);
            if (pos <= 0) return 0;

            int sampleCount = Mathf.Min(512, pos);
            float[] samples = new float[sampleCount * _recordingClip.channels];
            _recordingClip.GetData(samples, pos - sampleCount);

            float sum = 0;
            for (int i = 0; i < samples.Length; i++)
                sum += samples[i] * samples[i];

            return Mathf.Sqrt(sum / samples.Length);
        }

        /// <summary>
        /// 将 AudioClip 转为 PCM byte 数组（用于发送 ASR）
        /// </summary>
        public static byte[] AudioClipToPCM(AudioClip clip)
        {
            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            byte[] pcm = new byte[samples.Length * 2]; // 16-bit PCM
            for (int i = 0; i < samples.Length; i++)
            {
                short sample = (short)(samples[i] * short.MaxValue);
                pcm[i * 2] = (byte)(sample & 0xFF);
                pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            return pcm;
        }

        private void OnInterrupt(InterruptEvent evt)
        {
            if (IsRecording)
                StopRecording();
        }

        private void OnDestroy()
        {
            if (IsRecording)
                Microphone.End(deviceName);

            EventCenter.Instance?.Unsubscribe<InterruptEvent>(OnInterrupt);
        }
    }
}
