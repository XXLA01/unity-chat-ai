using UnityEngine;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ionic.Zip;
using UnityEngine.Networking;
using Vosk;

namespace ChatAI.Core
{
    /// <summary>
    /// 基于 Vosk 离线语音识别的跨平台唤醒词检测器
    /// 适用于 macOS、Linux、Android 等不支持 Windows KeywordRecognizer 的平台
    /// 需要 StreamingAssets 中放置 Vosk 模型文件（zip 格式，首次运行自动解压）
    /// </summary>
    public class VoskWakeWordDetector : MonoBehaviour, IWakeWordDetector
    {
        [Header("唤醒词配置")]
        [SerializeField, Tooltip("唤醒关键词（支持多个）")]
        private string[] wakeKeywords = { "你好小童", "你好 小童" };

        [SerializeField, Tooltip("打断关键词（TTS 播报期间检测到这些词会立即停止播放）")]
        private string[] interruptKeywords = { "停止", "停一下", "别说了", "闭嘴" };

        [SerializeField, Tooltip("休眠关键词（检测到这些词后退出对话模式，需重新唤醒）")]
        private string[] sleepKeywords = { "退下", "休眠", "再见", "拜拜", "下去吧" };

        [Header("Vosk 模型")]
        [SerializeField, Tooltip("模型文件名（相对于 StreamingAssets），zip 格式首次自动解压")]
        private string modelFileName = "vosk-model-small-cn-0.22.zip";

        [SerializeField, Tooltip("音频采样率（Hz），Vosk 小模型通常为 16000")]
        private int sampleRate = 16000;

        [SerializeField, Tooltip("VoiceProcessor 每次传递的采样数")]
        private int frameSize = 512;

        [Header("检测参数")]
        [SerializeField, Tooltip("唤醒词触发后的冷却时间（秒），防止重复触发")]
        private float wakeCooldown = 2f;

        [SerializeField, Tooltip("部分识别结果中需达到的最小字符数，过短的结果将被忽略")]
        private int minPartialLength = 2;

        [SerializeField] private bool autoStart = true;

        // ── 事件 ──
        public event Action OnWakeWordDetected;
        public event Action OnInterruptDetected;  // TTS 播报期间检测到打断词
        public event Action OnSleepDetected;     // 检测到休眠词

        // ── 状态 ──
        public bool IsListening { get; private set; }
        public bool IsPaused { get; private set; }

        // ── Vosk 核心 ──
        private Model _model;
        private VoskRecognizer _recognizer;
        private string _decompressedModelPath;
        private VoiceProcessor _voiceProcessor;

        // ── 线程相关 ──
        private readonly ConcurrentQueue<short[]> _audioQueue = new ConcurrentQueue<short[]>();
        private volatile bool _running;
        private volatile bool _paused;
        private volatile bool _wakeDetectedThisSegment;
        private volatile bool _interruptRequested;
        private volatile bool _sleepRequested;
        private bool _modelLoaded;
        private bool _isInitializing;
        private float _lastWakeTime = -999f;

        // ── 清理标记（主线程延迟释放模型，识别器由后台线程自行释放） ──
        private volatile bool _taskExited;
        private Task _processingTask;

        private void Start()
        {
            if (autoStart)
                StartListening();
        }

        /// <summary>
        /// 运行时设置唤醒关键词
        /// </summary>
        public void SetKeywords(string[] keywords)
        {
            bool wasListening = IsListening;
            if (wasListening) StopListening();

            wakeKeywords = keywords;

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
        /// 设置模型文件名（从 CozeConfig 读取时调用）
        /// </summary>
        public void SetModelPath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            bool wasListening = IsListening;
            if (wasListening) StopListening();

            modelFileName = fileName;
            _modelLoaded = false;

            if (wasListening) StartListening();
        }

        /// <summary>
        /// 开始监听唤醒词
        /// </summary>
        public void StartListening()
        {
            if (IsListening || _isInitializing) return;

            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[VoskWake] 未检测到麦克风设备");
                return;
            }

            if (!_modelLoaded)
            {
                StartCoroutine(DoStartListening());
                return;
            }

            StartVoiceProcessor();
        }

        private IEnumerator DoStartListening()
        {
            _isInitializing = true;

            // 等待麦克风就绪
            while (Microphone.devices.Length <= 0)
                yield return null;

            // 解压模型（如需要）
            yield return DecompressModel();

            // 加载模型
            if (!_modelLoaded)
            {
                try
                {
                    string modelPath = ResolveModelDir(_decompressedModelPath);
                    Debug.Log($"[VoskWake] 加载模型: {modelPath}");
                    _model = new Model(modelPath);
                    _modelLoaded = true;
                    Debug.Log("[VoskWake] 模型加载成功");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[VoskWake] 模型加载失败: {e.Message}");
                    Debug.LogError($"[VoskWake] 请确保 StreamingAssets 中有正确的模型文件: {modelFileName}");
                    _isInitializing = false;
                    yield break;
                }
            }

            StartVoiceProcessor();
            _isInitializing = false;
        }

        /// <summary>
        /// 解压后的目录可能嵌套一层同名文件夹，此方法自动定位到包含 am/ conf/ 等子目录的实际模型路径
        /// </summary>
        private string ResolveModelDir(string basePath)
        {
            if (Directory.Exists(Path.Combine(basePath, "am")) ||
                Directory.Exists(Path.Combine(basePath, "conf")))
                return basePath;

            // 尝试下一级目录
            string dirName = Path.GetFileName(basePath);
            string nested = Path.Combine(basePath, dirName);
            if (Directory.Exists(nested) &&
                (Directory.Exists(Path.Combine(nested, "am")) ||
                 Directory.Exists(Path.Combine(nested, "conf"))))
                return nested;

            // 找不到标准结构，返回原始路径（交给 Vosk 报错）
            return basePath;
        }

        private void StartVoiceProcessor()
        {
            // 创建 VoiceProcessor 子组件
            var vpGo = new GameObject("[VoskWake_VoiceProcessor]");
            vpGo.transform.SetParent(transform);
            _voiceProcessor = vpGo.AddComponent<VoiceProcessor>();

            _voiceProcessor.OnFrameCaptured += OnFrameCaptured;
            _voiceProcessor.StartRecording(sampleRate, frameSize);

            _running = true;
            _wakeDetectedThisSegment = false;
            IsListening = true;

            // 启动后台识别线程
            _processingTask = Task.Run(ProcessAudio);

            Debug.Log($"[VoskWake] 开始监听，关键词: {string.Join(", ", wakeKeywords)}");
        }

        /// <summary>
        /// 停止监听
        /// </summary>
        public void StopListening()
        {
            _running = false;
            _paused = false;
            IsListening = false;
            IsPaused = false;

            if (_voiceProcessor != null)
            {
                _voiceProcessor.OnFrameCaptured -= OnFrameCaptured;
                if (_voiceProcessor.IsRecording)
                    _voiceProcessor.StopRecording();
                Destroy(_voiceProcessor.gameObject);
                _voiceProcessor = null;
            }

            // 清空音频缓冲
            while (_audioQueue.TryDequeue(out _)) { }
            // 识别器由后台线程退出时自行释放，此处不做 Dispose
        }

        /// <summary>
        /// 暂停监听（TTS 播报时调用，防止回声误触发）
        /// 彻底销毁 VoiceProcessor 以释放麦克风，供 ASR 等其他模块使用
        /// </summary>
        public void PauseListening()
        {
            if (!IsListening || IsPaused) return;
            _paused = true;

            // 销毁 VoiceProcessor，完全释放麦克风
            if (_voiceProcessor != null)
            {
                _voiceProcessor.OnFrameCaptured -= OnFrameCaptured;
                if (_voiceProcessor.IsRecording)
                    _voiceProcessor.StopRecording();
                Destroy(_voiceProcessor.gameObject);
                _voiceProcessor = null;
            }

            IsPaused = true;
            Debug.Log("[VoskWake] 暂停监听（TTS 播报中），麦克风已释放");
        }

        /// <summary>
        /// 恢复监听（TTS 播报结束后调用）
        /// 重建 VoiceProcessor 重新获取麦克风
        /// </summary>
        public void ResumeListening()
        {
            if (!IsPaused) return;

            _wakeDetectedThisSegment = false;
            _paused = false;

            // 重建 VoiceProcessor
            if (_voiceProcessor == null && Microphone.devices.Length > 0)
            {
                var vpGo = new GameObject("[VoskWake_VoiceProcessor]");
                vpGo.transform.SetParent(transform);
                _voiceProcessor = vpGo.AddComponent<VoiceProcessor>();
                _voiceProcessor.OnFrameCaptured += OnFrameCaptured;
                _voiceProcessor.StartRecording(sampleRate, frameSize);
            }

            IsPaused = false;
            Debug.Log("[VoskWake] 恢复监听，麦克风已重新获取");
        }

        // ── 模型解压 ──

        private IEnumerator DecompressModel()
        {
            string modelName = Path.GetFileNameWithoutExtension(modelFileName);
            _decompressedModelPath = Path.Combine(Application.persistentDataPath, modelName);

            // 已解压则跳过
            if (Directory.Exists(_decompressedModelPath))
            {
                Debug.Log($"[VoskWake] 使用已解压模型: {_decompressedModelPath}");
                yield break;
            }

            Debug.Log("[VoskWake] 正在解压模型文件...");

            string zipPath = Path.Combine(Application.streamingAssetsPath, modelFileName);
            Stream dataStream;

            if (zipPath.Contains("://"))
            {
                var www = UnityWebRequest.Get(zipPath);
                www.SendWebRequest();
                while (!www.isDone)
                    yield return null;

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[VoskWake] 读取模型 zip 失败: {www.error}");
                    yield break;
                }
                dataStream = new MemoryStream(www.downloadHandler.data);
            }
            else
            {
                if (!File.Exists(zipPath))
                {
                    Debug.LogError($"[VoskWake] 模型文件不存在: {zipPath}");
                    Debug.LogError("[VoskWake] 请下载中文模型 vosk-model-small-cn-0.22.zip 放入 StreamingAssets 目录");
                    yield break;
                }
                dataStream = File.OpenRead(zipPath);
            }

            bool extractDone = false;
            string extractError = null;
            try
            {
                var zipFile = ZipFile.Read(dataStream);
                zipFile.ExtractProgress += (sender, e) =>
                {
                    if (e.EventType == ZipProgressEventType.Extracting_AfterExtractAll)
                        extractDone = true;
                };
                zipFile.ExtractAll(Application.persistentDataPath);
                zipFile.Dispose();
            }
            catch (Exception e)
            {
                extractError = e.Message;
            }

            // yield 必须在 try-catch 外面（C# 限制）
            while (!extractDone && extractError == null)
                yield return null;

            if (extractError != null)
            {
                Debug.LogError($"[VoskWake] 解压模型失败: {extractError}");
                yield break;
            }

            dataStream.Dispose();

            Debug.Log("[VoskWake] 模型解压完成");
            yield return new WaitForSeconds(0.5f);
        }

        // ── 音频处理 ──

        private void OnFrameCaptured(short[] samples)
        {
            if (_running && !_paused)
                _audioQueue.Enqueue(samples);
        }

        private async Task ProcessAudio()
        {
            // 使用 grammar 模式：只识别唤醒词 + [unk]（其他语音归为 unk）
            // 这样 Vosk 不会因为词汇量不足而输出空结果
            string grammar = BuildGrammar();
            if (!string.IsNullOrEmpty(grammar))
            {
                try
                {
                    Debug.Log($"[VoskWake] Grammar 模式: {grammar}");
                    _recognizer = new VoskRecognizer(_model, (float)sampleRate, grammar);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VoskWake] Grammar 模式创建失败（部分关键词可能不在模型词典中），回退通用模式: {e.Message}");
                    _recognizer = new VoskRecognizer(_model, (float)sampleRate);
                }
            }
            else
            {
                Debug.Log("[VoskWake] 通用识别模式（未配置关键词）");
                _recognizer = new VoskRecognizer(_model, (float)sampleRate);
            }
            _recognizer.SetMaxAlternatives(1);
            _wakeDetectedThisSegment = false;

            Debug.Log("[VoskWake] 后台识别线程已启动");

            try
            {
                while (_running)
                {
                    // 暂停时丢弃音频帧，保持线程存活
                    if (_paused)
                    {
                        while (_audioQueue.TryDequeue(out _)) { }
                        await Task.Delay(100);
                        continue;
                    }

                    if (_audioQueue.TryDequeue(out short[] buffer))
                    {
                        bool sentenceEnd = _recognizer.AcceptWaveform(buffer, buffer.Length);

                        if (sentenceEnd)
                        {
                            string result = _recognizer.Result();
                            string text = ParseText(result);
                            Debug.Log($"[VoskWake] 最终结果: \"{text}\"");

                            if (!string.IsNullOrEmpty(text))
                            {
                                // 优先检查打断词（TTS 播报期间有效）
                                if (CheckInterruptKeywords(text))
                                    _interruptRequested = true;
                                // 其次检查休眠词
                                else if (CheckSleepKeywords(text))
                                    _sleepRequested = true;
                                else if (CheckKeywords(text))
                                    TriggerWakeWord(text);
                            }

                            // 句子结束后重置识别段
                            _wakeDetectedThisSegment = false;
                            _recognizer.Reset();
                        }
                        else
                        {
                            // 检查部分识别结果，实现更快的唤醒响应
                            string partial = _recognizer.PartialResult();
                            string text = ParseText(partial);

                            if (text != null && text.Length >= minPartialLength)
                            {
                                // 优先检查打断词（TTS 播报期间有效）
                                if (CheckInterruptKeywords(text))
                                {
                                    _interruptRequested = true;
                                }
                                // 其次检查休眠词
                                else if (CheckSleepKeywords(text))
                                {
                                    _sleepRequested = true;
                                }
                                else if (!_wakeDetectedThisSegment && CheckKeywords(text))
                                {
                                    TriggerWakeWord(text);
                                }
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(50);
                    }
                }
            }
            finally
            {
                // 后台线程退出时自行释放识别器（安全，无竞争）
                try
                {
                    _recognizer?.Reset();
                    _recognizer?.Dispose();
                }
                catch { }
                _recognizer = null;
                _taskExited = true;
            }
        }

        private void TriggerWakeWord(string text)
        {
            // 仅设置标记，由主线程 Update 负责触发事件（冷却检查）
            _wakeDetectedThisSegment = true;
        }

        // ── 主线程 Update ──

        private void Update()
        {
            // 后台线程退出后安全释放模型（识别器已由后台线程自行释放）
            if (_taskExited && !IsListening && !IsPaused && _model != null)
            {
                _taskExited = false;
                try
                {
                    _model.Dispose();
                    _model = null;
                    _modelLoaded = false;
                }
                catch { }
            }

            // 唤醒词事件在主线程触发（冷却检查）
            if (_wakeDetectedThisSegment && Time.time - _lastWakeTime > wakeCooldown)
            {
                _lastWakeTime = Time.time;
                _wakeDetectedThisSegment = false;
                Debug.Log("[VoskWake] 检测到唤醒词！");
                OnWakeWordDetected?.Invoke();
            }

            // 打断词事件在主线程触发（后台线程设置标记，主线程分发）
            if (_interruptRequested)
            {
                _interruptRequested = false;
                Debug.Log("[VoskWake] 检测到打断词！");
                OnInterruptDetected?.Invoke();
            }

            // 休眠词事件在主线程触发
            if (_sleepRequested)
            {
                _sleepRequested = false;
                Debug.Log("[VoskWake] 检测到休眠词！");
                OnSleepDetected?.Invoke();
            }
        }

        // ── 工具方法 ──

        /// <summary>
        /// 构建 Vosk grammar JSON: ["关键词1", "关键词2", "[unk]"]
        /// [unk] 让 Vosk 把非唤醒词的语音归类为 unk，避免输出空结果
        /// </summary>
        private string BuildGrammar()
        {
            bool hasWake = wakeKeywords != null && wakeKeywords.Length > 0;
            bool hasInterrupt = interruptKeywords != null && interruptKeywords.Length > 0;
            bool hasSleep = sleepKeywords != null && sleepKeywords.Length > 0;
            if (!hasWake && !hasInterrupt && !hasSleep)
                return null;

            var arr = new JSONArray();
            if (hasWake)
            {
                foreach (string kw in wakeKeywords)
                {
                    if (!string.IsNullOrEmpty(kw))
                        arr.Add(new JSONString(kw));
                }
            }
            if (hasInterrupt)
            {
                foreach (string kw in interruptKeywords)
                {
                    if (!string.IsNullOrEmpty(kw))
                        arr.Add(new JSONString(kw));
                }
            }
            if (hasSleep)
            {
                foreach (string kw in sleepKeywords)
                {
                    if (!string.IsNullOrEmpty(kw))
                        arr.Add(new JSONString(kw));
                }
            }
            arr.Add(new JSONString("[unk]"));

            return arr.ToString();
        }

        private bool CheckKeywords(string text)
        {
            if (string.IsNullOrEmpty(text) || wakeKeywords == null) return false;

            string normalized = text.Replace(" ", "").ToLower();
            foreach (string kw in wakeKeywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                string normalizedKw = kw.Replace(" ", "").ToLower();
                if (normalized.Contains(normalizedKw))
                {
                    Debug.Log($"[VoskWake] ★ 关键词匹配! 识别=\"{text}\" 匹配关键词=\"{kw}\"");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查识别文本是否包含打断关键词（TTS 播报期间用于触发打断）
        /// </summary>
        private bool CheckInterruptKeywords(string text)
        {
            if (string.IsNullOrEmpty(text) || interruptKeywords == null) return false;

            string normalized = text.Replace(" ", "").ToLower();
            foreach (string kw in interruptKeywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                string normalizedKw = kw.Replace(" ", "").ToLower();
                if (normalized.Contains(normalizedKw))
                {
                    Debug.Log($"[VoskWake] ✋ 打断词匹配! 识别=\"{text}\" 匹配=\"{kw}\"");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查识别文本是否包含休眠关键词（触发休眠，退出对话模式）
        /// </summary>
        private bool CheckSleepKeywords(string text)
        {
            if (string.IsNullOrEmpty(text) || sleepKeywords == null) return false;

            string normalized = text.Replace(" ", "").ToLower();
            foreach (string kw in sleepKeywords)
            {
                if (string.IsNullOrEmpty(kw)) continue;
                string normalizedKw = kw.Replace(" ", "").ToLower();
                if (normalized.Contains(normalizedKw))
                {
                    Debug.Log($"[VoskWake] 💤 休眠词匹配! 识别=\"{text}\" 匹配=\"{kw}\"");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 从 Vosk JSON 结果中提取 text 字段
        /// 如: {"text": "你好小童"} -> "你好小童"
        /// 如: {"partial": "你好"} -> "你好"
        /// </summary>
        private string ParseText(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length < 10) return null;
            try
            {
                var node = JSONNode.Parse(json);
                if (node == null || !node.IsObject) return null;

                // 优先取 "text"，再取 "partial"
                if (node.HasKey("text"))
                {
                    string text = node["text"].Value;
                    if (!string.IsNullOrEmpty(text))
                        return text.Trim();
                }

                if (node.HasKey("partial"))
                {
                    string partial = node["partial"].Value;
                    if (!string.IsNullOrEmpty(partial))
                        return partial.Trim();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void OnDestroy()
        {
            _running = false;
            _paused = false;

            if (_voiceProcessor != null)
            {
                _voiceProcessor.OnFrameCaptured -= OnFrameCaptured;
                if (_voiceProcessor.IsRecording)
                    _voiceProcessor.StopRecording();
                Destroy(_voiceProcessor.gameObject);
                _voiceProcessor = null;
            }

            // 识别器由后台线程 finally 块释放
            // 此处仅释放模型（后台线程不再引用模型）
            try
            {
                _model?.Dispose();
            }
            catch { }
            _model = null;
            _recognizer = null;  // 放弃引用，由后台线程 finally 负责 Dispose
        }
    }
}
