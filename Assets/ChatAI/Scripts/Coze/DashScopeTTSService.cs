using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

namespace ChatAI.Coze
{
    /// <summary>
    /// 阿里云 DashScope CosyVoice TTS 语音合成服务
    /// 将文字转换为语音并通过 AudioSource 播放
    /// API: POST https://dashscope.aliyuncs.com/api/v1/services/audio/tts/SpeechSynthesizer
    /// </summary>
    public class DashScopeTTSService : MonoBehaviour
    {
        [Header("配置")]
        [SerializeField] private string apiKey;
        [SerializeField] private string model = "cosyvoice-v2";
        [SerializeField, Tooltip("音色名称（v2模型需带_v2后缀）")]
        private string voice = "longxiaochun_v2";
        [SerializeField] private string apiUrl = "https://dashscope.aliyuncs.com/api/v1/services/audio/tts/SpeechSynthesizer";
        [SerializeField, Tooltip("请求超时（秒）")]
        private float timeout = 30f;

        [Header("播放")]
        [SerializeField] private AudioSource audioSource;

        // 事件
        public event Action OnPlaybackStarted;                // 整轮开始播放
        public event Action OnPlaybackFinished;               // 整轮播放完成
        public event Action<string> OnSentencePlaybackStarted; // 单个句子开始播放（传递句子文本）
        public event Action<string> OnError;                  // 错误

        // 状态
        public bool IsSpeaking { get; private set; }
        public bool IsSynthesizing { get; private set; }

        // 流式 TTS 队列
        private Queue<string> _sentenceQueue = new Queue<string>();
        private bool _isQueueActive;
        private bool _playbackStarted;        // 本轮是否已触发过 OnPlaybackStarted
        private bool _queueStopRequested;     // 外部请求停止
        private bool _stopped;                // StopSpeaking 后硬性阻止 QueueSpeak 重启队列

        // 预合成（Prefetch）：播放当前句时并行合成下一句
        private Coroutine _prefetchRoutine;
        private AudioClip _prefetchClip;
        private string _prefetchText;         // 预合成对应的文本（用于校验匹配）

        private void Start()
        {
            // 如果没有指定 AudioSource，自动创建一个
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f; // 2D 音频
            }
        }

        private void Update()
        {
            // 队列模式：当前音频片段播完后，交给 DrainQueueCoroutine 处理下一段或收尾
            if (_isQueueActive && audioSource != null && !audioSource.isPlaying)
            {
                if (IsSpeaking)
                    IsSpeaking = false;
                StartCoroutine(DrainQueueCoroutine());
            }
        }

        /// <summary>
        /// 合成并播放语音（会先停止之前的队列）
        /// </summary>
        /// <param name="text">要合成的文本</param>
        public void Speak(string text)
        {
            // 先停止任何正在进行的合成 / 播放 / 队列
            StopSpeaking();

            if (string.IsNullOrEmpty(text))
            {
                Debug.LogWarning("[DashScopeTTS] 文本为空");
                return;
            }
            if (string.IsNullOrEmpty(apiKey))
            {
                OnError?.Invoke("未配置 DashScope API Key");
                return;
            }

            _stopped = false;
            _sentenceQueue.Enqueue(text);
            _queueStopRequested = false;
            StartCoroutine(DrainQueueCoroutine());
        }

        /// <summary>
        /// 将一句话加入 TTS 队列（用于流式边出字边播报）
        /// 在 SSE 文字流输出时调用，每凑够一个完整句子就入队
        /// </summary>
        public void QueueSpeak(string sentence)
        {
            if (string.IsNullOrEmpty(sentence)) return;
            if (string.IsNullOrEmpty(apiKey))
            {
                OnError?.Invoke("未配置 DashScope API Key");
                return;
            }

            // 如果已被外部停止（如打断/休眠），忽略新的入队请求，防止队列重启
            if (_stopped)
            {
                Debug.Log($"[DashScopeTTS] 已停止，忽略入队: \"{sentence.Substring(0, Math.Min(sentence.Length, 20))}...\"");
                return;
            }

            _sentenceQueue.Enqueue(sentence);

            // 如果当前没有在跑队列，启动它
            if (!_isQueueActive)
            {
                StartCoroutine(DrainQueueCoroutine());
            }
        }

        /// <summary>
        /// 停止播放并清空队列
        /// </summary>
        public void StopSpeaking()
        {
            _stopped = true;
            _queueStopRequested = true;
            _sentenceQueue.Clear();
            _isQueueActive = false;
            _playbackStarted = false;
            _prefetchClip = null;
            _prefetchText = null;

            StopAllCoroutines();

            if (audioSource != null && audioSource.isPlaying)
                audioSource.Stop();

            IsSpeaking = false;
            IsSynthesizing = false;
        }

        /// <summary>
        /// 重置停止状态，允许新的 TTS 入队（新一轮对话开始时调用）
        /// </summary>
        public void ResetForNewTurn()
        {
            _stopped = false;
            _queueStopRequested = false;
        }

        private IEnumerator SynthesizeAndPlayCoroutine(string text)
        {
            IsSynthesizing = true;

            // 截断过长文本（CosyVoice 单次合成有长度限制）
            if (text.Length > 500)
            {
                Debug.LogWarning($"[DashScopeTTS] 文本过长 ({text.Length} 字)，截断为 500 字");
                text = text.Substring(0, 500);
            }

            // 第一步：请求合成
            string jsonBody = BuildRequestBody(text);
            string audioUrl = null;

            using (var request = new UnityWebRequest(apiUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)timeout;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = $"TTS 请求失败: HTTP {request.responseCode} {request.error}";
                    string body = request.downloadHandler?.text;
                    if (!string.IsNullOrEmpty(body))
                        error += $"\n{body}";
                    Debug.LogError($"[DashScopeTTS] {error}");
                    OnError?.Invoke(error);
                    IsSynthesizing = false;
                    yield break;
                }

                string responseText = request.downloadHandler.text;

                audioUrl = ExtractAudioUrl(responseText);
                // DashScope 返回的 URL 可能是 http://，Unity 默认禁止不安全连接，转为 https
                if (!string.IsNullOrEmpty(audioUrl) && audioUrl.StartsWith("http://"))
                    audioUrl = "https://" + audioUrl.Substring(7);
                if (string.IsNullOrEmpty(audioUrl))
                {
                    string error = $"无法从响应中解析音频 URL: {responseText}";
                    Debug.LogError($"[DashScopeTTS] {error}");
                    OnError?.Invoke(error);
                    IsSynthesizing = false;
                    yield break;
                }
            }

            // 第二步：下载音频文件
            using (var dlRequest = UnityWebRequestMultimedia.GetAudioClip(audioUrl, AudioType.MPEG))
            {
                dlRequest.timeout = (int)timeout;
                yield return dlRequest.SendWebRequest();

                if (dlRequest.result != UnityWebRequest.Result.Success)
                {
                    string error = $"音频下载失败: {dlRequest.error}";
                    Debug.LogError($"[DashScopeTTS] {error}");
                    OnError?.Invoke(error);
                }
                else
                {
                    var clip = DownloadHandlerAudioClip.GetContent(dlRequest);
                    if (clip != null)
                    {
                        PlayClip(clip);
                    }
                    else
                    {
                        // 尝试 WAV 格式
                        UnityEngine.Debug.LogWarning("[DashScopeTTS] MP3 解析为 null，尝试 WAV...");
                        using (var wavRequest = UnityWebRequestMultimedia.GetAudioClip(audioUrl, AudioType.WAV))
                        {
                            wavRequest.timeout = (int)timeout;
                            yield return wavRequest.SendWebRequest();

                            if (wavRequest.result == UnityWebRequest.Result.Success)
                            {
                                var wavClip = DownloadHandlerAudioClip.GetContent(wavRequest);
                                if (wavClip != null)
                                {
                                    PlayClip(wavClip);
                                }
                                else
                                {
                                    UnityEngine.Debug.LogError("[DashScopeTTS] WAV 也解析为 null");
                                    OnError?.Invoke("无法解析音频数据");
                                }
                            }
                            else
                            {
                                OnError?.Invoke($"音频下载失败: {wavRequest.error}");
                            }
                        }
                    }
                }
            }

            IsSynthesizing = false;
        }

        private void PlayClip(AudioClip clip)
        {
            if (audioSource == null || clip == null) return;

            // 停止当前播放
            if (audioSource.isPlaying)
                audioSource.Stop();

            audioSource.clip = clip;
            audioSource.Play();
            IsSpeaking = true;

            Debug.Log($"[DashScopeTTS] 开始播放 ({clip.length:F1}s)");
        }

        // ==================== 队列驱动协程 ====================

        /// <summary>
        /// 持续从队列中取出句子，逐句合成→下载→播放，直到队列清空或被外部停止。
        /// 优化：播放当前句时，并行预合成下一句，消除句间延迟。
        /// 注意：预合成使用 Peek 而非 Dequeue，避免在 SSE 流式入队期间取出顺序错乱。
        /// </summary>
        private IEnumerator DrainQueueCoroutine()
        {
            if (_isQueueActive) yield break; // 防止重复启动
            _isQueueActive = true;
            IsSynthesizing = true;

            while (!_queueStopRequested)
            {
                string text = null;
                AudioClip clip = null;

                if (_sentenceQueue.Count > 0)
                {
                    // 检查预合成结果是否与队列头部匹配（队列可能因 SSE 流式入队而变化）
                    if (_prefetchClip != null && _sentenceQueue.Peek() == _prefetchText)
                    {
                        text = _sentenceQueue.Dequeue();
                        clip = _prefetchClip;
                        _prefetchClip = null;
                        _prefetchText = null;
                        Debug.Log("[DashScopeTTS] 使用预合成音频");
                    }
                    else
                    {
                        // 预合成不匹配或不存在：丢弃旧预合成，同步合成当前句
                        if (_prefetchClip != null)
                        {
                            Debug.Log("[DashScopeTTS] 预合成文本不匹配，丢弃预合成结果，重新合成");
                            _prefetchClip = null;
                            _prefetchText = null;
                        }

                        text = _sentenceQueue.Dequeue();
                        yield return SynthesizeAndDownload(text, (c) => clip = c);
                    }
                }
                else
                {
                    // 队列为空：尝试等待预合成完成
                    if (_prefetchRoutine != null)
                    {
                        yield return _prefetchRoutine;
                        _prefetchRoutine = null;

                        if (_prefetchClip != null
                            && _sentenceQueue.Count > 0
                            && _sentenceQueue.Peek() == _prefetchText)
                        {
                            text = _sentenceQueue.Dequeue();
                            clip = _prefetchClip;
                            _prefetchClip = null;
                            _prefetchText = null;
                            Debug.Log("[DashScopeTTS] 使用预合成音频（等待后）");
                        }
                        else if (_prefetchClip != null)
                        {
                            // 预合成完成但队列内容已变化，丢弃
                            Debug.Log("[DashScopeTTS] 预合成文本不匹配（等待后），丢弃预合成结果");
                            _prefetchClip = null;
                            _prefetchText = null;
                        }
                    }

                    // 仍然没有可播放的内容，检查是否需要继续等待
                    if (clip == null)
                    {
                        if (audioSource != null && audioSource.isPlaying)
                        {
                            // 上一段还在播，等它播完再检查
                            while (audioSource != null && audioSource.isPlaying)
                                yield return null;
                            continue;
                        }
                        else
                        {
                            // 没有音频在播，也没有预合成，队列结束
                            break;
                        }
                    }
                }

                if (_queueStopRequested) break;

                if (clip != null)
                {
                    // 等上一段播完
                    while (audioSource != null && audioSource.isPlaying)
                        yield return null;

                    PlayClip(clip);

                    // 只在整轮第一个片段播放时触发 OnPlaybackStarted
                    if (!_playbackStarted)
                    {
                        _playbackStarted = true;
                        OnPlaybackStarted?.Invoke();
                    }

                    // 通知 UI 层：这个句子的语音开始播放了，可以显示文字
                    OnSentencePlaybackStarted?.Invoke(text);

                    // ===== 预合成下一句（Peek 不取出，保证队列顺序不变） =====
                    _prefetchClip = null;
                    _prefetchText = null;
                    if (_sentenceQueue.Count > 0 && !_queueStopRequested)
                    {
                        string nextText = _sentenceQueue.Peek();
                        _prefetchText = nextText;
                        _prefetchRoutine = StartCoroutine(
                            SynthesizeAndDownload(nextText, (c) => _prefetchClip = c));
                    }
                    else
                    {
                        _prefetchRoutine = null;
                    }
                }
            }

            // 清理预合成协程
            if (_prefetchRoutine != null)
            {
                StopCoroutine(_prefetchRoutine);
                _prefetchRoutine = null;
            }
            _prefetchClip = null;
            _prefetchText = null;

            // 等待最后一个片段播完
            while (audioSource != null && audioSource.isPlaying)
                yield return null;

            _isQueueActive = false;
            IsSynthesizing = false;

            if (_playbackStarted && !_queueStopRequested)
            {
                _playbackStarted = false;
                IsSpeaking = false;
                OnPlaybackFinished?.Invoke();
            }
            else if (!_playbackStarted && !_queueStopRequested)
            {
                UnityEngine.Debug.LogWarning("[DashScopeTTS] 队列结束但没有成功播放任何片段");
                OnPlaybackFinished?.Invoke();
            }
        }

        /// <summary>
        /// 对单句文本执行 TTS 合成 + 音频下载，通过回调返回 AudioClip
        /// </summary>
        private IEnumerator SynthesizeAndDownload(string text, Action<AudioClip> onResult)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 500)
            {
                if (text != null && text.Length > 500)
                    Debug.LogWarning($"[DashScopeTTS] 句子过长 ({text.Length} 字)，截断");
                onResult(null);
                yield break;
            }

            string jsonBody = BuildRequestBody(text);
            string audioUrl = null;

            using (var request = new UnityWebRequest(apiUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)timeout;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = $"TTS 请求失败: HTTP {request.responseCode} {request.error}";
                    string body = request.downloadHandler?.text;
                    if (!string.IsNullOrEmpty(body)) error += $"\n{body}";
                    Debug.LogError($"[DashScopeTTS] {error}");
                    OnError?.Invoke(error);
                    onResult(null);
                    yield break;
                }

                string responseText = request.downloadHandler.text;
                audioUrl = ExtractAudioUrl(responseText);
                if (!string.IsNullOrEmpty(audioUrl) && audioUrl.StartsWith("http://"))
                    audioUrl = "https://" + audioUrl.Substring(7);

                if (string.IsNullOrEmpty(audioUrl))
                {
                    Debug.LogError($"[DashScopeTTS] 无法解析音频 URL: {responseText}");
                    OnError?.Invoke("无法解析音频 URL");
                    onResult(null);
                    yield break;
                }
            }

            // 下载音频
            AudioClip resultClip = null;

            using (var dlRequest = UnityWebRequestMultimedia.GetAudioClip(audioUrl, AudioType.MPEG))
            {
                dlRequest.timeout = (int)timeout;
                yield return dlRequest.SendWebRequest();

                if (dlRequest.result == UnityWebRequest.Result.Success)
                {
                    resultClip = DownloadHandlerAudioClip.GetContent(dlRequest);
                }

                // MP3 失败则尝试 WAV
                if (resultClip == null)
                {
                    using (var wavRequest = UnityWebRequestMultimedia.GetAudioClip(audioUrl, AudioType.WAV))
                    {
                        wavRequest.timeout = (int)timeout;
                        yield return wavRequest.SendWebRequest();
                        if (wavRequest.result == UnityWebRequest.Result.Success)
                            resultClip = DownloadHandlerAudioClip.GetContent(wavRequest);
                    }
                }
            }

            if (resultClip == null)
                Debug.LogWarning($"[DashScopeTTS] 句子下载失败: \"{text}\"");

            onResult(resultClip);
        }

        /// <summary>
        /// 构建请求体 JSON
        /// </summary>
        private string BuildRequestBody(string text)
        {
            // 转义 JSON 特殊字符
            string escapedText = text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

            // CosyVoice API: voice 和 format 放在 input 内部
            return $@"{{
  ""model"": ""{model}"",
  ""input"": {{
    ""text"": ""{escapedText}"",
    ""voice"": ""{voice}"",
    ""format"": ""mp3""
  }}
}}";
        }

        /// <summary>
        /// 从响应 JSON 中提取音频 URL
        /// 响应格式: {"output":{"audio":{"url":"https://..."}}}
        /// </summary>
        private string ExtractAudioUrl(string json)
        {
            // 查找 "url" 字段值
            string pattern = "\"url\"";
            int idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;

            idx = json.IndexOf(':', idx + pattern.Length);
            if (idx < 0) return null;
            idx++;
            while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
            if (idx >= json.Length || json[idx] != '"') return null;

            idx++;
            var sb = new StringBuilder();
            while (idx < json.Length && json[idx] != '"')
            {
                if (json[idx] == '\\' && idx + 1 < json.Length)
                {
                    sb.Append(json[idx + 1]);
                    idx += 2;
                }
                else
                {
                    sb.Append(json[idx]);
                    idx++;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 运行时设置 API Key
        /// </summary>
        public void SetApiKey(string key)
        {
            apiKey = key;
        }

        /// <summary>
        /// 运行时设置音色
        /// </summary>
        public void SetVoice(string voiceName)
        {
            voice = voiceName;
        }
    }
}
