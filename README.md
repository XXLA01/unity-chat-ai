# Unity AI 数字人语音对话系统

基于 Unity 开发的 AI 语音数字人项目，集成扣子（Coze）平台的大模型对话能力，配合 Live2D 虚拟形象、语音唤醒、实时语音识别（ASR）和语音合成（TTS），打造一个可自然交流的桌面端智能数字人——**小童**。

## 功能特性

- **语音唤醒**：说出"你好小童"即可激活数字人，进入对话模式
- **语音打断（Barge-in）**：数字人播报时可随时打断，立即开始新一轮对话
- **休眠 / 唤醒**：说"退下"等关键词进入休眠，需重新唤醒才恢复交互
- **流式 TTS 合成**：边生成边播放，预合成下一句，减少等待感
- **智能断句**：括号保护 + 标点断句，让合成语音更自然
- **Live2D 数字人**：口型同步 + 表情动画（需自备 Live2D 模型资源）
- **双唤醒引擎**：Windows 平台使用 `KeywordRecognizer`，其他平台回退到 Vosk 离线识别

## 技术栈

| 模块 | 技术方案 |
|------|----------|
| 引擎 | Unity 2022.3 LTS |
| AI 对话 | [Coze API](https://www.coze.com/) — Chat `/v3/chat` |
| 语音识别 (ASR) | [阿里云 DashScope](https://dashscope.aliyun.com/) — Paraformer 实时识别 |
| 语音合成 (TTS) | [阿里云 DashScope](https://dashscope.aliyun.com/) — CosyVoice 流式合成 |
| 唤醒词检测 | Windows `KeywordRecognizer` / [Vosk](https://alphacephei.com/vosk/) 离线识别 |
| 数字人形象 | Live2D Cubism SDK for Unity |
| 平台 | Windows 桌面端（x86_64） |

## 系统架构

```
┌─────────────────────────────────────────────────────────┐
│                    Unity 客户端                          │
│                                                         │
│  麦克风录音 ──▶ 唤醒词检测 ──▶ ASR 语音识别              │
│                     │                  │                 │
│                     ▼                  ▼                 │
│               对话模式管理    Coze Chat API (SSE)         │
│                                     │                    │
│                                     ▼                    │
│  扬声器 ◀── TTS 语音合成 ◀── 流式文本解析               │
│              (CosyVoice)      + 智能断句                 │
│                                                         │
│  Live2D 表现层：口型同步 / 表情动画 / UI 面板           │
└─────────────────────────────────────────────────────────┘
```

## 项目结构

```
Assets/
├── ChatAI/
│   ├── Scripts/
│   │   ├── Core/              # 核心模块
│   │   │   ├── CozeConfig.cs          # 全局配置（ScriptableObject）
│   │   │   ├── IWakeWordDetector.cs   # 唤醒检测接口
│   │   │   ├── WakeWordDetector.cs    # Windows KeywordRecognizer 实现
│   │   │   ├── VoskWakeWordDetector.cs# Vosk 离线唤醒实现
│   │   │   ├── EventCenter.cs         # 事件中心
│   │   │   └── GameManager.cs         # 游戏状态管理
│   │   ├── Coze/              # 服务层
│   │   │   ├── CozeChatService.cs     # Coze 对话 API
│   │   │   ├── CozeRealtimeService.cs # Coze WebSocket 实时通信
│   │   │   ├── DashScopeASRService.cs # DashScope 语音识别
│   │   │   └── DashScopeTTSService.cs # DashScope TTS 语音合成
│   │   ├── Audio/             # 音频模块
│   │   │   ├── AudioPlayer.cs         # 音频播放控制
│   │   │   └── MicrophoneController.cs# 麦克风控制
│   │   ├── Live2D/            # Live2D 表现
│   │   │   ├── ExpressionController.cs# 表情控制
│   │   │   └── LipSyncController.cs   # 口型同步
│   │   └── Debug/             # 调试工具
│   │       ├── DebugChatBootstrapper.cs# 服务引导 & 依赖注入
│   │       └── DebugChatUI.cs         # 调试 UI 主控
│   ├── Scenes/
│   │   └── DebugChatScene.unity       # 主调试场景
│   ├── Prefabs/
│   │   └── SystemMsg.prefab           # 系统消息预制体
│   └── Editor/
│       └── DebugSceneCreator.cs       # 编辑器工具
├── ThirdParty/                # 第三方依赖
│   ├── SimpleJson/                    # JSON 解析
│   └── Vosk/                          # Vosk 原生插件（C# 封装）
└── StreamingAssets/
    └── vosk-model-small-cn-0.22.zip   # Vosk 小型中文模型（42MB）
```

## 环境要求

- **Unity** 2022.3 LTS（推荐 2022.3.62f1 或更新版本）
- **操作系统** Windows 10/11 x86_64
- **网络** 需要联网访问 Coze API 和 DashScope API
- **麦克风** 用于语音唤醒和语音输入

## 快速开始

### 1. 克隆项目

```bash
git clone https://github.com/XXLA01/unity-chat-ai.git
```

### 2. 用 Unity Hub 打开

在 Unity Hub 中点击 **Open**，选择克隆下来的项目文件夹。首次打开 Unity 会自动导入依赖。

### 3. 配置 API 密钥

项目启动时需要一个 `CozeConfig` ScriptableObject 资源，包含所有配置项：

| 配置项 | 说明 |
|--------|------|
| Coze Token | Coze 平台 Personal Access Token |
| Bot ID | Coze 智能体 ID |
| DashScope API Key | 阿里云 DashScope API Key |
| 唤醒词 | 默认 `你好小童` |
| 打断关键词 | 如 `停`、`打断`、`别说了` |
| 休眠关键词 | 如 `退下`、`休眠`、`再见` |

> **注意**：`CozeConfig.asset` 已在 `.gitignore` 中排除，不会提交到仓库。首次运行前需在 Unity 中手动创建并填入你自己的密钥。

### 4. 配置 Coze 平台

1. 在 [Coze](https://www.coze.com/) 创建一个智能体（Bot），获取 Bot ID
2. 创建 Personal Access Token，填入 CozeConfig
3. 可选：为智能体添加知识库、插件和长期记忆

### 5. 配置 DashScope

1. 开通 [阿里云 DashScope](https://dashscope.console.aliyun.com/) 服务
2. 创建 API Key，填入 CozeConfig
3. 本项目使用 Paraformer（ASR）和 CosyVoice（TTS），均为按量付费

### 6. 运行场景

打开 `Assets/ChatAI/Scenes/DebugChatScene.unity`，点击 Unity 的 Play 按钮即可运行。

## 交互流程

```
用户说"你好小童"
       │
       ▼
  [唤醒词匹配成功] ──▶ 进入对话模式
       │
       ▼
  [录制用户语音] ──▶ [发送至 ASR]
       │
       ▼
  [Coze AI 生成回复] ──▶ [TTS 语音合成]
       │
       ▼
  [播放回复语音 + Live2D 口型同步]
       │
       ▼
  [等待下一次输入 / 超时退出 / 休眠]
```

在对话过程中：
- **打断**：随时说打断关键词，数字人会立即停止播报并开始听你说话
- **休眠**：说休眠关键词退出对话模式，需重新唤醒

## 关键设计

### TTS 流式队列

TTS 采用"边生成边播放"策略：文本流式到达后按句切分，当前句播放时预合成下一句，两句之间无缝衔接。打断或休眠时通过 `_stopped` 硬标志立即清空队列，新对话轮次通过 `ResetForNewTurn()` 重置状态。

### 双唤醒引擎

项目同时支持两套唤醒方案，通过预处理指令 `#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN` 自动选择：
- **Windows**：使用 `KeywordRecognizer`，低延迟、零依赖
- **其他平台**：使用 Vosk 离线模型，支持 Grammar 约束模式提高准确率

### 打断防回声

TTS 播放时麦克风会拾到合成语音，触发误唤醒。项目通过 1.5 秒的"反回声静默期"规避此问题——TTS 停止后短暂忽略唤醒事件。

## 已知限制

- Live2D 模型资源未包含在仓库中，需自行导入并配置
- 当前主要测试环境为 Windows，非 Windows 平台的 Vosk 路径未经充分验证
- Coze Realtime WebSocket 模式暂未在 UI 中暴露切换入口

## 参考文档

- [Coze API 文档](https://www.coze.com/docs/developer_guides/coze_api)
- [DashScope 语音识别](https://help.aliyun.com/document_detail/2712195.html)
- [DashScope 语音合成](https://help.aliyun.com/document_detail/2860747.html)
- [Vosk 离线识别](https://alphacephei.com/vosk/)
- [Live2D Cubism SDK](https://www.live2d.com/en/sdk/about/)

## 许可证

本项目仅供学习和参考使用。项目中引用的第三方库（Vosk、SimpleJSON 等）遵循其各自的开源许可证。
