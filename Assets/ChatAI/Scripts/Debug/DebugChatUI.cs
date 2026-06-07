using UnityEngine;
using UnityEngine.UI;
using System;
using ChatAI.Core;
using ChatAI.Coze;

namespace ChatAI.DebugTools
{
    /// <summary>
    /// 调试用文字对话界面（支持文字 + 语音输入）
    /// 语音输入方案：录音 → DashScope ASR 识别为文字 → Coze Chat API 文字对话
    /// </summary>
    public class DebugChatUI : MonoBehaviour
    {
        // ==================== 序列化 UI 引用 ====================
        [Header("配置面板")]
        [SerializeField] private GameObject configPanel;
        [SerializeField] private InputField tokenInput;
        [SerializeField] private InputField botIdInput;
        [SerializeField] private InputField baseUrlInput;
        [SerializeField] private InputField asrKeyInput;
        [SerializeField] private Button connectBtn;
        [SerializeField] private Button collapseBtn;

        [Header("聊天区域")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform chatContent;

        [Header("输入区域")]
        [SerializeField] private InputField messageInput;
        [SerializeField] private Button sendBtn;
        [SerializeField] private Button clearBtn;

        [Header("语音控制")]
        [SerializeField] private Button voiceToggleBtn;
        [SerializeField] private Button micBtn;
        [SerializeField] private Button ttsToggleBtn;

        [Header("状态栏")]
        [SerializeField] private Text statusText;

        // ==================== 颜色 ====================
        private static readonly Color StatusIdle      = new Color(0.4f, 0.4f, 0.4f);
        private static readonly Color StatusListening = new Color(0.3f, 0.8f, 0.4f);
        private static readonly Color StatusThinking  = new Color(1.0f, 0.7f, 0.2f);
        private static readonly Color StatusRecording = new Color(1.0f, 0.3f, 0.3f);
        private static readonly Color StatusASR       = new Color(0.6f, 0.5f, 0.9f);
        private static readonly Color StatusError     = new Color(1.0f, 0.3f, 0.3f);
        private static readonly Color UserBubble      = new Color(0.10f, 0.40f, 0.78f);
        private static readonly Color AIBubble        = new Color(0.20f, 0.20f, 0.24f);
        private static readonly Color SystemBubble    = new Color(0.25f, 0.25f, 0.15f, 0.6f);
        private static readonly Color MicBtnActive    = new Color(0.85f, 0.20f, 0.20f);
        private static readonly Color MicBtnNormal    = new Color(0.15f, 0.50f, 0.85f);

        private const string DefaultBaseUrl = "https://api.coze.cn";

        private Font _font;
        private Text _currentAIText;
        private GameObject _currentAIBubble;
        private string _fullAIResponse = "";
        private bool _configCollapsed;

        // ==================== 语音状态 ====================
        private bool _voiceModeEnabled;
        private bool _isRecording;
        private AudioClip _recordingClip;
        private string _micDevice;
        private const int MicSampleRate = 16000;
        private const int MicMaxSeconds = 30;

        // 唤醒词状态
        private bool _wakeWordEnabled;
        private bool _wokenByKeyword;      // 本次录音是否由唤醒词触发
        private float _silenceTimer;        // 静音计时器
        private float _recordingGraceTimer; // 录音启动宽限期（给用户时间开口）
        private const float RecordingGracePeriod = 2.0f; // 唤醒后等待 2 秒再开始检测静音
        private const float SilenceTimeout = 2.0f; // 静音 2 秒自动停止录音
        private const float SilenceThreshold = 0.01f; // 音量阈值

        // 持续对话模式：唤醒一次后可连续对话，超时才休眠
        private bool _inConversationMode;
        private float _conversationIdleTimer;           // 对话空闲计时器
        private const float ConversationIdleTimeout = 10f; // 10 秒没说话自动退出对话模式

        // TTS 状态
        private bool _ttsEnabled;
        private System.Text.StringBuilder _ttsSentenceBuffer = new System.Text.StringBuilder();
        private bool _ttsStreamStarted;
        private int _ttsRevealedLength;   // 已随语音显示到 _fullAIResponse 的哪个位置

        // 事件委托引用（用于正确取消订阅）
        private Action<string> _onConversationCreated;

        // ==================== 初始化 ====================

        private void Start()
        {
            _font = GetChineseFont();

            if (connectBtn != null) connectBtn.onClick.AddListener(OnConnectClicked);
            if (collapseBtn != null) collapseBtn.onClick.AddListener(OnToggleConfig);
            if (sendBtn != null) sendBtn.onClick.AddListener(OnSendClicked);
            if (clearBtn != null) clearBtn.onClick.AddListener(OnClearClicked);
            if (messageInput != null) messageInput.onEndEdit.AddListener(OnInputEndEdit);
            if (voiceToggleBtn != null) voiceToggleBtn.onClick.AddListener(OnVoiceToggleClicked);
            if (ttsToggleBtn != null) ttsToggleBtn.onClick.AddListener(OnTTSToggleClicked);

            BindServiceEvents();

            // 获取麦克风设备
            if (Microphone.devices.Length > 0)
                _micDevice = Microphone.devices[0];

            // 初始禁用麦克风按钮
            if (micBtn != null) micBtn.interactable = false;

            // 检查配置是否已预设，自动连接
            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper != null && bootstrapper.Config != null && bootstrapper.Config.IsValid())
            {
                AutoConnect(bootstrapper);
            }
            else
            {
                SetStatus("就绪 - 请在配置面板输入 Token 和 Bot ID", StatusIdle);
                AddSystemMessage("未检测到预设配置。请在上方输入 Coze API Token 和 Bot ID，然后点击「连接」。");
            }
        }

        private void AutoConnect(DebugChatBootstrapper bootstrapper)
        {
            if (!_configCollapsed)
                OnToggleConfig();

            AddSystemMessage($"检测到预设配置 (Bot: {bootstrapper.Config.botId})，正在自动连接...");

            if (tokenInput != null) tokenInput.text = bootstrapper.Config.apiToken;
            if (botIdInput != null) botIdInput.text = bootstrapper.Config.botId;
            if (baseUrlInput != null) baseUrlInput.text = bootstrapper.Config.apiBaseUrl;
            if (asrKeyInput != null) asrKeyInput.text = bootstrapper.Config.dashScopeApiKey;

            bootstrapper.ChatService.CreateConversation();
            SetStatus("对话已连接", StatusListening);

            bool hasASR = !string.IsNullOrEmpty(bootstrapper.Config.dashScopeApiKey);
            AddSystemMessage(hasASR
                ? "已自动连接，可直接输入消息或开启语音模式。"
                : "已自动连接。如需语音功能，请在配置面板输入 DashScope API Key。");
        }

        private Font GetChineseFont()
        {
            Font f = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 14);
            if (f != null) return f;
            f = Font.CreateDynamicFontFromOSFont("Arial", 14);
            return f ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ==================== 服务事件绑定 ====================

        private void BindServiceEvents()
        {
            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper == null) return;

            // Chat 服务
            if (bootstrapper.ChatService != null)
            {
                var svc = bootstrapper.ChatService;
                svc.OnTextChunkReceived += OnTextChunk;
                svc.OnFullResponseReady += OnFullResponse;
                svc.OnError += OnServiceError;
                svc.OnConversationCreated += (_onConversationCreated = id => AddSystemMessage($"会话已创建: {id}"));
            }

            // ASR 服务
            if (bootstrapper.ASRService != null)
            {
                bootstrapper.ASRService.OnTranscriptionResult += OnASRResult;
                bootstrapper.ASRService.OnError += OnASRError;
            }

            // TTS 服务
            if (bootstrapper.TTSService != null)
            {
                bootstrapper.TTSService.OnPlaybackStarted += OnTTSPlaybackStarted;
                bootstrapper.TTSService.OnPlaybackFinished += OnTTSPlaybackFinished;
                bootstrapper.TTSService.OnSentencePlaybackStarted += OnTTSSentenceStarted;
                bootstrapper.TTSService.OnError += OnTTSError;
            }
        }

        // ==================== 文字连接 ====================

        private void OnConnectClicked()
        {
            string token = tokenInput != null ? tokenInput.text.Trim() : "";
            string botId = botIdInput != null ? botIdInput.text.Trim() : "";
            string baseUrl = baseUrlInput != null ? baseUrlInput.text.Trim() : "";
            string asrKey = asrKeyInput != null ? asrKeyInput.text.Trim() : "";

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(botId))
            {
                AddSystemMessage("[错误] 请输入 API Token 和 Bot ID");
                SetStatus("错误: 配置不完整", StatusError);
                return;
            }

            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper == null) return;

            bootstrapper.UpdateConfig(token, botId, string.IsNullOrEmpty(baseUrl) ? DefaultBaseUrl : baseUrl);
            if (!string.IsNullOrEmpty(asrKey))
                bootstrapper.UpdateASRConfig(asrKey);

            bootstrapper.ChatService.CreateConversation();
            SetStatus("对话已连接", StatusListening);
            AddSystemMessage($"已连接 Coze 对话 (Bot: {botId})");
        }

        // ==================== 语音模式切换 ====================

        private void OnVoiceToggleClicked()
        {
            _voiceModeEnabled = !_voiceModeEnabled;
            var btnText = voiceToggleBtn?.GetComponentInChildren<Text>();

            if (_voiceModeEnabled)
            {
                if (string.IsNullOrEmpty(_micDevice))
                {
                    AddSystemMessage("[错误] 未检测到麦克风设备，无法开启语音模式");
                    _voiceModeEnabled = false;
                    return;
                }

                var bootstrapper = DebugChatBootstrapper.Instance;
                if (bootstrapper == null || string.IsNullOrEmpty(bootstrapper.Config.dashScopeApiKey))
                {
                    AddSystemMessage("[错误] 请先在配置面板输入 DashScope API Key 以启用语音识别");
                    _voiceModeEnabled = false;
                    return;
                }

                if (micBtn != null) micBtn.interactable = true;
                if (btnText != null) btnText.text = "关闭语音";

                // 启动唤醒词检测
                StartWakeWordDetection(bootstrapper);

                SetStatus("语音模式已开启 - 说\"你好小童\"唤醒，或按住「说话」按钮录音", StatusListening);
                AddSystemMessage("语音模式已开启！你可以说「你好小童」唤醒，或按住「说话」按钮直接录音。");
            }
            else
            {
                if (micBtn != null) micBtn.interactable = false;
                if (btnText != null) btnText.text = "语音模式";
                SetStatus("就绪", StatusIdle);
                if (_isRecording) StopMicRecording();

                // 退出对话模式和唤醒词检测
                _inConversationMode = false;
                StopWakeWordDetection();
            }
        }

        // ==================== 唤醒词管理 ====================

        private void StartWakeWordDetection(DebugChatBootstrapper bootstrapper)
        {
            if (bootstrapper?.WakeWord == null) return;
            bootstrapper.WakeWord.OnWakeWordDetected += OnWakeWordDetected;
            bootstrapper.WakeWord.StartListening();
            _wakeWordEnabled = bootstrapper.WakeWord.IsListening;
            if (_wakeWordEnabled)
                AddSystemMessage("[唤醒词] 已启动，说「你好小童」开始对话。");
            else
                AddSystemMessage("[唤醒词] 启动失败，请确保系统已安装中文语音识别。仍可通过按钮录音。");
        }

        private void StopWakeWordDetection()
        {
            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper?.WakeWord != null)
            {
                bootstrapper.WakeWord.OnWakeWordDetected -= OnWakeWordDetected;
                bootstrapper.WakeWord.StopListening();
            }
            _wakeWordEnabled = false;
        }

        private void OnWakeWordDetected()
        {
            if (_isRecording) return;
            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper?.ChatService != null && bootstrapper.ChatService.IsRequesting) return;
            if (bootstrapper?.TTSService != null && bootstrapper.TTSService.IsSpeaking) return;

            UnityEngine.Debug.Log("[WakeWord] 唤醒！进入持续对话模式");
            AddSystemMessage("[唤醒] 你好小童！我在听，请说...");

            // 进入持续对话模式
            _inConversationMode = true;
            _conversationIdleTimer = 0f;

            _wokenByKeyword = true;
            _silenceTimer = 0f;
            _recordingGraceTimer = RecordingGracePeriod;

            // 暂停唤醒词检测，进入对话模式后不再需要唤醒词
            if (bootstrapper?.WakeWord != null)
                bootstrapper.WakeWord.PauseListening();

            StartMicRecording();
        }

        /// <summary>
        /// 退出持续对话模式，回到唤醒词监听
        /// </summary>
        private void ExitConversationMode()
        {
            _inConversationMode = false;
            AddSystemMessage("[对话模式] 已休眠，说「你好小童」重新唤醒。");
            SetStatus("语音模式 - 说\"你好小童\"唤醒", StatusListening);

            // 恢复唤醒词监听
            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper?.WakeWord != null && _wakeWordEnabled)
                bootstrapper.WakeWord.ResumeListening();
        }

        /// <summary>
        /// 持续对话模式：AI 回答完毕后自动开始新一轮录音
        /// </summary>
        private void AutoStartConversationRecording()
        {
            if (!_inConversationMode || _isRecording) return;
            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper?.ChatService != null && bootstrapper.ChatService.IsRequesting) return;

            _conversationIdleTimer = 0f; // 重置空闲计时器
            _wokenByKeyword = true;       // 使用静音自动停止
            _silenceTimer = 0f;
            _recordingGraceTimer = RecordingGracePeriod + 1f; // 多给 1 秒缓冲

            SetStatus("聆听中... 说话即可，沉默自动休眠", StatusListening);
            UnityEngine.Debug.Log("[对话模式] 自动开始新一轮录音");
            StartMicRecording();
        }

        // ==================== 语音录制 ====================

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Return) && messageInput != null && !messageInput.isFocused)
                messageInput.ActivateInputField();

            // 按钮按住录音（手动模式）
            if (_voiceModeEnabled && micBtn != null && micBtn.interactable)
            {
                var pointerOver = UnityEngine.EventSystems.EventSystem.current != null
                    && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()
                    && IsPointerOverUIElement(micBtn.gameObject);

                if (Input.GetMouseButtonDown(0) && pointerOver && !_isRecording)
                {
                    _wokenByKeyword = false; // 手动触发，不使用静音检测
                    StartMicRecording();
                }
                else if (Input.GetMouseButtonUp(0) && _isRecording && !_wokenByKeyword)
                    StopMicRecording();
            }

            // 唤醒词触发的录音：宽限期过后检测静音自动停止
            if (_isRecording && _wokenByKeyword && _recordingClip != null)
            {
                // 宽限期倒计时
                if (_recordingGraceTimer > 0f)
                {
                    _recordingGraceTimer -= Time.deltaTime;
                }
                else
                {
                    float level = GetMicLevel();
                    if (level < SilenceThreshold)
                    {
                        _silenceTimer += Time.deltaTime;
                        if (_silenceTimer >= SilenceTimeout)
                        {
                            UnityEngine.Debug.Log($"[WakeWord] 检测到 {_silenceTimer:F1}s 静音，自动停止录音");
                            StopMicRecording();
                        }
                    }
                    else
                    {
                        _silenceTimer = 0f;
                    }
                }

                // 超过最大录音时长也自动停止
                int pos = Microphone.GetPosition(_micDevice);
                if (pos >= MicSampleRate * (MicMaxSeconds - 1))
                {
                    UnityEngine.Debug.Log("[WakeWord] 达到最大录音时长，自动停止");
                    StopMicRecording();
                }
            }

            // 持续对话模式：空闲超时自动退出
            if (_inConversationMode && !_isRecording)
            {
                var bootstrapper = DebugChatBootstrapper.Instance;
                bool busy = (bootstrapper?.ChatService != null && bootstrapper.ChatService.IsRequesting)
                         || (bootstrapper?.TTSService != null && (bootstrapper.TTSService.IsSpeaking || bootstrapper.TTSService.IsSynthesizing));

                if (!busy)
                {
                    _conversationIdleTimer += Time.deltaTime;
                    if (_conversationIdleTimer >= ConversationIdleTimeout)
                    {
                        UnityEngine.Debug.Log($"[对话模式] {_conversationIdleTimer:F0}s 无交互，退出对话模式");
                        ExitConversationMode();
                    }
                }
            }
        }

        /// <summary>
        /// 获取当前麦克风音量级别 (0~1)
        /// </summary>
        private float GetMicLevel()
        {
            if (_recordingClip == null) return 0f;
            int pos = Microphone.GetPosition(_micDevice);
            if (pos <= 0) return 0f;

            int sampleCount = Mathf.Min(256, pos);
            float[] samples = new float[sampleCount];
            _recordingClip.GetData(samples, pos - sampleCount);

            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
                sum += samples[i] * samples[i];
            return Mathf.Sqrt(sum / samples.Length);
        }

        private bool IsPointerOverUIElement(GameObject target)
        {
            var rt = target.GetComponent<RectTransform>();
            if (rt == null || Camera.main == null) return false;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt, Input.mousePosition, null, out Vector2 localPoint);
            return rt.rect.Contains(localPoint);
        }

        private void StartMicRecording()
        {
            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper?.ChatService == null) return;
            if (bootstrapper.ChatService.IsRequesting)
            {
                AddSystemMessage("[提示] 上一条消息尚未处理完成");
                return;
            }

            _isRecording = true;
            _recordingClip = Microphone.Start(_micDevice, false, MicMaxSeconds, MicSampleRate);

            string recStatus = _inConversationMode ? "聆听中... 请说话" : "录音中... 松开停止";
            SetStatus(recStatus, StatusRecording);
            var btnText = micBtn?.GetComponentInChildren<Text>();
            if (btnText != null) btnText.text = "松开停止";
            var btnImg = micBtn?.GetComponent<Image>();
            if (btnImg != null) btnImg.color = MicBtnActive;
        }

        private void StopMicRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;

            int pos = Microphone.GetPosition(_micDevice);
            Microphone.End(_micDevice);

            var btnText = micBtn?.GetComponentInChildren<Text>();
            if (btnText != null) btnText.text = "说话";
            var btnImg = micBtn?.GetComponent<Image>();
            if (btnImg != null) btnImg.color = MicBtnNormal;

            if (pos <= 0 || _recordingClip == null)
            {
                SetStatus("录音数据为空", StatusIdle);
                return;
            }

            // 转换为 WAV
            byte[] wavData = CozeAudioHelper.AudioClipToWav(_recordingClip, pos, MicSampleRate);
            float duration = CozeAudioHelper.GetWavDuration(wavData, MicSampleRate);

            if (wavData == null || wavData.Length <= 44)
            {
                SetStatus("WAV 转换失败", StatusError);
                return;
            }

            UnityEngine.Debug.Log($"[Voice] 录音完成，时长: {duration:F1}s，WAV: {wavData.Length} bytes");
            SetStatus("语音识别中...", StatusASR);
            AddSystemMessage($"[语音] 录音 {duration:F1}s，正在识别...");

            // 发送到 ASR 服务
            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper?.ASRService != null)
            {
                bootstrapper.ASRService.Transcribe(wavData);
            }
        }

        // ==================== ASR 回调 ====================

        private void OnASRResult(string transcript)
        {
            if (string.IsNullOrEmpty(transcript))
            {
                if (_inConversationMode)
                {
                    // 对话模式中 ASR 为空 = 没说话，继续监听
                    UnityEngine.Debug.Log("[对话模式] ASR 为空，继续聆听...");
                    AutoStartConversationRecording();
                }
                else
                {
                    AddSystemMessage("[语音] 识别结果为空");
                    SetStatus("识别结果为空", StatusIdle);
                }
                return;
            }

            // 有实际内容，重置空闲计时器
            _conversationIdleTimer = 0f;

            // 显示识别结果并发送给 Coze Chat
            AddUserMessage($"[语音] {transcript}");
            SetStatus("AI 思考中...", StatusThinking);
            _fullAIResponse = "";
            _ttsSentenceBuffer.Clear();
            _ttsStreamStarted = false;
            _ttsRevealedLength = 0;

            // 停止上一轮 TTS 播报
            var tts = DebugChatBootstrapper.Instance?.TTSService;
            if (tts != null) tts.StopSpeaking();

            CreateAIBubble();

            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper?.ChatService != null)
            {
                bootstrapper.ChatService.SendMessage(transcript);
            }
        }

        private void OnASRError(string error)
        {
            AddSystemMessage($"[语音识别] 失败: {error}");
            SetStatus("语音识别失败", StatusError);
        }

        // ==================== TTS 语音播报 ====================

        private void OnTTSToggleClicked()
        {
            _ttsEnabled = !_ttsEnabled;
            var btnText = ttsToggleBtn?.GetComponentInChildren<Text>();

            if (_ttsEnabled)
            {
                var bootstrapper = DebugChatBootstrapper.Instance;
                if (bootstrapper == null || string.IsNullOrEmpty(bootstrapper.Config.dashScopeApiKey))
                {
                    AddSystemMessage("[错误] 请先在配置面板输入 DashScope API Key 以启用语音播报");
                    _ttsEnabled = false;
                    return;
                }

                if (btnText != null) btnText.text = "关闭播报";
                AddSystemMessage("语音播报已开启，AI 回复将自动朗读。");
            }
            else
            {
                if (btnText != null) btnText.text = "语音播报";

                var bootstrapper = DebugChatBootstrapper.Instance;
                if (bootstrapper?.TTSService != null)
                    bootstrapper.TTSService.StopSpeaking();

                _ttsSentenceBuffer.Clear();
                _ttsStreamStarted = false;

                // 关闭 TTS 时，立即显示所有尚未显示的文字
                RevealAllPendingText();

                AddSystemMessage("语音播报已关闭。");
            }
        }

        private void OnTTSPlaybackStarted()
        {
            SetStatus("AI 语音播报中...", StatusListening);

            // TTS 播报时暂停唤醒词检测，防止回声误触发
            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper?.WakeWord != null && _wakeWordEnabled)
                bootstrapper.WakeWord.PauseListening();
        }

        /// <summary>
        /// TTS 某个句子的语音开始播放 → 在 UI 中显示该句子的文字
        /// </summary>
        private void OnTTSSentenceStarted(string sentenceText)
        {
            if (_currentAIText == null) return;

            // 将 _fullAIResponse 中尚未显示的部分追加到气泡，最多显示到 _ttsRevealedLength
            int displayUpTo = Mathf.Min(_ttsRevealedLength, _fullAIResponse.Length);
            string displayText = _fullAIResponse.Substring(0, displayUpTo);
            _currentAIText.text = displayText;
            Canvas.ForceUpdateCanvases();
            ScrollToBottom();
        }

        private void OnTTSPlaybackFinished()
        {
            // 兜底：确保所有文字都已显示
            RevealAllPendingText();

            if (_inConversationMode)
            {
                // 持续对话模式：自动开始新一轮录音
                AutoStartConversationRecording();
            }
            else
            {
                string statusMsg = _voiceModeEnabled ? "语音模式 - 说\"你好小童\"唤醒" : "就绪";
                SetStatus(statusMsg, StatusListening);

                // 非对话模式：恢复唤醒词检测
                var bootstrapper = DebugChatBootstrapper.Instance;
                if (bootstrapper?.WakeWord != null && _wakeWordEnabled)
                    bootstrapper.WakeWord.ResumeListening();
            }
        }

        private void OnTTSError(string error)
        {
            AddSystemMessage($"[语音播报] 失败: {error}");
            // TTS 出错时，确保文字最终还是会被显示出来
            RevealAllPendingText();
        }

        /// <summary>
        /// 将尚未入队的剩余文字作为最后一个句子送入 TTS 队列
        /// </summary>
        private void FlushTTSSentenceBuffer()
        {
            // 将 _fullAIResponse 中尚未入队的部分作为最后一个句子
            if (_ttsRevealedLength < _fullAIResponse.Length)
            {
                string remaining = _fullAIResponse.Substring(_ttsRevealedLength);
                _ttsRevealedLength = _fullAIResponse.Length;

                string cleaned = CleanTextForTTS(remaining);
                if (!string.IsNullOrEmpty(cleaned))
                {
                    var tts = DebugChatBootstrapper.Instance?.TTSService;
                    if (tts != null)
                        tts.QueueSpeak(cleaned);
                }
            }
        }

        /// <summary>
        /// 将所有尚未显示的文字立即显示（用于 TTS 失败或关闭时的兜底）
        /// </summary>
        private void RevealAllPendingText()
        {
            if (_currentAIText != null && _fullAIResponse.Length > 0)
            {
                _currentAIText.text = _fullAIResponse;
                _ttsRevealedLength = _fullAIResponse.Length;
                Canvas.ForceUpdateCanvases();
                ScrollToBottom();
            }
        }

        /// <summary>
        /// 清理文本供 TTS 使用：去除括号内的动作描写、markdown 格式符号
        /// </summary>
        private string CleanTextForTTS(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // 去除中文括号内的内容（通常是动作/表情描写，不应朗读）
            text = System.Text.RegularExpressions.Regex.Replace(text, @"（[^）]*）", "");
            // 去除英文括号内的内容
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\([^)]*\)", "");
            // 去除 markdown 强调符号
            text = text.Replace("**", "").Replace("*", "").Replace("__", "").Replace("_", "");
            // 去除行首的 # 标题符号
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^#+\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);

            return text.Trim();
        }

        /// <summary>
        /// 判断文本中指定位置的字符是否在括号（中文或英文）内部
        /// 用于句子切分时避免在括号内的逗号处断句
        /// </summary>
        private bool IsInsideParentheses(string text, int index)
        {
            int cnDepth = 0;  // 中文括号嵌套深度
            int enDepth = 0;  // 英文括号嵌套深度
            for (int i = 0; i < index && i < text.Length; i++)
            {
                char c = text[i];
                if (c == '（') cnDepth++;
                else if (c == '）') cnDepth = System.Math.Max(0, cnDepth - 1);
                else if (c == '(') enDepth++;
                else if (c == ')') enDepth = System.Math.Max(0, enDepth - 1);
            }
            return cnDepth > 0 || enDepth > 0;
        }

        // ==================== 文字输入 ====================

        private void OnInputEndEdit(string text)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
                    OnSendClicked();
            }
        }

        private void OnSendClicked()
        {
            string msg = messageInput != null ? messageInput.text.Trim() : "";
            if (string.IsNullOrEmpty(msg)) return;

            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper?.ChatService == null) return;

            if (bootstrapper.ChatService.IsRequesting)
            {
                AddSystemMessage("[提示] 上一条消息尚未回复完成");
                return;
            }

            AddUserMessage(msg);
            messageInput.text = "";
            messageInput.ActivateInputField();

            SetStatus("思考中...", StatusThinking);
            _fullAIResponse = "";
            _ttsSentenceBuffer.Clear();
            _ttsStreamStarted = false;
            _ttsRevealedLength = 0;

            // 停止上一轮 TTS 播报
            var tts = DebugChatBootstrapper.Instance?.TTSService;
            if (tts != null) tts.StopSpeaking();

            CreateAIBubble();
            bootstrapper.ChatService.SendMessage(msg);
        }

        private void OnClearClicked()
        {
            if (chatContent == null) return;
            for (int i = chatContent.childCount - 1; i >= 0; i--)
                Destroy(chatContent.GetChild(i).gameObject);

            _currentAIText = null;
            _currentAIBubble = null;
            _fullAIResponse = "";
            _ttsRevealedLength = 0;
            AddSystemMessage("对话已清空。");
        }

        private void OnToggleConfig()
        {
            _configCollapsed = !_configCollapsed;
            if (configPanel == null) return;

            var le = configPanel.GetComponent<LayoutElement>();
            if (le != null) le.preferredHeight = _configCollapsed ? 30 : 190;

            for (int i = 1; i < configPanel.transform.childCount; i++)
                configPanel.transform.GetChild(i).gameObject.SetActive(!_configCollapsed);

            var btnText = collapseBtn?.GetComponentInChildren<Text>();
            if (btnText != null) btnText.text = _configCollapsed ? "展开 ▶" : "收起 ▼";
        }

        // ==================== 消息流式处理 ====================

        private void OnTextChunk(string chunk)
        {
            _fullAIResponse += chunk;

            if (_ttsEnabled)
            {
                // TTS 开启时：文字先缓存，不立即显示，等语音播到哪句再显示到哪句
                if (!_ttsStreamStarted)
                {
                    // SSE 流开始，确保 TTS 队列处于干净状态
                    var bootstrapper = DebugChatBootstrapper.Instance;
                    if (bootstrapper?.TTSService != null)
                        bootstrapper.TTSService.StopSpeaking();
                    _ttsSentenceBuffer.Clear();
                    _ttsStreamStarted = true;
                    SetStatus("AI 思考中，正在准备语音...", StatusThinking);
                }

                // 智能句子切分：优先在强标点处断句，文本较长时在逗号处次级断句
                // 注意：括号内的逗号不作为断句点（括号内容是动作描写，应整体跳过）
                char[] strongEnds = { '。', '！', '？', '；', '～' };
                char[] weakEnds = { '，', ',' };
                const int MinCharsBeforeWeakSplit = 12; // 至少积累 N 字后才启用逗号断句

                int unrevealedLen = _fullAIResponse.Length - _ttsRevealedLength;
                int splitPos = -1;

                // 第一轮：从末尾往前找强标点（强标点不受括号限制，括号内出现强标点很少见）
                for (int i = _fullAIResponse.Length - 1; i >= _ttsRevealedLength; i--)
                {
                    if (System.Array.IndexOf(strongEnds, _fullAIResponse[i]) >= 0)
                    {
                        splitPos = i;
                        break;
                    }
                }

                // 第二轮：没有强标点且文本够长，尝试逗号次级断句（跳过括号内的逗号）
                if (splitPos < 0 && unrevealedLen >= MinCharsBeforeWeakSplit)
                {
                    for (int i = _fullAIResponse.Length - 1; i >= _ttsRevealedLength; i--)
                    {
                        if (System.Array.IndexOf(weakEnds, _fullAIResponse[i]) >= 0
                            && !IsInsideParentheses(_fullAIResponse, i))
                        {
                            splitPos = i;
                            break;
                        }
                    }
                }

                if (splitPos >= 0)
                {
                    string sentence = _fullAIResponse.Substring(_ttsRevealedLength, splitPos - _ttsRevealedLength + 1);
                    string cleaned = CleanTextForTTS(sentence);
                    if (!string.IsNullOrEmpty(cleaned))
                    {
                        var tts = DebugChatBootstrapper.Instance?.TTSService;
                        if (tts != null)
                            tts.QueueSpeak(cleaned);
                    }
                    _ttsRevealedLength = splitPos + 1;
                }
                // 文字暂不显示，等 OnTTSSentenceStarted 回调时逐句显示
            }
            else
            {
                // TTS 未开启：立即显示文字
                if (_currentAIText != null)
                {
                    _currentAIText.text = _fullAIResponse;
                    Canvas.ForceUpdateCanvases();
                    ScrollToBottom();
                }
            }
        }

        private void OnFullResponse(string fullText)
        {
            // TTS 未启用时才立即显示全部文字（TTS 启用时由 OnTTSSentenceStarted 逐句显示）
            if (!_ttsEnabled && _currentAIText != null && string.IsNullOrEmpty(_currentAIText.text))
                _currentAIText.text = fullText;

            string statusMsg = _voiceModeEnabled ? "语音模式 - 说\"你好小童\"唤醒" : "就绪";
            SetStatus(statusMsg, StatusListening);
            UnityEngine.Debug.Log($"[DebugChat] AI 完整回复:\n{fullText}");

            // 流式 TTS：将尚未入队的剩余文字作为最后一个句子刷入
            if (_ttsEnabled)
            {
                FlushTTSSentenceBuffer();
                _ttsStreamStarted = false;
                // 唤醒词恢复会在 OnTTSPlaybackFinished 中处理
            }
            else if (_inConversationMode)
            {
                // TTS 未启用但在对话模式，自动开始新一轮录音
                AutoStartConversationRecording();
            }
            else if (_wakeWordEnabled)
            {
                // 非对话模式：恢复唤醒词监听
                var bootstrapper = DebugChatBootstrapper.Instance;
                if (bootstrapper?.WakeWord != null)
                    bootstrapper.WakeWord.ResumeListening();
            }
        }

        private void OnServiceError(string error)
        {
            SetStatus($"错误: {error}", StatusError);
            // 出错时立即显示所有已收到的文字
            RevealAllPendingText();
            if (_currentAIText != null && string.IsNullOrEmpty(_currentAIText.text))
                _currentAIText.text = $"[错误] {error}";
            else
                AddSystemMessage($"[错误] {error}");
        }

        // ==================== 消息气泡 ====================

        private void AddUserMessage(string text)
        {
            CreateBubble("UserMsg", text, UserBubble, Color.white, 14, true);
        }

        private void AddSystemMessage(string text)
        {
            CreateBubble("SystemMsg", text, SystemBubble, new Color(0.6f, 0.6f, 0.6f), 12, false);
        }

        private void CreateAIBubble()
        {
            _currentAIBubble = CreateBubble("AIResponse", "", AIBubble, Color.white, 14, false);
            _currentAIText = _currentAIBubble.GetComponentInChildren<Text>();
        }

        private GameObject CreateBubble(string objName, string text, Color bgColor, Color textColor,
                                         int fontSize, bool alignRight)
        {
            if (chatContent == null) return null;

            var row = new GameObject(objName, typeof(RectTransform));
            row.transform.SetParent(chatContent, false);
            var rowHlg = row.AddComponent<HorizontalLayoutGroup>();
            rowHlg.childControlWidth = true;
            rowHlg.childControlHeight = true;
            rowHlg.childForceExpandWidth = true;
            rowHlg.childForceExpandHeight = true;

            var bubbleGo = new GameObject("Bubble", typeof(RectTransform));
            bubbleGo.transform.SetParent(row.transform, false);
            bubbleGo.AddComponent<Image>().color = bgColor;
            var bubbleLe = bubbleGo.AddComponent<LayoutElement>();
            bubbleLe.flexibleWidth = 1;
            bubbleLe.preferredWidth = 600;
            bubbleLe.minWidth = 60;

            var bubbleVlg = bubbleGo.AddComponent<VerticalLayoutGroup>();
            bubbleVlg.childControlWidth = true;
            bubbleVlg.childControlHeight = true;
            bubbleVlg.childForceExpandWidth = true;
            bubbleVlg.childForceExpandHeight = true;
            bubbleVlg.padding = new RectOffset(12, 12, 8, 8);

            bubbleGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(bubbleGo.transform, false);
            var textComp = textGo.AddComponent<Text>();
            textComp.text = text;
            textComp.font = _font;
            textComp.fontSize = fontSize;
            textComp.color = textColor;
            textComp.alignment = alignRight ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            textComp.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComp.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            Canvas.ForceUpdateCanvases();
            ScrollToBottom();
            return bubbleGo;
        }

        // ==================== 工具 ====================

        private void ScrollToBottom()
        {
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 0f;
        }

        private void SetStatus(string text, Color color)
        {
            if (statusText != null)
            {
                statusText.text = $"  ● {text}";
                statusText.color = color;
            }
        }

        private void OnDestroy()
        {
            var bootstrapper = DebugChatBootstrapper.Instance;
            if (bootstrapper != null)
            {
                if (bootstrapper.ChatService != null)
                {
                    bootstrapper.ChatService.OnTextChunkReceived -= OnTextChunk;
                    bootstrapper.ChatService.OnFullResponseReady -= OnFullResponse;
                    bootstrapper.ChatService.OnError -= OnServiceError;
                    if (_onConversationCreated != null)
                        bootstrapper.ChatService.OnConversationCreated -= _onConversationCreated;
                }
                if (bootstrapper.ASRService != null)
                {
                    bootstrapper.ASRService.OnTranscriptionResult -= OnASRResult;
                    bootstrapper.ASRService.OnError -= OnASRError;
                }
                if (bootstrapper.TTSService != null)
                {
                    bootstrapper.TTSService.OnPlaybackStarted -= OnTTSPlaybackStarted;
                    bootstrapper.TTSService.OnPlaybackFinished -= OnTTSPlaybackFinished;
                    bootstrapper.TTSService.OnSentencePlaybackStarted -= OnTTSSentenceStarted;
                    bootstrapper.TTSService.OnError -= OnTTSError;
                }
                // 唤醒词清理
                if (bootstrapper.WakeWord != null)
                {
                    bootstrapper.WakeWord.OnWakeWordDetected -= OnWakeWordDetected;
                    bootstrapper.WakeWord.StopListening();
                }
            }
        }
    }
}
