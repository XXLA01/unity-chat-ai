using UnityEngine;
using System;
using ChatAI.Core;
using ChatAI.Audio;

namespace ChatAI.WakeWord
{
    /// <summary>
    /// 唤醒词检测器
    /// 持续监听麦克风，检测是否说出"你好，小童"
    /// 
    /// 实现策略：
    /// 1. 使用 VAD 检测语音活动
    /// 2. 检测到语音后，将短音频发送至 ASR
    /// 3. ASR 返回文本后匹配唤醒词
    /// </summary>
    public class WakeWordDetector : MonoBehaviour
    {
        [Header("唤醒词配置")]
        [SerializeField] private string wakeWord = "你好小童";
        [SerializeField, Tooltip("允许的模糊匹配度（0~1），1 为完全匹配")]
        private float matchThreshold = 0.7f;

        [Header("检测参数")]
        [SerializeField, Tooltip("VAD 灵敏度阈值")]
        private float vadThreshold = 0.015f;
        [SerializeField, Tooltip("语音最短时长（秒），过滤太短的噪音")]
        private float minVoiceDuration = 0.5f;
        [SerializeField, Tooltip("语音后静默超时（秒），超时后发送识别")]
        private float silenceTimeout = 1.5f;

        // 状态
        public bool IsActive { get; private set; }
        public event Action OnWakeWordDetected;

        // 内部状态
        private bool _isVoiceDetected;
        private float _voiceStartTime;
        private float _silenceTimer;
        private MicrophoneController _microphone;

        private void Start()
        {
            _microphone = GetComponent<MicrophoneController>();
            if (_microphone == null)
                _microphone = gameObject.AddComponent<MicrophoneController>();

            // 启动持续监听
            StartListening();
        }

        /// <summary>
        /// 开始持续监听唤醒词
        /// </summary>
        public void StartListening()
        {
            IsActive = true;
            _microphone.StartRecording();
            Debug.Log("[WakeWord] 开始监听唤醒词...");
        }

        /// <summary>
        /// 停止监听
        /// </summary>
        public void StopListening()
        {
            IsActive = false;
            _microphone.StopRecording();
        }

        /// <summary>
        /// 检查 ASR 返回的文本是否包含唤醒词
        /// </summary>
        public void CheckWakeWord(string recognizedText)
        {
            if (!IsActive) return;
            if (string.IsNullOrEmpty(recognizedText)) return;

            // 清理文本（去除标点、空格）
            string cleaned = CleanText(recognizedText);
            string target = CleanText(wakeWord);

            Debug.Log($"[WakeWord] 识别文本: '{cleaned}', 目标: '{target}'");

            // 简单包含匹配
            if (cleaned.Contains(target))
            {
                TriggerWakeWord();
                return;
            }

            // 模糊匹配（编辑距离）
            float similarity = CalculateSimilarity(cleaned, target);
            if (similarity >= matchThreshold)
            {
                Debug.Log($"[WakeWord] 模糊匹配成功，相似度: {similarity:F2}");
                TriggerWakeWord();
            }
        }

        private void TriggerWakeWord()
        {
            Debug.Log("[WakeWord] ★ 唤醒词检测成功！★");
            IsActive = false;
            _microphone.StopRecording();

            OnWakeWordDetected?.Invoke();
            GameManager.Instance?.OnWakeWordDetected();
        }

        /// <summary>
        /// 清除文本中的标点和空格
        /// </summary>
        private string CleanText(string text)
        {
            return text.Replace(" ", "")
                       .Replace("，", "")
                       .Replace(",", "")
                       .Replace("。", "")
                       .Replace(".", "")
                       .Replace("！", "")
                       .Replace("!", "")
                       .Replace("？", "")
                       .Replace("?", "");
        }

        /// <summary>
        /// 计算两个字符串的相似度（基于编辑距离）
        /// </summary>
        private float CalculateSimilarity(string a, string b)
        {
            if (a.Length == 0 && b.Length == 0) return 1f;

            int maxLen = Mathf.Max(a.Length, b.Length);
            if (maxLen == 0) return 1f;

            int distance = LevenshteinDistance(a, b);
            return 1f - (float)distance / maxLen;
        }

        /// <summary>
        /// Levenshtein 编辑距离算法
        /// </summary>
        private int LevenshteinDistance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];

            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    d[i, j] = Mathf.Min(
                        Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost
                    );
                }
            }
            return d[a.Length, b.Length];
        }

        private void OnDestroy()
        {
            StopListening();
        }
    }
}
