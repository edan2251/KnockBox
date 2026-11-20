using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.UI;
using static Knockbox.Game.UI.FrontendWidgets;
using static Knockbox.Game.UI.FrontendMotion;

namespace Knockbox.Game.UI
{
    /// <summary>확정된 KO 결과를 점수판으로 연결한다. 시뮬레이션 점수/위치는 수정하지 않는다.</summary>
    [System.Serializable]
    public sealed class FrontendScoreView
    {
        [SerializeField] RectTransform root;
        [SerializeField] Image[] rounds = new Image[4];
        [SerializeField] Image[] sets = new Image[10];
        [SerializeField] Image[] travelers = new Image[2];
        Color[] colors;
        Color empty;
        SfxPlayer sound;
        int soundKo = -1, soundStage;
        public RectTransform Root => root;
        public bool Traveling => travelers[0].gameObject.activeSelf || travelers[1].gameObject.activeSelf;
        public Vector2 TravelerPosition(int slot) => travelers[slot].rectTransform.anchoredPosition;
        Text[] labels;
        bool labelsHidden;

        /// <summary>점수판의 "P1"/"P2" 글자(프리팹에서 옮겨도 실제 자리를 쓴다). 없으면 null.</summary>
        public Text Label(int slot)
        {
            if (labels == null)
            {
                labels = new Text[2];
                foreach (var text in root.GetComponentsInChildren<Text>(true))
                    for (int p = 0; p < 2; p++)
                        if (labels[p] == null && text.text.Trim() == "P" + (p + 1)) labels[p] = text;
            }
            return slot >= 0 && slot < 2 ? labels[slot] : null;
        }

        /// <summary>
        /// 로비 → 첫 경기 전환(2026-11-19): 빈 세트 점이 안쪽(가운데)부터 차례로 톡 드러나고, 라운드 점이 한 박자 뒤에 따라온다.
        /// 이 동안 "P1"/"P2" 글자는 로비 표시의 이름이 맡으므로 숨긴다(이름이 이 자리에 와서 멈춘다). reveal은 0~1.
        /// </summary>
        public void RenderLobbyReveal(float reveal)
        {
            root.gameObject.SetActive(true);
            SetLabelsVisible(false);
            for (int p = 0; p < 2; p++)
            {
                for (int i = 0; i < 5; i++)
                {
                    int order = p == 0 ? 4 - i : i;
                    float t = Range(reveal, order * .1f, order * .1f + .34f);
                    var dot = sets[p*5+i];
                    dot.color = PaletteAsset.WithAlpha(empty, Out(t * 2));
                    dot.rectTransform.localScale = Vector3.one * Mathf.Max(0, Pop(t));
                }
                for (int i = 0; i < 2; i++)
                {
                    int order = p == 0 ? 1 - i : i;
                    float t = Range(reveal, .42f + order * .1f, .76f + order * .1f);
                    var dot = rounds[p*2+i];
                    dot.gameObject.SetActive(true);
                    dot.color = PaletteAsset.WithAlpha(empty, Out(t * 2));
                    dot.rectTransform.localScale = Vector3.one * Mathf.Max(0, Pop(t));
                    dot.rectTransform.anchoredPosition = RoundPoint(p, i);
                }
                travelers[p].gameObject.SetActive(false);
            }
        }

        void SetLabelsVisible(bool visible)
        {
            if (labelsHidden == !visible) return;
            labelsHidden = !visible;
            for (int p = 0; p < 2; p++)
            {
                var label = Label(p);
                if (label != null) label.enabled = visible;
            }
        }
        public static Vector2 RoundPoint(int slot, int index) => new Vector2((slot == 0 ? 764 : 1120) + index * 36, -140);
        public static Vector2 SetPoint(int slot, int index) => new Vector2((slot == 0 ? 714 : 1070) + index * 34, -94);

        /// <summary>
        /// 세트 점 합치기 뒤 한 번 눌렸다 정착한다. PhaseTick으로 합치기 시점을 역산하므로
        /// KO→RoundEnd(1틱)→선택/결과에서도 이어지고, 일시정지·되감기·반복 렌더에 타이머가 누적되지 않는다.
        /// </summary>
        public static float SetPointScale(in MatchState match, in MatchParams config, int slot, int index, float alpha)
        {
            if (!config.CollectCardsOnly || match.RoundWinner != slot) return 1;
            float start = CardCollectionRules.HasCountIn(config) ? 84 : 0;
            float mergeAt = Mathf.Lerp(start, config.KoTicks, .96f);
            float age;
            int target;
            if (match.Phase == MatchPhase.KO)
            {
                if (match.GetScore(slot) + 1 < config.RoundsToWinSet) return 1;
                target = match.GetSetScore(slot);
                age = match.PhaseTick + alpha - mergeAt;
            }
            else if (match.Phase == MatchPhase.RoundEnd)
            {
                if (match.GetScore(slot) < config.RoundsToWinSet) return 1;
                target = match.GetSetScore(slot);
                age = config.KoTicks - mergeAt + match.PhaseTick + alpha;
            }
            else if (match.Phase == MatchPhase.SetPick || match.Phase == MatchPhase.MatchEnd)
            {
                target = match.GetSetScore(slot) - 1;
                age = config.KoTicks - mergeAt + 1 + match.PhaseTick + alpha;
            }
            else return 1;
            if (target != index || index < 0 || age < 0 || age >= 24) return 1;
            // 0.4초: 합쳐진 점의 탄성 압축(1.30→0.94) 뒤 한 번 부드럽게 원래 크기로 돌아온다.
            return age < 9 ? Mathf.Lerp(1.30f, .94f, Smooth(age / 9))
                : Mathf.Lerp(.94f, 1, Out((age - 9) / 15));
        }

        public FrontendScoreView(RectTransform stage, FrontendTheme theme, MatchPresenter presenter)
        {
            colors = new[] { presenter.Palette.FighterBody(0), presenter.Palette.FighterBody(1) };
            sound = presenter.SfxPlayer;
            empty = theme.emptyScore;
            root = Rect(stage, "Round_and_set_points", 0, 0, 1920, 1080);
            for (int p = 0; p < 2; p++)
            {
                Text(root, "P" + (p + 1), new Rect(p == 0 ? 630 : 1240, 65, 65, 55), "P" + (p + 1), theme.boldFont, 30, colors[p]);
                for (int i = 0; i < 5; i++) sets[p*5+i] = Dot("Set " + p + ":" + i, SetPoint(p, i), 23, presenter.Shapes.Circle);
                for (int i = 0; i < 2; i++) rounds[p*2+i] = Dot("Round " + p + ":" + i, RoundPoint(p, i), 13, presenter.Shapes.Circle);
                travelers[p] = Dot("Player becomes point " + p, Vector2.zero, 22, presenter.Shapes.Circle);
                travelers[p].color = colors[p];
            }
        }

        public void Bind(FrontendTheme theme, MatchPresenter presenter)
        {
            colors = new[] { presenter.Palette.FighterBody(0), presenter.Palette.FighterBody(1) };
            sound=presenter.SfxPlayer; empty=theme.emptyScore; soundKo=-1; soundStage=0;
        }
        public void AddAnimationChannels(System.Collections.Generic.Dictionary<RectTransform,FrontendLayout.Channels> result)
        {
            foreach(var image in sets)result[image.rectTransform]=FrontendLayout.Channels.Scale|FrontendLayout.Channels.Color;
            foreach(var image in rounds)result[image.rectTransform]=FrontendLayout.Channels.Position|FrontendLayout.Channels.Scale|FrontendLayout.Channels.Color;
            foreach(var image in travelers)result[image.rectTransform]=FrontendLayout.Channels.Position|FrontendLayout.Channels.Scale|FrontendLayout.Channels.Color;
        }

        Image Dot(string name, Vector2 at, float size, Sprite sprite)
        {
            var image = Image(root, name, new Rect(0, 0, size, size), empty, sprite);
            image.rectTransform.pivot = Vector2.one * .5f;
            image.rectTransform.anchoredPosition = at;
            return image;
        }

        public void Render(in SimState state, SimConfig config, bool visible, Vector2 origin, float alpha)
        {
            root.gameObject.SetActive(visible);
            SetLabelsVisible(true);
            var m = state.Match;
            float t = m.Phase == MatchPhase.KO ? Range(m.PhaseTick + alpha, CardCollectionRules.HasCountIn(config.Match) ? 84 : 0, config.Match.KoTicks) : 0;
            int winner = m.Phase == MatchPhase.KO ? m.RoundWinner : -1;
            bool setWon = winner >= 0 && m.GetScore(winner) + 1 >= config.Match.RoundsToWinSet;
            // RoundEnd에는 라운드 점수만 확정되어 있다. 다음 틱의 세트 점수 반영까지 미리 켠 점을 유지한다.
            bool pendingSet = m.Phase == MatchPhase.RoundEnd && m.RoundWinner >= 0
                && m.GetScore(m.RoundWinner) >= config.Match.RoundsToWinSet;
            float merge = Smooth(Range(t, .77f, .96f));
            for (int p = 0; p < 2; p++)
            {
                int roundCount = m.GetScore(p) + (p == winner && t >= .72f ? 1 : 0);
                int setCount = m.GetSetScore(p) + ((p == winner && setWon && t >= .96f)
                    || (pendingSet && p == m.RoundWinner) ? 1 : 0);
                for (int i = 0; i < 5; i++)
                {
                    var dot = sets[p*5+i];
                    dot.color = i < setCount ? colors[p] : empty;
                    float pulse = SetPointScale(m, config.Match, p, i, alpha);
                    dot.rectTransform.localScale = Vector3.one * pulse;
                }
                for (int i = 0; i < 2; i++)
                {
                    var dot = rounds[p*2+i];
                    bool merging = p == winner && setWon && t >= .77f;
                    dot.color = i < roundCount ? colors[p] : empty;
                    dot.rectTransform.anchoredPosition = merging
                        ? Arc(RoundPoint(p, i), SetPoint(p, Mathf.Min(4, m.GetSetScore(p))), merge, 25)
                        : RoundPoint(p, i);
                    dot.rectTransform.localScale = Vector3.one * (merging ? Mathf.Lerp(1, 1.7f, merge) : 1);
                    dot.gameObject.SetActive(!(setWon && t >= .96f) && !pendingSet);
                }
                var travel = travelers[p];
                bool flying = p == winner && t >= .26f && t < .72f;
                travel.gameObject.SetActive(flying);
                if (flying)
                {
                    float move = Smooth(Range(t, .40f, .72f));
                    travel.rectTransform.anchoredPosition = Arc(origin, RoundPoint(p, Mathf.Clamp(m.GetScore(p), 0, 1)), move, 140);
                    travel.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, .59f, move);
                    travel.color = PaletteAsset.WithAlpha(colors[p], Range(t, .26f, .38f));
                }
            }
            if (!visible || winner < 0) return;
            int ko = state.Tick - m.PhaseTick;
            if (soundKo != ko) { soundKo = ko; soundStage = 0; }
            if (t >= .72f && soundStage == 0) { sound.Play(Sfx.Countdown, .35f, 1.5f); soundStage = 1; }
            if (setWon && t >= .96f && soundStage == 1) { sound.Play(Sfx.Grab, .45f, 1.2f); soundStage = 2; }
        }
    }
}
