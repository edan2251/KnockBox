using UnityEngine;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// 메인 화면 닉네임 입력칸(2026-11-21). UGUI InputField는 글자가 선택된 채 한글을 조합하면 선택 범위를 조합 길이만큼 어긋나게 계산해
    /// Append에서 ArgumentOutOfRangeException이 난다(고치기를 시작하면 기존 이름이 모두 선택되어 있어 바로 재현됨).
    /// 조합 중 글자가 들어오기 전에 선택된 글자를 먼저 지워(선택을 덮어쓰는 것과 같은 결과) 예외 없이 입력되게 한다.
    /// </summary>
    public sealed class NicknameInputField : InputField
    {
        protected override void Append(char input)
        {
            if (!string.IsNullOrEmpty(Input.compositionString) && m_CaretPosition != m_CaretSelectPosition)
            {
                int length = m_Text.Length;
                int from = Mathf.Clamp(Mathf.Min(m_CaretPosition, m_CaretSelectPosition), 0, length);
                int to = Mathf.Clamp(Mathf.Max(m_CaretPosition, m_CaretSelectPosition), 0, length);
                m_Text = m_Text.Remove(from, to - from);
                m_CaretPosition = m_CaretSelectPosition = from;
            }
            base.Append(input);
        }
    }
}
