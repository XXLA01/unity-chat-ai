using UnityEngine;
using System;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

namespace ChatAI.Core
{
    /// <summary>
    /// 唤醒词检测器
    /// Windows 平台使用 KeywordRecognizer（本地语音识别引擎），零延迟、无 API 消耗
    /// 检测到唤醒词后触发事件，由上层决定是否开始录音
    /// </summary>
    public class WakeWordDetector : MonoBehaviour
    {
        [Header("唤醒词配置")]
        [SerializeField, Tooltip("唤醒关键词（支持多个）")]
        private string[] wakeKeywords = { "你好小童", "你好 小童" };

        [SerializeField, Tooltip("识别置信度阈值")]
        private ConfidenceLevel minConfidence = ConfidenceLevel.Low;

        [Header("状态")]
        [SerializeField] private bool autoStart = true;

        // 事件
        public event Action OnWakeWordDetected;

        // 状态
        public bool IsListening { get; private set; }
        public bool IsPaused { get; private set; }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private KeywordRecognizer _recognizer;
#endif

        private void Start()
        {
            if (autoStart)
                StartListening();
        }

        /// <summary>
        /// 开始监听唤醒词
        /// </summary>
        public void StartListening()
        {
            if (IsListening) return;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                // 过滤空关键词
                var validKeywords = Array.FindAll(wakeKeywords, k => !string.IsNullOrEmpty(k.Trim()));
                if (validKeywords.Length == 0)
                {
                    Debug.LogWarning("[WakeWord] 未配置有效的唤醒关键词");
                    return;
                }

                _recognizer = new KeywordRecognizer(validKeywords, minConfidence);
                _recognizer.OnPhraseRecognized += OnPhraseRecognized;
                _recognizer.Start();
                IsListening = true;
                Debug.Log($"[WakeWord] 开始监听，关键词: {string.Join(", ", validKeywords)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[WakeWord] 启动 KeywordRecognizer 失败: {e.Message}");
                Debug.LogWarning("[WakeWord] 请确保系统已安装中文语音识别（Windows 设置 → 时间和语言 → 语音 → 添加语音）");
            }
#else
            Debug.LogWarning("[WakeWord] KeywordRecognizer 仅支持 Windows 平台，当前平台暂不支持唤醒词检测");
#endif
        }

        /// <summary>
        /// 停止监听
        /// </summary>
        public void StopListening()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_recognizer != null && _recognizer.IsRunning)
            {
                _recognizer.Stop();
                _recognizer.OnPhraseRecognized -= OnPhraseRecognized;
                _recognizer.Dispose();
                _recognizer = null;
            }
#endif
            IsListening = false;
            IsPaused = false;
        }

        /// <summary>
        /// 暂停监听（TTS 播报时调用，防止回声误触发）
        /// </summary>
        public void PauseListening()
        {
            if (!IsListening || IsPaused) return;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_recognizer != null && _recognizer.IsRunning)
                _recognizer.Stop();
#endif
            IsPaused = true;
            Debug.Log("[WakeWord] 暂停监听（TTS 播报中）");
        }

        /// <summary>
        /// 恢复监听（TTS 播报结束后调用）
        /// </summary>
        public void ResumeListening()
        {
            if (!IsPaused) return;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_recognizer != null && !_recognizer.IsRunning)
                _recognizer.Start();
#endif
            IsPaused = false;
            Debug.Log("[WakeWord] 恢复监听");
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private void OnPhraseRecognized(PhraseRecognizedEventArgs args)
        {
            Debug.Log($"[WakeWord] 检测到唤醒词: \"{args.text}\" (置信度: {args.confidence})");
            OnWakeWordDetected?.Invoke();
        }
#endif

        private void OnDestroy()
        {
            StopListening();
        }
    }
}
