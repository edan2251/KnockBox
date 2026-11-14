using System;
using System.Collections.Generic;
using UnityEngine;

namespace Knockbox.Game.Cards
{
    /// <summary>효과 줄의 의미. 긍정은 연두, 부정은 빨강, 중립은 흰색으로 그린다.</summary>
    public enum CardLinePolarity : byte { Neutral, Positive, Negative }

    /// <summary>스프라이트 아트가 없을 때 쓰는 기존 도형 조합 아이콘.</summary>
    public enum CardIconShape : byte { Burst, Shield, Arc }

    [Serializable]
    public struct CardEffectLine
    {
        public string text;
        public CardLinePolarity polarity;
    }

    /// <summary>카드 한 장의 표시 데이터. 시뮬레이션의 AugmentDef(판정·체크섬)와 카드 Id로만 연결한다.</summary>
    [Serializable]
    public struct CardFace
    {
        public int cardId;
        public string title;
        [Tooltip("줄바꿈(\\n)으로 나눈 설명. 자동 줄바꿈을 하지 않으므로 규칙 안에서 직접 나눈다.")]
        [TextArea(2, 4)] public string description;
        public CardEffectLine[] effects;
        public CardIconShape icon;
        [Tooltip("나중에 교체할 아이콘 아트. 비어 있으면 도형 조합 아이콘을 그린다.")]
        public Sprite iconSprite;
        [Tooltip("효과가 아직 없는 레이아웃 시험용 문구")]
        public bool placeholder;

        public string[] DescriptionLines() =>
            string.IsNullOrEmpty(description) ? Array.Empty<string>() : description.Replace("\r", "").Split('\n');
    }

    /// <summary>
    /// 모놀리스 표면이 읽는 카드 표시 목록. 아이콘·이름·설명·효과 줄을 담는다.
    /// PC 기준 최소 글자 크기에서 읽히도록 줄당 글자 수와 줄 수를 여기서 제한한다.
    /// </summary>
    [CreateAssetMenu(menuName = "Knockbox/Card Face Catalog", fileName = "CardFaceCatalog")]
    public sealed class CardFaceCatalog : ScriptableObject
    {
        public const int TitleMaxChars = 6;
        // 비석 카드 면(글자 크기 0.40유닛, 1080p에서 약 22px)의 패널 폭에 맞춘 한 줄 최대 글자 수.
        public const int LineMaxChars = 11;
        public const int DescriptionMaxLines = 3;
        public const int EffectMaxLines = 3;
        public const string PlaceholderMark = "임시";

        [SerializeField] CardFace[] faces = Array.Empty<CardFace>();
        public IReadOnlyList<CardFace> Faces => faces;

        public bool TryGet(int cardId, out CardFace face)
        {
            for (int i = 0; i < faces.Length; i++)
            {
                if (faces[i].cardId != cardId) continue;
                face = faces[i];
                return true;
            }
            face = default;
            return false;
        }

        /// <summary>글자 수는 한글·영문·기호를 모두 한 칸으로 센다(넓은 글자 기준의 보수적 제한).</summary>
        public static int Length(string text) => string.IsNullOrEmpty(text) ? 0 : text.Length;

        public List<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<int>();
            for (int i = 0; i < faces.Length; i++)
                errors.AddRange(Validate(faces[i], ids));
            return errors;
        }

        public static List<string> Validate(in CardFace face, HashSet<int> seenIds = null)
        {
            var errors = new List<string>();
            string who = $"카드 {face.cardId}";
            if (seenIds != null && !seenIds.Add(face.cardId)) errors.Add($"{who}: 카드 Id가 중복됩니다.");
            if (string.IsNullOrWhiteSpace(face.title)) errors.Add($"{who}: 이름이 비었습니다.");
            else if (Length(face.title) > TitleMaxChars || face.title.Contains("\n"))
                errors.Add($"{who}: 이름은 한 줄 {TitleMaxChars}자 이하입니다.");
            var lines = face.DescriptionLines();
            if (lines.Length > DescriptionMaxLines) errors.Add($"{who}: 설명은 {DescriptionMaxLines}줄 이하입니다.");
            foreach (var line in lines)
                if (Length(line) > LineMaxChars) errors.Add($"{who}: 설명 한 줄은 {LineMaxChars}자 이하입니다: {line}");
            int effects = face.effects?.Length ?? 0;
            if (effects > EffectMaxLines) errors.Add($"{who}: 효과 줄은 {EffectMaxLines}줄 이하입니다.");
            for (int i = 0; i < effects; i++)
            {
                string text = face.effects[i].text;
                if (string.IsNullOrWhiteSpace(text) || text.Contains("\n") || Length(text) > LineMaxChars)
                    errors.Add($"{who}: 효과 줄은 비지 않은 한 줄 {LineMaxChars}자 이하입니다: {text}");
            }
            if (face.placeholder && (face.description == null || !face.description.Contains(PlaceholderMark)))
                errors.Add($"{who}: 시험용 문구는 설명에 '{PlaceholderMark}'을 표시합니다.");
            return errors;
        }
    }
}
