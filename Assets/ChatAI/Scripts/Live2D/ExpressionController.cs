using UnityEngine;
using ChatAI.Core;

namespace ChatAI.Live2D
{
    /// <summary>
    /// Live2D 表情控制器
    /// 根据对话状态和情绪切换数字人表情
    /// </summary>
    public class ExpressionController : MonoBehaviour
    {
        // 表情枚举
        public enum Expression
        {
            Neutral,    // 默认/中性
            Happy,      // 开心
            Thinking,   // 思考
            Surprised,  // 惊讶
            Speaking,   // 说话
            Listening   // 聆听
        }

        [Header("表情配置")]
        [SerializeField] private Expression currentExpression = Expression.Neutral;
        [SerializeField] private float expressionTransitionSpeed = 5f;

        // Live2D 模型引用（需要 Cubism SDK）
        // private CubismModel _model;

        private void Start()
        {
            // 订阅状态变更事件
            EventCenter.Instance?.Subscribe<StateChangedEvent>(OnStateChanged);

            // 初始化模型引用
            // _model = GetComponent<CubismModel>();

            SetExpression(Expression.Neutral);
        }

        /// <summary>
        /// 设置表情
        /// </summary>
        public void SetExpression(Expression expression)
        {
            currentExpression = expression;
            ApplyExpression(expression);
        }

        /// <summary>
        /// 根据状态切换表情
        /// </summary>
        private void OnStateChanged(StateChangedEvent evt)
        {
            switch (evt.NewState)
            {
                case DigitalHumanState.Idle:
                    SetExpression(Expression.Neutral);
                    break;
                case DigitalHumanState.Listening:
                    SetExpression(Expression.Listening);
                    break;
                case DigitalHumanState.Thinking:
                    SetExpression(Expression.Thinking);
                    break;
                case DigitalHumanState.Speaking:
                    SetExpression(Expression.Speaking);
                    break;
            }
        }

        /// <summary>
        /// 应用表情到 Live2D 模型
        /// </summary>
        private void ApplyExpression(Expression expression)
        {
            Debug.Log($"[Expression] 切换表情: {expression}");

            // Live2D Cubism SDK 集成代码
            // 取消注释以启用（需导入 SDK）
            /*
            if (_model == null) return;

            switch (expression)
            {
                case Expression.Neutral:
                    _model.SetParameterValue("ParamEyeLOpen", 1f);
                    _model.SetParameterValue("ParamEyeROpen", 1f);
                    _model.SetParameterValue("ParamMouthOpenY", 0f);
                    _model.SetParameterValue("ParamBrowLY", 0f);
                    _model.SetParameterValue("ParamBrowRY", 0f);
                    break;

                case Expression.Happy:
                    _model.SetParameterValue("ParamEyeLSmile", 1f);
                    _model.SetParameterValue("ParamEyeRSmile", 1f);
                    _model.SetParameterValue("ParamMouthForm", 1f);
                    break;

                case Expression.Thinking:
                    _model.SetParameterValue("ParamEyeLOpen", 0.6f);
                    _model.SetParameterValue("ParamEyeROpen", 1f);
                    _model.SetParameterValue("ParamBrowLY", 0.5f);
                    _model.SetParameterValue("ParamAngleZ", 10f);
                    break;

                case Expression.Surprised:
                    _model.SetParameterValue("ParamEyeLOpen", 1.2f);
                    _model.SetParameterValue("ParamEyeROpen", 1.2f);
                    _model.SetParameterValue("ParamMouthOpenY", 0.5f);
                    _model.SetParameterValue("ParamBrowLY", 0.8f);
                    _model.SetParameterValue("ParamBrowRY", 0.8f);
                    break;

                case Expression.Speaking:
                    _model.SetParameterValue("ParamEyeLSmile", 0.3f);
                    _model.SetParameterValue("ParamEyeRSmile", 0.3f);
                    break;

                case Expression.Listening:
                    _model.SetParameterValue("ParamEyeLOpen", 1f);
                    _model.SetParameterValue("ParamEyeROpen", 1f);
                    _model.SetParameterValue("ParamAngleZ", -5f);
                    break;
            }
            */
        }

        private void OnDestroy()
        {
            EventCenter.Instance?.Unsubscribe<StateChangedEvent>(OnStateChanged);
        }
    }
}
