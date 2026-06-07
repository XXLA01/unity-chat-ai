## AI 数字人语音对话项目规划

### 一、项目概述

本项目基于 Unity 2022.3 LTS 开发，面向 PC 桌面端，集成扣子（Coze）平台的 AI 对话能力，打造一个 2D Live2D 风格的智能语音数字人。用户通过语音唤醒词"你好，小童"激活数字人后，即可进行自然流畅的语音对话。

**核心技术栈：** Unity 2022.3 + Coze API + Live2D Cubism + WebSocket 实时通信

---

### 二、系统架构

```
┌─────────────────────────────────────────────────────────────────┐
│                        Unity 客户端                             │
│                                                                 │
│  ┌──────────┐   ┌──────────────┐   ┌──────────────────────┐    │
│  │  麦克风   │──▶│  音频处理层   │──▶│  Coze 对话服务层     │    │
│  │  录音模块 │   │ (唤醒/ASR)   │   │ (Chat API + TTS)     │    │
│  └──────────┘   └──────────────┘   └──────────┬───────────┘    │
│                                                │                │
│  ┌──────────┐   ┌──────────────┐   ┌──────────▼───────────┐    │
│  │  音频     │◀──│  TTS 语音    │◀──│  响应解析 & 状态管理  │    │
│  │  播放模块 │   │  合成/播放   │   │                      │    │
│  └──────────┘   └──────────────┘   └──────────────────────┘    │
│                                                │                │
│  ┌──────────────────────────────────────────────▼────────────┐  │
│  │                   UI & Live2D 表现层                      │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌─────────┐  │  │
│  │  │ Live2D   │  │ 口型同步 │  │ 表情动画 │  │ UI 面板 │  │  │
│  │  │ 模型渲染 │  │          │  │          │  │         │  │  │
│  │  └──────────┘  └──────────┘  └──────────┘  └─────────┘  │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
          │                              │
          ▼                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Coze 云端服务                             │
│  ┌──────────┐  ┌───────────────┐  ┌──────────────────────┐ │
│  │ Chat API │  │ Realtime API  │  │ Bot 智能体 (小童)    │ │
│  │ /v3/chat │  │ (WebSocket)   │  │ 知识库 / 插件 / 记忆 │ │
│  └──────────┘  └───────────────┘  └──────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

### 三、交互流程

```
用户说"你好，小童"
        │
        ▼
[麦克风持续监听] ──▶ [唤醒词检测] ──▶ 匹配成功
        │                                  │
        │                                  ▼
        │                    [进入对话模式 / 播放欢迎语]
        │                                  │
        │                                  ▼
        │                    [录制用户语音] ──▶ [发送至 Coze]
        │                                          │
        │                                          ▼
        │                    [Coze AI 生成回复文本]
        │                                          │
        │                                          ▼
        │                    [TTS 语音合成] ──▶ [播放回复语音]
        │                                          │
        │                                          ▼
        │                    [Live2D 口型同步 + 表情动画]
        │                                          │
        │                                          ▼
        └────────── [等待下一次语音输入 / 超时退出] ─┘
```

---

### 四、Coze 平台配置

#### 4.1 创建智能体 "小童"

在 coze.cn 平台创建 Bot 智能体，配置以下内容：

- **名称：** 小童
- **人设 Prompt：** 定义数字人的性格、说话风格、知识范围
- **知识库：** 上传业务相关文档，使数字人能回答专业问题
- **插件：** 按需接入天气、日程、搜索等能力插件
- **记忆：** 开启对话记忆，支持多轮上下文连贯对话
- **发布渠道：** 选择 API 方式发布，获取 Bot ID

#### 4.2 API 接入方案

推荐采用**双通道架构**，兼顾稳定性和实时性：

| 模块 | 方案 | 协议 | 说明 |
|------|------|------|------|
| 对话引擎 | Coze Chat API (`/v3/chat`) | HTTP SSE 流式 | 获取 AI 文本回复，支持多轮对话和插件调用 |
| 语音识别 (ASR) | 方案 A：火山引擎 ASR / 方案 B：阿里云 达摩院 | WebSocket | 将用户语音实时转为文字 |
| 语音合成 (TTS) | 方案 A：火山引擎 TTS / 方案 B：阿里云 CosyVoice | WebSocket/HTTP | 将 AI 回复文本转为自然语音 |
| 备选方案 | Coze Realtime API | WebSocket | 全链路语音对话（ASR+LLM+TTS 一体化），延迟最低 |

> **推荐方案：** 优先使用 Coze Chat API + 火山引擎语音服务（与 Coze 同属字节生态，兼容性好）。如果对延迟要求极高，可后续升级为 Coze Realtime API 方案。

#### 4.3 API 密钥管理

在 Unity 中通过 `ScriptableObject` 管理敏感配置，不硬编码在代码中：

```csharp
[CreateAssetMenu(fileName = "CozeConfig", menuName = "ChatAI/Coze Config")]
public class CozeConfig : ScriptableObject
{
    public string apiToken;        // Coze API Token（Personal Access Token）
    public string botId;           // 智能体 Bot ID
    public string asrApiKey;       // 语音识别 API Key
    public string ttsApiKey;       // 语音合成 API Key
    public string apiBaseUrl = "https://api.coze.cn";  // API 基础地址
}
```

---

### 五、核心模块设计

#### 5.1 音频采集与唤醒词检测模块

**职责：** 持续监听麦克风输入，检测唤醒词"你好，小童"。

**技术要点：**

- 使用 Unity `Microphone.Start()` 进行持续录音
- 音频采样率建议 16kHz（ASR 标准输入）
- 唤醒词检测方案（三选一）：

| 方案 | 说明 | 优缺点 |
|------|------|--------|
| **本地轻量 ASR** | 使用 Vosk / PocketSphinx 本地小模型做唤醒词 | 离线可用，但需打包模型文件 |
| **云端 ASR 持续流** | 将音频流持续发送至 ASR 服务，检测文本中是否包含唤醒词 | 准确率高，但消耗 API 额度 |
| **VAD + 关键词** | 先用 VAD（Voice Activity Detection）检测语音活动，再触发 ASR | 节省资源，推荐方案 |

**推荐实现：** VAD 检测语音活动 → 本地轻量 ASR 或云端 ASR 识别短语音 → 匹配"你好，小童"

#### 5.2 Coze 对话服务模块

**职责：** 管理与 Coze 平台的通信，处理对话请求和响应。

**核心接口：**

```
POST https://api.coze.cn/v3/chat
Headers: Authorization: Bearer {api_token}
Body: {
    "bot_id": "{bot_id}",
    "user_id": "{user_id}",
    "conversation_id": "{conversation_id}",  // 多轮对话上下文
    "additional_messages": [{
        "role": "user",
        "content": "{用户语音转写的文本}",
        "content_type": "text"
    }],
    "stream": true  // SSE 流式输出
}
```

**关键特性：**
- SSE（Server-Sent Events）流式接收 AI 回复，支持逐字输出效果
- `conversation_id` 维护多轮对话上下文
- 支持 Bot 插件回调（如查询天气、操作日历等）
- 错误重试和超时机制

#### 5.3 语音合成 (TTS) 模块

**职责：** 将 AI 回复文本转换为自然语音并播放。

**流程：**
1. 接收 Coze 流式返回的文本，按句子分段
2. 逐段发送 TTS 请求，获取音频数据
3. 音频数据入队列，顺序播放
4. 播放时驱动 Live2D 口型同步

**TTS 参数配置：**
- 采样率：24kHz（高质量）或 16kHz（省带宽）
- 音色：选择温暖亲切的女声/男声
- 语速：1.0（正常）
- 格式：PCM / MP3

#### 5.4 Live2D 表现模块

**职责：** 渲染数字人 2D 形象，实现口型同步和表情动画。

**技术选型：** Live2D Cubism SDK for Unity

**核心功能：**

| 功能 | 实现方式 |
|------|----------|
| 模型渲染 | Live2D Cubism SDK，支持高分辨率 2D 模型 |
| 口型同步 | 根据 TTS 音频振幅实时驱动嘴型参数 `ParamMouthOpenY` |
| 表情动画 | 根据对话情绪切换表情（开心、思考、惊讶等） |
| 待机动画 | 眨眼、微微摇头等自然待机动作 |
| 状态反馈 | 唤醒时睁大眼睛、听音时侧耳等交互反馈 |

**Live2D 口型同步原理：**
```csharp
// 从音频数据提取振幅
float amplitude = GetAudioAmplitude(audioClip, currentTime);
// 映射到 Live2D 口型参数 (0~1)
float mouthOpen = Mathf.Clamp01(amplitude * sensitivity);
// 设置到模型参数
model.SetParameterValue("ParamMouthOpenY", mouthOpen);
```

#### 5.5 UI 交互模块

**职责：** 提供用户操作界面和状态反馈。

**UI 元素规划：**

- **主界面：** Live2D 数字人全屏展示 + 底部半透明对话气泡
- **对话气泡：** 显示 AI 回复的实时文字（逐字打印效果）
- **状态指示器：** 待机 / 聆听中 / 思考中 / 回复中 四种状态
- **设置面板：** 音量调节、音色选择、对话历史、唤醒词灵敏度
- **历史记录：** 可折叠的对话历史面板，支持文本回看

---

### 六、项目目录结构

```
Assets/
├── ChatAI/
│   ├── Scripts/
│   │   ├── Core/                    # 核心框架
│   │   │   ├── GameManager.cs           # 全局状态管理（单例）
│   │   │   ├── EventCenter.cs           # 事件中心（发布/订阅）
│   │   │   └── ServiceLocator.cs        # 服务定位器
│   │   │
│   │   ├── Audio/                   # 音频模块
│   │   │   ├── MicrophoneController.cs  # 麦克风录音控制
│   │   │   ├── AudioProcessor.cs        # 音频处理（VAD、振幅提取）
│   │   │   ├── AudioClipRecorder.cs     # 录音片段管理
│   │   │   └── AudioPlayer.cs           # 音频播放（支持队列播放）
│   │   │
│   │   ├── WakeWord/                # 唤醒词模块
│   │   │   ├── WakeWordDetector.cs      # 唤醒词检测器
│   │   │   └── WakeWordConfig.cs        # 唤醒词配置
│   │   │
│   │   ├── ASR/                     # 语音识别模块
│   │   │   ├── IASRService.cs           # ASR 接口定义
│   │   │   ├── VolcanoASRService.cs     # 火山引擎 ASR 实现
│   │   │   └── ASRResult.cs             # 识别结果数据结构
│   │   │
│   │   ├── TTS/                     # 语音合成模块
│   │   │   ├── ITTSService.cs           # TTS 接口定义
│   │   │   ├── VolcanoTTSService.cs     # 火山引擎 TTS 实现
│   │   │   ├── CosyVoiceTTSService.cs   # 阿里云 CosyVoice 实现（备选）
│   │   │   └── TTSQueue.cs              # TTS 音频队列管理
│   │   │
│   │   ├── Coze/                    # Coze 对话模块
│   │   │   ├── CozeChatService.cs       # Coze Chat API 服务
│   │   │   ├── CozeRealtimeService.cs   # Coze Realtime API 服务（备选）
│   │   │   ├── CozeModels.cs            # API 数据模型
│   │   │   └── SSEParser.cs             # SSE 流式解析器
│   │   │
│   │   ├── Live2D/                  # Live2D 表现模块
│   │   │   ├── Live2DController.cs      # Live2D 模型控制
│   │   │   ├── LipSyncController.cs     # 口型同步控制
│   │   │   ├── ExpressionController.cs  # 表情控制
│   │   │   └── MotionController.cs      # 动作控制
│   │   │
│   │   ├── UI/                      # UI 模块
│   │   │   ├── DialogBubble.cs          # 对话气泡（逐字打印）
│   │   │   ├── StatusIndicator.cs       # 状态指示器
│   │   │   ├── SettingsPanel.cs         # 设置面板
│   │   │   └── HistoryPanel.cs          # 对话历史
│   │   │
│   │   └── Utils/                   # 工具类
│   │       ├── ConfigManager.cs         # 配置管理
│   │       ├── NetworkHelper.cs         # 网络工具
│   │       └── Logger.cs               # 日志工具
│   │
│   ├── Configs/                     # 配置文件
│   │   ├── CozeConfig.asset             # Coze API 配置（ScriptableObject）
│   │   ├── AudioConfig.asset            # 音频参数配置
│   │   └── Live2DConfig.asset           # Live2D 模型配置
│   │
│   ├── Resources/                   # 运行时资源
│   │   ├── Audio/                       # 音频素材（唤醒提示音等）
│   │   ├── Live2D/                      # Live2D 模型文件
│   │   └── UI/                          # UI 素材
│   │
│   ├── Prefabs/                     # 预制体
│   │   ├── DigitalHuman.prefab          # 数字人主预制体
│   │   └── UI/                          # UI 预制体
│   │
│   └── Scenes/                      # 场景
│       ├── MainScene.unity              # 主场景
│       └── BootScene.unity              # 启动/初始化场景
│
├── ThirdParty/                      # 第三方库
│   ├── WebSocketSharp/                  # WebSocket 客户端库
│   ├── Live2DCubism/                    # Live2D Cubism SDK
│   └── Newtonsoft.Json/                 # JSON 序列化库
│
└── Plugins/                         # 原生插件（如有）
```

---

### 七、关键技术难点与解决方案

#### 7.1 语音对话延迟优化

**问题：** 从用户说话到 AI 回复播放，整体延迟可能超过 3 秒，影响体验。

**解决方案：**

- **流式处理管线：** ASR 识别结果 → 立即发送 Coze Chat → SSE 流式接收 → 按句分段 → 逐段 TTS 合成 → 逐段播放
- **TTS 预加载：** 当第一句 TTS 音频正在播放时，第二句已在合成中
- **音频缓冲队列：** 避免音频间断，保证流畅播放
- **打断机制：** 用户随时可以说话打断 AI 回复，立即停止当前播放

**预期延迟指标：**
| 环节 | 目标延迟 |
|------|---------|
| 唤醒词检测 | < 500ms |
| ASR 语音识别 | < 800ms |
| Coze AI 首 Token | < 1000ms |
| TTS 首段合成 | < 800ms |
| **端到端首字发声** | **< 2.5s** |

#### 7.2 Live2D 口型同步

**问题：** TTS 语音播放时需要与 Live2D 模型嘴型实时同步。

**解决方案：**

```
TTS 音频播放
    │
    ├──▶ 音频振幅分析（每帧采样）
    │         │
    │         ▼
    │    振幅值 → 口型参数映射
    │         │
    │         ▼
    └──▶ Live2D ParamMouthOpenY 实时更新
```

采用音频振幅驱动方式，不需要额外的 Viseme 映射，实现简单且效果自然。

#### 7.3 唤醒词误触发

**问题：** 环境噪音或日常对话可能误触发唤醒词。

**解决方案：**
- 设置置信度阈值（> 0.8 才触发）
- 加入确认机制：检测到唤醒词后播放"你好！"提示音，确认进入对话模式
- 对话超时自动退出（默认 30 秒无语音输入后回到待机）
- 可配置唤醒词灵敏度

#### 7.4 Unity WebSocket 连接管理

**问题：** WebSocket 连接可能因网络波动断开。

**解决方案：**
- 实现自动重连机制（指数退避策略）
- 心跳包保活（每 30 秒发送 ping）
- 连接状态可视化反馈
- 离线时的降级处理（提示用户网络异常）

---

### 八、开发阶段规划

#### Phase 1：基础框架搭建（第 1~2 周）

| 任务 | 产出 |
|------|------|
| 创建项目目录结构和基础框架 | 完整目录树 |
| 搭建 GameManager 状态机 | 待机/聆听/思考/回复 四态切换 |
| 实现麦克风录音模块 | MicrophoneController |
| 实现事件中心 | EventCenter 发布/订阅系统 |
| 集成 Newtonsoft.Json | JSON 序列化工具 |
| 集成 WebSocket 库 | WebSocketSharp 或 NativeWebSocket |

#### Phase 2：Coze 对话集成（第 3~4 周）

| 任务 | 产出 |
|------|------|
| 在 Coze 平台创建并配置"小童"智能体 | Bot ID + API Token |
| 实现 Coze Chat API 对接（SSE 流式） | CozeChatService |
| 实现 SSE 解析器 | SSEParser |
| 多轮对话上下文管理 | ConversationManager |
| 文本对话 UI（调试用） | 简易聊天界面 |

#### Phase 3：语音能力集成（第 5~6 周）

| 任务 | 产出 |
|------|------|
| 接入 ASR 语音识别服务 | ASRService 实现 |
| 实现唤醒词检测 | WakeWordDetector |
| 接入 TTS 语音合成服务 | TTSService 实现 |
| 实现 TTS 音频队列播放 | TTSQueue + AudioPlayer |
| 端到端语音对话联调 | 语音输入 → AI 回复 → 语音输出 |
| 实现打断机制 | 用户随时打断 AI 说话 |

#### Phase 4：Live2D 数字人表现（第 7~8 周）

| 任务 | 产出 |
|------|------|
| 集成 Live2D Cubism SDK | SDK 导入与配置 |
| 导入 Live2D 模型并设置渲染 | Live2DController |
| 实现口型同步 | LipSyncController |
| 实现表情动画系统 | ExpressionController |
| 实现待机动画（眨眼、微动） | MotionController |
| 语音与动画联调 | 完整的数字人对话体验 |

#### Phase 5：UI 打磨与优化（第 9~10 周）

| 任务 | 产出 |
|------|------|
| 设计并实现完整 UI | 对话气泡、状态指示、设置面板 |
| 逐字打印效果 | DialogBubble |
| 对话历史面板 | HistoryPanel |
| 性能优化 | 内存管理、GC 优化、音频缓冲池 |
| 错误处理和边界情况 | 网络异常、API 限流、超时处理 |

#### Phase 6：测试与发布（第 11~12 周）

| 任务 | 产出 |
|------|------|
| 功能测试 | 全流程测试用例 |
| 用户体验优化 | 根据测试反馈调优 |
| 打包发布 | Windows 可执行文件 |
| 项目文档 | 使用手册和部署说明 |

---

### 九、第三方依赖清单

| 依赖 | 版本 | 用途 | 获取方式 |
|------|------|------|----------|
| Live2D Cubism SDK for Unity | 5.x | 2D 数字人模型渲染和动画 | [Live2D 官网](https://www.live2d.com/) |
| Newtonsoft.Json | 3.0+ | JSON 序列化/反序列化 | Unity Package Manager |
| WebSocketSharp 或 NativeWebSocket | latest | WebSocket 客户端通信 | GitHub / OpenUPM |
| TextMeshPro | 3.0.7 (已安装) | 高质量文本渲染 | 已安装 |

---

### 十、Coze 平台准备工作清单

在开始编码之前，需要在 Coze 平台完成以下准备：

1. **注册/登录** coze.cn 账号
2. **创建 Bot 智能体"小童"**：配置人设、知识库、插件
3. **发布 Bot**：选择 API 渠道发布，记录 Bot ID
4. **获取 API Token**：在"个人设置 → API Token"页面生成 Personal Access Token
5. **配置语音服务**（如使用火山引擎）：注册火山引擎账号，开通 ASR 和 TTS 服务，获取 API Key
6. **测试 API 连通性**：用 curl 或 Postman 先验证 Chat API 可正常调用

---

### 十一、风险与备选方案

| 风险项 | 影响 | 备选方案 |
|--------|------|----------|
| Coze API 响应延迟高 | 对话体验差 | 切换到 Coze Realtime API 或增加本地缓存回复 |
| ASR 识别准确率低 | 唤醒和对话失败 | 切换 ASR 引擎（讯飞/Azure），或增加降噪处理 |
| Live2D 模型资源不足 | 数字人表现力差 | 使用 Live2D 官方示例模型或购买商用模型 |
| API 额度限制 | 开发测试受阻 | 申请开发者额度，或使用 Mock 数据本地开发 |
| 网络不稳定 | 对话中断 | 实现离线降级模式（预设回复 + 本地 TTS） |
