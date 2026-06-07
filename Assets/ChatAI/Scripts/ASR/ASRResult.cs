using System;

namespace ChatAI.ASR
{
    /// <summary>
    /// ASR 结果数据结构
    /// </summary>
    [Serializable]
    public class ASRResult
    {
        public string Text;           // 识别文本
        public float Confidence;      // 置信度 (0~1)
        public bool IsFinal;          // 是否为最终结果（非中间结果）
        public float Duration;        // 音频时长（秒）
    }
}
