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
    public class WakeWordDetector : MonoBehaviour, IWakeWordDetector
    {
        [Header("唤醒词配置")]
        [SerializeField, Tooltip("唤醒关键词（支持多个）")]
        private string[] wakeKeywords = { "你好小童", "你好 小童" };

        [SerializeField, Tooltip("打断关键词（TTS 播报期间检测到这些词会立即停止播放）")]
        private string[] interruptKeywords = { "停止", "停一下", "别说了", "闭嘴" };

        [SerializeField, Tooltip("休眠关键词（检测到这些词后退出对话模式，需重新唤醒）")]
        private string[] sleepKeywords = { "退下", "休眠", "再见", "拜拜", "下去吧" };

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [SerializeField, Tooltip("识别置信度阈值")]
        private ConfidenceLevel minConfidence = ConfidenceLevel.Low;
#endif

        [Header("状态")]
        [SerializeField] private bool autoStart = true;

        // 事件
        public event Action OnWakeWordDetected;
        public event Action OnInterruptDetected;
        public event Action OnSleepDetected;

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
        /// 运行时设置唤醒关键词和置信度（从 CozeConfig 读取）
        /// 需在 StartListening 之前调用，或在停止后重新调用
        /// </summary>
        public void SetKeywords(string[] keywords
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            , ConfidenceLevel confidence
#endif
        )
        {
            bool wasListening = IsListening;
            if (wasListening) StopListening();

            wakeKeywords = keywords;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            minConfidence = confidence;
#endif

            if (wasListening) StartListening();
        }

        /// <summary>
        /// 运行时设置打断关键词（从 CozeConfig 读取时调用）
        /// </summary>
        public void SetInterruptKeywords(string[] keywords)
        {
            bool wasListening = IsListening;
            if (wasListening) StopListening();

            interruptKeywords = keywords;

            if (wasListening) StartListening();
        }

        /// <summary>
        /// 运行时设置休眠关键词（从 CozeConfig 读取时调用）
        /// </summary>
        public void SetSleepKeywords(string[] keywords)
        {
            bool wasListening = IsListening;
            if (wasListening) StopListening();

            sleepKeywords = keywords;

            if (wasListening) StartListening();
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
                // 合并唤醒词、打断词、休眠词到同一个识别器
                var validWake = Array.FindAll(wakeKeywords, k => !string.IsNullOrEmpty(k.Trim()));
                var validInterrupt = (interruptKeywords != null)
                    ? Array.FindAll(interruptKeywords, k => !string.IsNullOrEmpty(k.Trim()))
                    : Array.Empty<string>();
                var validSleep = (sleepKeywords != null)
                    ? Array.FindAll(sleepKeywords, k => !string.IsNullOrEmpty(k.Trim()))
                    : Array.Empty<string>();

                var allKeywords = new string[validWake.Length + validInterrupt.Length + validSleep.Length];
                Array.Copy(validWake, 0, allKeywords, 0, validWake.Length);
                Array.Copy(validInterrupt, 0, allKeywords, validWake.Length, validInterrupt.Length);
                Array.Copy(validSleep, 0, allKeywords, validWake.Length + validInterrupt.Length, validSleep.Length);

                if (allKeywords.Length == 0)
                {
                    Debug.LogWarning("[WakeWord] 未配置有效的关键词");
                    return;
                }

                _recognizer = new KeywordRecognizer(allKeywords, minConfidence);
                _recognizer.OnPhraseRecognized += OnPhraseRecognized;
                _recognizer.Start();
                IsListening = true;
                Debug.Log($"[WakeWord] 开始监听，唤醒词: {string.Join(", ", validWake)}, 打断词: {string.Join(", ", validInterrupt)}, 休眠词: {string.Join(", ", validSleep)}");
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
            string text = args.text;
            string normalized = text.Replace(" ", "").ToLower();

            // 检查是否是打断词
            if (interruptKeywords != null)
            {
                foreach (string kw in interruptKeywords)
                {
                    if (string.IsNullOrEmpty(kw)) continue;
                    string normalizedKw = kw.Replace(" ", "").ToLower();
                    if (normalized.Contains(normalizedKw) || normalizedKw.Contains(normalized))
                    {
                        Debug.Log($"[WakeWord] 检测到打断词: \"{text}\" (置信度: {args.confidence})");
                        OnInterruptDetected?.Invoke();
                        return;
                    }
                }
            }

            // 检查是否是休眠词
            if (sleepKeywords != null)
            {
                foreach (string kw in sleepKeywords)
                {
                    if (string.IsNullOrEmpty(kw)) continue;
                    string normalizedKw = kw.Replace(" ", "").ToLower();
                    if (normalized.Contains(normalizedKw) || normalizedKw.Contains(normalized))
                    {
                        Debug.Log($"[WakeWord] 检测到休眠词: \"{text}\" (置信度: {args.confidence})");
                        OnSleepDetected?.Invoke();
                        return;
                    }
                }
            }

            // 否则视为唤醒词
            Debug.Log($"[WakeWord] 检测到唤醒词: \"{text}\" (置信度: {args.confidence})");
            OnWakeWordDetected?.Invoke();
        }
#endif

        private void OnDestroy()
        {
            StopListening();
        }
    }
}
