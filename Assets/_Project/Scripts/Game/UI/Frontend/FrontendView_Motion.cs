using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.UI;
using static Knockbox.Game.UI.FrontendWidgets;
using static Knockbox.Game.UI.FrontendMotion;

namespace Knockbox.Game.UI
{
    public sealed partial class FrontendView
    {
        MatchRunner motionRunner;
        MatchPresenter motionPresenter;
        [SerializeField] FrontendScoreView points;
        [SerializeField] Button resultReady;
        [SerializeField] Text startCue;
        [SerializeField] CanvasGroup lobbyAlpha, winAlpha, scoreAlpha, questionAlpha;
        [SerializeField] CanvasGroup handAlpha, pickHeadingAlpha, pickHintAlpha;
        [SerializeField] CanvasGroup[] plateAlpha = new CanvasGroup[2];
        readonly Vector2[] cardDisplay = new Vector2[3];
        readonly float[] cardScales = { 1, 1, 1 };
        readonly float[] cardBrightness = new float[3];
        readonly Vector2[] pickedPositions = new Vector2[3];
        readonly float[] pickedScales = new float[3], pickedRotations = new float[3], pickedAlpha = new float[3];
        readonly float[] cardRotations = new float[3], cardFlips = new float[3], pickedFlips = new float[3];
        int motionCard = -1;
        int pendingCard = -1;
        float cardClickedAt, cardFeedbackOffset;
        int VisualCard => acquired >= 0 ? acquired : pendingCard;
        float CardFeedbackTime => Mathf.Max(acquiredProgress * motionRunner.World.Config.Match.PickHoldTicks / 60f,
            pendingCard < 0 ? 0 : Mathf.Min(.40f, Time.unscaledTime - cardClickedAt));
        float CardFeedbackSeconds => Mathf.Max(0, CardFeedbackTime - cardFeedbackOffset);
        float CardActorLeave => acquired < 0 ? 0 : In(Range(acquiredProgress, .30f, .80f));
        public void BeginCardFeedback(int index)
        {
            if (pendingCard >= 0 || index < 0 || index >= cards.Length) return;
            pendingCard = index; cardClickedAt = Time.unscaledTime;
            CaptureCardPose(index);
        }

        void CaptureCardPose(int selected)
        {
            if (motionCard >= 0 && motionCard != selected) cardFeedbackOffset = CardFeedbackTime;
            motionCard = selected;
            for (int i = 0; i < cards.Length; i++)
            {
                pickedPositions[i] = cardDisplay[i]; pickedScales[i] = cardScales[i];
                pickedRotations[i] = cardRotations[i]; pickedFlips[i] = cardFlips[i]; pickedAlpha[i] = cardAlpha[i].alpha;
            }
        }
        [SerializeField] RectTransform[] menuEntries, roomEntries;
        [SerializeField] Vector2[] menuEntryPositions, roomEntryPositions;
        [SerializeField] CanvasGroup[] menuEntryAlpha, roomEntryAlpha;
        [SerializeField] Vector2[] mainPositions = new Vector2[2];
        int resultWinner;
        bool resultReady0, resultReady1;
        float resultAge;
        bool keepCardBackdrop;
        bool vsImpactPlayed, vsActorsPlayed;
        const float CardDealStart = .72f, CardDealInterval = .10f, CardDealDuration = .26f;
        const float CardsReadyAt = CardDealStart + CardDealInterval * 2 + CardDealDuration + .02f;
        bool MatchHudVisible => Screen == FrontendScreen.Hidden || Screen == FrontendScreen.Cards;
        public FrontendScoreView Points => points;
        public float DividerCenter => split.center;
        public bool RematchVisible => Screen == FrontendScreen.Result && motionRunner.World.State.Match.PhaseTick >= motionRunner.World.Config.Match.MatchEndMinTicks;
        public int IntroRevealTick => Mathf.Max(0, motionRunner.World.Config.Match.CountdownForRound(1) - motionRunner.World.Config.Match.CountdownTicks);

        void BuildMotionUI()
        {
            lobbyAlpha = lobby.gameObject.AddComponent<CanvasGroup>();
            lobbyAlpha.alpha = 0; menuAlpha.alpha = 0;
            for (int p = 0; p < 2; p++) mainPositions[p] = ((RectTransform)mainActors[p].transform).anchoredPosition;
            winAlpha = resultTitle.gameObject.AddComponent<CanvasGroup>();
            scoreAlpha = resultScore.gameObject.AddComponent<CanvasGroup>();
            questionAlpha = resultHint.gameObject.AddComponent<CanvasGroup>();
            handAlpha = handRoot.gameObject.AddComponent<CanvasGroup>();
            pickHeadingAlpha = pickTitle.gameObject.AddComponent<CanvasGroup>();
            pickHintAlpha = pickHint.gameObject.AddComponent<CanvasGroup>();
            for (int p = 0; p < 2; p++) plateAlpha[p] = nameplates[p].AddComponent<CanvasGroup>();
            resultScore.fontSize = 78;
            resultHint.font = Theme.titleFont; resultHint.fontSize = 82;
            resultHint.rectTransform.anchoredPosition = new Vector2(510, -340);
            resultHint.rectTransform.sizeDelta = new Vector2(900, 160);
            resultReady = Button(duel, "Ready_for_rematch", new Rect(730, 530, 190, 170), "O", Theme, PaletteAsset.Transparent, Theme.white, () => Rematch?.Invoke(), 110);
            resultBack.GetComponentInChildren<Text>().text = "X";
            resultBack.GetComponentInChildren<Text>().fontSize = 110;
            resultBack.image.color = PaletteAsset.Transparent;
            resultBack.GetComponent<RectTransform>().anchoredPosition = new Vector2(1000, -530);
            resultBack.GetComponent<RectTransform>().sizeDelta = new Vector2(190, 170);
            var backLabel = resultBack.GetComponentInChildren<Text>().rectTransform;
            backLabel.sizeDelta = new Vector2(170, 162);
            resultReady.gameObject.SetActive(false);
            BuildRematchDim();
            points = new FrontendScoreView(stage, Theme, motionPresenter);
            // 경기 UI를 선택 패널/초상/카드/손보다 위에 유지한다.
            inventory.SetAsLastSibling();
            points.Root.SetAsLastSibling();
            startCue = Text(stage, "Round_start_cue", new Rect(690, 430, 540, 150), "GO", Theme.titleFont, 112, Theme.white);
            ReadableOnDivider(startCue, 4);
            CaptureEntries(menuAlpha.transform, out menuEntries, out menuEntryPositions, out menuEntryAlpha);
            CaptureEntries(lobby, out roomEntries, out roomEntryPositions, out roomEntryAlpha);
        }

        static void CaptureEntries(Transform parent, out RectTransform[] entries, out Vector2[] positions, out CanvasGroup[] groups)
        {
            int count = parent.childCount;
            entries = new RectTransform[count]; positions = new Vector2[count]; groups = new CanvasGroup[count];
            for (int i = 0; i < count; i++)
            {
                entries[i] = parent.GetChild(i) as RectTransform;
                positions[i] = entries[i].anchoredPosition;
                groups[i] = entries[i].GetComponent<CanvasGroup>();
                if (groups[i] == null) groups[i] = entries[i].gameObject.AddComponent<CanvasGroup>();
            }
        }

        void BeginScreenMotion(FrontendScreen screen)
        {
            ShowRematchDim(screen == FrontendScreen.Result);
            resultReady.gameObject.SetActive(false);
            startCue.gameObject.SetActive(false);
            points.Root.gameObject.SetActive(MatchHudVisible);
            matte.color = Theme.background;
            if (screen == FrontendScreen.Main || screen == FrontendScreen.Join || screen == FrontendScreen.Waiting)
            {
                // 메인↔로비는 같은 배경 위에서 서로 다른 방향으로 지나간다.
                menuAlpha.gameObject.SetActive(true); lobby.gameObject.SetActive(true);
            }
            pendingCard = motionCard = -1;
            cardFeedbackOffset = 0;
            vsImpactPlayed = vsActorsPlayed = false;
            for (int i = 0; i < 3; i++) { cardDisplay[i] = cardPositions[i]; cardScales[i] = .88f; cardBrightness[i] = cardRotations[i] = cardFlips[i] = 0; }
            winAlpha.alpha = scoreAlpha.alpha = 1; questionAlpha.alpha = 0;
            if (screen == FrontendScreen.Cards)
            {
                // 첫 렌더 프레임에도 완성 화면이 한 번 번쩍이지 않게 진입 상태부터 배치한다.
                split.SetOpening(1); matte.color = PaletteAsset.Transparent;
                for (int p = 0; p < 2; p++)
                    portraits[p].anchoredPosition = portraitPositions[p] + Vector2.right * (p == 0 ? -1100 : 1100);
                pickHeadingAlpha.alpha = pickHintAlpha.alpha = 0;
                handRoot.anchoredPosition = Vector2.down * 900;
            }
        }

        void AnimateScreen()
        {
            float age = Time.unscaledTime - enteredAt;
            veil.color = PaletteAsset.Transparent;
            if (Screen == FrontendScreen.Main || Screen == FrontendScreen.Join || Screen == FrontendScreen.Waiting)
            {
                bool main = Screen == FrontendScreen.Main;
                float speed = 1 - Mathf.Exp(-16 * Time.unscaledDeltaTime);
                menuAlpha.alpha = Mathf.Lerp(menuAlpha.alpha, main ? 1 : 0, speed);
                lobbyAlpha.alpha = Mathf.Lerp(lobbyAlpha.alpha, main ? 0 : 1, speed);
                menuAlpha.interactable = menuAlpha.blocksRaycasts = main;
                lobbyAlpha.interactable = lobbyAlpha.blocksRaycasts = !main;
                ((RectTransform)menuAlpha.transform).anchoredPosition = new Vector2(845 - 100 * (1 - menuAlpha.alpha), -205);
                lobby.anchoredPosition = new Vector2(900 + 100 * (1 - lobbyAlpha.alpha), -230);
                AnimateEntries(main ? menuEntries : roomEntries, main ? menuEntryPositions : roomEntryPositions,
                    main ? menuEntryAlpha : roomEntryAlpha, age);
                // 메인/방 만들기/코드 참가 전환은 메뉴만 움직인다. 배경 캐릭터는 같은 자리에서 유지한다.
                for (int p = 0; p < 2; p++) ((RectTransform)mainActors[p].transform).anchoredPosition = mainPositions[p];
                return;
            }
            if (Screen == FrontendScreen.Hidden) return;
            float entry = Out(age / .42f);
            float exit = 0;
            if (Screen == FrontendScreen.Versus)
            {
                float tick = motionRunner.World.State.Match.PhaseTick + motionRunner.RenderAlpha;
                if (IntroRevealTick >= CardCollectionRules.IntroVsTicks)
                {
                    AnimateImpactVersus(tick);
                    return;
                }
                entry = Out(Range(tick, 9, 33));
                exit = Smooth(Range(tick, IntroRevealTick - 22, IntroRevealTick));
                float actorExit = Smooth(Range(tick, IntroRevealTick - 18, IntroRevealTick));
                float plateEntry = Out(Range(tick, 18, 42));
                float plateExit = Smooth(Range(tick, IntroRevealTick - 12, IntroRevealTick));
                split.SetCenter(.5f); split.SetOpening(Mathf.Max(1 - Out(tick / 18), exit));
                matte.color = PaletteAsset.WithAlpha(Theme.background, 1 - exit);
                vsText.rectTransform.localScale = Vector3.one * Pop(Range(tick, 18, 42)) * (1 + plateExit * .25f);
                vsText.color = PaletteAsset.WithAlpha(Theme.white, Range(tick, 18, 27) * (1 - plateExit));
                for (int p = 0; p < 2; p++)
                {
                    float direction = p == 0 ? -1 : 1;
                    portraits[p].anchoredPosition = portraitPositions[p] + Vector2.right * direction * ((1 - entry) * 220 + actorExit * 1000);
                    portraitAlpha[p].alpha = Range(tick, 9, 20) * (1 - actorExit);
                    Plate(p, direction * ((1 - plateEntry) * 320 + plateExit * 900), 1, plateEntry * (1 - plateExit));
                }
                return;
            }
            split.SetOpening(0);
            if (Screen == FrontendScreen.Cards)
            {
                float leave = CardActorLeave;
                split.SetCenter(selectingSlot == 0 ? .92f : .08f);
                split.SetOpening(Mathf.Max(1 - Out(Range(age, 0, .72f)), acquired >= 0 ? Smooth(Range(acquiredProgress, .65f, 1)) : 0));
                matte.color = PaletteAsset.Transparent;
                float actorEntry = Out(Range(age, 0, .86f));
                for (int p = 0; p < 2; p++)
                    portraits[p].anchoredPosition = portraitPositions[p] + Vector2.right * (p == 0 ? -1 : 1) * (1100 * (1 - actorEntry) + 1500 * leave);
                // 완성된 초상이 들어오고, 카드를 손으로 가져간 뒤 한 박자 늦게 물러난다.
                portraitAlpha[selectingSlot].alpha = 1;
                float headingExit=acquired>=0?1-Out(Range(acquiredProgress,.15f,.50f)):1;
                pickHeadingAlpha.alpha = Out(Range(age, .45f, .65f))*headingExit;
                pickHintAlpha.alpha = Out(Range(age, .55f, .75f))*headingExit;
                handAlpha.alpha = 1;
                handRoot.anchoredPosition = Vector2.down * 900 * (1 - Out(Range(age, 1.02f, 1.28f)));
                pickTitle.rectTransform.anchoredPosition = new Vector2(300, -160 + (1 - Out(Range(age, .35f, .70f))) * 60);
                pickHint.rectTransform.anchoredPosition = new Vector2(450, -226 + (1 - Out(Range(age, .45f, .75f))) * 40);
                AnimateCards(age);
                if (authored) foreach (var card in cards) layout.Apply((RectTransform)card.transform);
                DrawHand();
                return;
            }
            if (Screen == FrontendScreen.Result) AnimateResult();
        }

        static void AnimateEntries(RectTransform[] items, Vector2[] home, CanvasGroup[] groups, float age)
        {
            for (int i = 0; i < items.Length; i++)
            {
                float t = Range(age, i * .045f, .32f + i * .045f);
                items[i].anchoredPosition = home[i] + Vector2.right * 85 * (1 - Out(t));
                groups[i].alpha = Out(t);
            }
        }

        void AnimateImpactVersus(float tick)
        {
            if (tick >= 36 && !vsImpactPlayed) { vsImpactPlayed = true; motionPresenter.SfxPlayer.Play(Sfx.Clash, .55f, .85f); }
            if (tick >= 42 && !vsActorsPlayed) { vsActorsPlayed = true; motionPresenter.SfxPlayer.Play(Sfx.Dash, .35f, 1.2f); }
            // 느린 출발→가속 충돌. 캐릭터/명판은 배경이 닫힌 뒤 진입하고, 정착 후 2초간 유지한다.
            float close = In(Range(tick, 0, 36));
            float impact = Mathf.Sin(Range(tick, 36, 42) * Mathf.PI) * .012f;
            float exit = In(Range(tick, IntroRevealTick - 30, IntroRevealTick));
            split.SetCenter(.5f); split.SetOpening(Mathf.Max(1 - close + impact, exit));
            matte.color = PaletteAsset.WithAlpha(Theme.background, 1 - exit);
            float actor = Pop(Range(tick, 42, 66));
            float plate = Out(Range(tick, 48, 70));
            float actorExit = In(Range(tick, IntroRevealTick - 28, IntroRevealTick));
            for (int p = 0; p < 2; p++)
            {
                float side = p == 0 ? -1 : 1;
                portraits[p].anchoredPosition = portraitPositions[p] + Vector2.right * side * (1100 * (1 - actor) + 1350 * actorExit);
                portraitAlpha[p].alpha = tick < 42 ? 0 : 1;
                Plate(p, side * (650 * (1 - plate) + 1100 * actorExit), 1, tick < 48 ? 0 : 1);
            }
            float title = Pop(Range(tick, 51, 70));
            vsText.rectTransform.localScale = Vector3.one * title * (1 + exit * .2f);
            vsText.color = PaletteAsset.WithAlpha(Theme.white, Out(Range(tick, 51, 57)) * (1 - exit));
        }

        void AnimateCards(float age)
        {
            float sideShift = selectingSlot == 0 ? 220 : -220;
            float side = selectingSlot == 0 ? -1 : 1;
            int selected = VisualCard;
            if (selected >= 0)
            {
                // 원격/시간초과 선택도 현재 포즈에서 시작한다. 확정이 예측과 다르면
                // 이미 사라진 카드를 되살리지 않으며 실제 획득은 보유 UI에 표시한다.
                if (motionCard != selected) CaptureCardPose(selected);
                cards[selected].transform.SetAsLastSibling();
            }
            for (int i = 0; i < 3; i++)
            {
                bool active = selected >= 0 ? selected == i : age >= CardsReadyAt && HoveredCard == i;
                float dealAt = CardDealStart + i * CardDealInterval;
                float entry = Pop(Range(age, dealAt, dealAt + CardDealDuration));
                Vector2 pos = cardPositions[i] + new Vector2(sideShift + (1 - entry) * side * 520, (active ? -20 : 0) + (1 - entry) * 60);
                float scale = active ? 1 : .88f;
                float rotation = (1 - entry) * side * 12, flip = 0;
                float opacity = Out(Range(age, dealAt, dealAt + .06f));
                if (selected >= 0)
                {
                    float t = CardFeedbackSeconds;
                    pos = pickedPositions[i]; scale = pickedScales[i];
                    rotation = pickedRotations[i]; opacity = pickedAlpha[i];
                    if (active)
                    {
                        // 영상의 짧은 눌림→손으로 가져가기. 긴 화면 횡단 대신 초상 앞에서 작아진다.
                        float press = t < .065f ? Mathf.Lerp(1, .90f, Out(t / .065f))
                            : Mathf.LerpUnclamped(.90f, 1, Pop(Range(t, .065f, .14f)));
                        float pull = In(Range(t, .10f, .30f));
                        Vector2 destination = new Vector2(selectingSlot == 0 ? 330 : 1590, 880) - new Vector2(220, 319);
                        pos = Vector2.Lerp(pos, destination, pull);
                        scale *= press * Mathf.Lerp(1, .04f, pull);
                        rotation = Mathf.Lerp(rotation, -side * 12, pull);
                        flip = Mathf.Lerp(pickedFlips[i], 0, Out(Range(t, 0, .10f)));
                        opacity *= 1 - Smooth(Range(t, .24f, .30f));
                    }
                    else
                    {
                        // 한 방향으로 함께 쓸려나간다. 큰 회전/각기 다른 투척 궤적 대신
                        // 처음부터 사라지는 잔상과 완만한 가감속으로 짧은 바람 같은 퇴장.
                        float leave = Smooth(Range(t, 0, .42f));
                        pos += new Vector2(-side * 620, -36) * leave;
                        rotation = Mathf.Lerp(rotation, -side * 3, leave);
                        flip = Mathf.Lerp(pickedFlips[i], 0, leave);
                        scale *= Mathf.Lerp(1, .96f, leave);
                        opacity *= 1 - Out(Range(t, .04f, .40f));
                    }
                }
                float follow = selected >= 0 || age < CardsReadyAt ? 1 : 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime);
                cardDisplay[i] = Vector2.Lerp(cardDisplay[i], pos, follow);
                cardScales[i] = Mathf.Lerp(cardScales[i], scale, follow);
                var rect = (RectTransform)cards[i].transform;
                // 위치·눌림·뒤집기 모두 카드 중심이 기준이다.
                rect.anchoredPosition = new Vector2(cardDisplay[i].x + 220, -cardDisplay[i].y - 319);
                cardRotations[i] = rotation; cardFlips[i] = flip;
                rect.localScale = Vector3.one * cardScales[i]; rect.localRotation = Quaternion.Euler(0, flip, rotation);
                cardAlpha[i].alpha = opacity;
                cards[i].image.color = active ? Theme.white : Theme.card;
                cardBrightness[i] = Mathf.Lerp(cardBrightness[i], active ? 1 : 0, selected >= 0 ? 1 : follow);
                cardShade[i].color = PaletteAsset.WithAlpha(Theme.background,.94f * (1 - cardBrightness[i]));
                var border = cards[i].GetComponent<Outline>(); border.enabled = active; border.effectColor = playerColors[selectingSlot];
            }
        }

        void Plate(int slot, float shift, float brightness, float opacity = 1)
        {
            plateAlpha[slot].alpha = opacity;
            var rect = (RectTransform)nameplates[slot].transform;
            rect.anchoredPosition = new Vector2((slot == 0 ? -38 : 1458) + shift, -162);
            rect.GetComponent<FrontendGraphic>().color = Color.Lerp(Theme.background, playerColors[slot], brightness);
            rect.GetComponent<FrontendGraphic>().SetVerticesDirty();
            nameTexts[slot].color = Color.Lerp(Theme.background, ink, brightness);
            // 같은 색으로 진입하는 글자도 페이드/재활성화 중 메시를 갱신한다.
            nameTexts[slot].SetVerticesDirty();
        }

        void AnimateResult()
        {
            resultAge = motionRunner.World.State.Match.PhaseTick / 60f;
            float expand = Smooth(Range(resultAge, 0, .42f));
            float center = Smooth(Range(resultAge, 2.65f, 3.12f));
            float actorEntry = Out(Range(resultAge, .16f, .64f));
            float plateEntry = Out(Range(resultAge, .28f, .72f));
            float actorsReturn = Out(Range(resultAge, 2.85f, 3.40f));
            float platesReturn = Out(Range(resultAge, 2.98f, 3.48f));
            split.SetCenter(Mathf.Lerp(Mathf.Lerp(.5f, resultWinner == 0 ? .88f : .12f, expand), .5f, center));
            split.SetOpening(1 - Out(Range(resultAge, 0, .42f)));
            for (int p = 0; p < 2; p++)
            {
                bool winner = p == resultWinner;
                float appear = winner ? actorEntry : actorsReturn;
                float plateAppear = winner ? plateEntry : platesReturn;
                float direction = p == 0 ? -1 : 1;
                portraits[p].gameObject.SetActive(winner || actorsReturn > 0);
                nameplates[p].SetActive(winner || platesReturn > 0);
                portraitAlpha[p].alpha = appear;
                // 승리 강조 때도 자기 쪽 가장자리 구도. 상대는 재대결 질문이 열릴 때 돌아온다.
                portraits[p].anchoredPosition = portraitPositions[p] + Vector2.right * direction * 200 * (1 - appear);
                Plate(p, direction * 260 * (1 - plateAppear), 1, plateAppear);
            }
            // 완성된 배경·초상·명판 위를 같은 검은 오버레이로 덮는다.
            // 캐릭터 알파를 낮춰 얼굴/눈이 배경에 비쳐 사라지는 현상을 피한다.
            split.SetSides(Theme.selectedLeft, Theme.selectedRight);
            float leftDim = Mathf.Lerp(resultWinner == 0 ? 0 : .88f, resultReady0 ? 0 : RematchDimOpacity, center);
            float rightDim = Mathf.Lerp(resultWinner == 1 ? 0 : .88f, resultReady1 ? 0 : RematchDimOpacity, center);
            SetRematchDim(split.center, split.Opening, leftDim, rightDim);
            float x = resultWinner == 0 ? 170 : 1050;
            resultTitle.rectTransform.sizeDelta = new Vector2(700, 170);
            float textExit = Smooth(Range(resultAge, 2.80f, 3.18f));
            resultTitle.rectTransform.anchoredPosition = new Vector2(x, -350 + (1 - Out(Range(resultAge, .34f, .72f))) * 140 + textExit * 300);
            resultScore.rectTransform.anchoredPosition = new Vector2(x - 10, -510 + (1 - Out(Range(resultAge, .42f, .78f))) * 90 + textExit * 250);

            winAlpha.alpha = Out(Range(resultAge, .34f, .55f)) * (1 - textExit);
            scoreAlpha.alpha = Out(Range(resultAge, .42f, .65f)) * (1 - textExit);
            float question = Out(Range(resultAge, 3.12f, 3.5f));
            questionAlpha.alpha = question;
            resultHint.rectTransform.anchoredPosition = new Vector2(510, -340 - (1 - question) * 90);
            resultReady.gameObject.SetActive(question > 0);resultBack.gameObject.SetActive(question > 0);
            resultReady.interactable = RematchVisible && !(localSlot == 0 ? resultReady0 : resultReady1);
            resultBack.interactable = RematchVisible;
            resultReady.image.color = PaletteAsset.Transparent;
            resultReady.GetComponentInChildren<Text>().color = PaletteAsset.WithAlpha(Theme.white, question);
            resultBack.GetComponentInChildren<Text>().color = PaletteAsset.WithAlpha(Theme.white, question);
            ((RectTransform)resultReady.transform).anchoredPosition = new Vector2(730, -530 - (1 - question) * 120);
            ((RectTransform)resultBack.transform).anchoredPosition = new Vector2(1000, -530 - (1 - question) * 120);
        }

        /// <summary>MatchPresenter가 IK 렌더링 뒤 호출한다. 두 선수의 재등장에는 같은 진행도를 쓴다.</summary>
        public void PresentMatchFrame()
        {
            if (motionRunner == null || points == null) return;
            var w = motionRunner.World;var m = w.State.Match;
            if (w.Config.Match.IntroCountdownTicks <= 0)
            {
                motionPresenter.PresentFrontendStage(1, true, 1, 1, 1, 0, true);
                points.Root.gameObject.SetActive(false); return;
            }
            bool countdown = m.Phase == MatchPhase.Countdown, ko = m.Phase == MatchPhase.KO;
            bool fighting = m.Phase == MatchPhase.Fighting;
            if (fighting || Screen == FrontendScreen.Main || Screen == FrontendScreen.Versus || Screen == FrontendScreen.Result)
                keepCardBackdrop = false;
            // 선택으로 넘어갈 세트 KO에서 지형/파편만 퇴장시킨다. 다음 재구성까지 배경이 한 프레임 꺼지지 않게 유지한다.
            int winner = m.RoundWinner;
            bool goingToCards = w.Config.Match.CollectCardsOnly && winner >= 0 && (ko || m.Phase == MatchPhase.RoundEnd)
                && m.GetScore(winner) + (ko ? 1 : 0) >= w.Config.Match.RoundsToWinSet
                && m.GetSetScore(winner) + 1 < w.Config.Match.SetsToWinMatch
                && m.GetPicks(1 - winner) < w.Config.Match.MaxPicks;
            if (goingToCards || m.Phase == MatchPhase.SetPick) keepCardBackdrop = true;
            float tick = m.PhaseTick + motionRunner.RenderAlpha;
            bool countIn = CardCollectionRules.HasCountIn(w.Config.Match);
            float t = ko ? Range(tick, countIn ? 84 : 0, w.Config.Match.KoTicks) : 0;
            int buildStart = m.Round == 1 ? IntroRevealTick : 0;
            int buildEnd = countIn ? buildStart + CardCollectionRules.RebuildTicks : w.Config.Match.CountdownForRound(m.Round) - 5;
            bool roundLightOn = !countdown || !countIn || tick >= buildEnd + CardCollectionRules.CountBeatTicks * 3;
            // 화면 전체를 감광하지 않는다. 배경의 그림자 명암은 그대로 두고 GO에서 빛 영역만 연다.
            float build = countdown ? Range(tick, buildStart, buildEnd) : 1;
            float spawn = countdown ? Pop(Range(build, .38f, .84f)) : fighting || ko ? 1 : 0;
            float shrink = 1 - Out(Range(t, .18f, .38f));
            float exit = ko ? Range(t, .48f, 1) : fighting || countdown ? 0 : 1;
            float opacity = countdown ? Out(build) : 1 - Smooth(exit);
            Vector2 origin = Vector2.zero;
            if (ko && m.RoundWinner >= 0 && Camera.main != null)
            {
                var screen = Camera.main.WorldToScreenPoint(motionPresenter.Fighter(m.RoundWinner).BodyCenter);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(stage, screen, null, out var local))
                    origin = local + new Vector2(960, -540);
            }
            points.Render(w.State, w.Config, MatchHudVisible, origin, motionRunner.RenderAlpha);
            float s0 = ko && m.RoundWinner == 0 ? shrink : spawn;
            float s1 = ko && m.RoundWinner == 1 ? shrink : spawn;
            motionPresenter.PresentFrontendStage(countdown ? build : exit, countdown, opacity, s0, s1,
                countdown ? (1 - Out(Range(build, .38f, .84f))) * 2.2f : 0, fighting, roundLightOn, keepCardBackdrop);
            motionPresenter.Background.SetRoundLightPulse(countdown && countIn
                ? (tick - buildEnd - CardCollectionRules.CountBeatTicks * 3) / 60f : -1);
            bool cue = countdown && (countIn ? tick >= buildEnd : build >= .86f) && Screen == FrontendScreen.Hidden;
            startCue.gameObject.SetActive(cue);
            if (cue)
            {
                int beat = countIn ? Mathf.Min(3, (int)(tick - buildEnd) / CardCollectionRules.CountBeatTicks) : 3;
                float beatTime = countIn ? (tick - buildEnd - beat * CardCollectionRules.CountBeatTicks) / 24f : Range(build, .86f, 1);
                startCue.text = beat == 3 ? "GO" : (3 - beat).ToString();
                startCue.rectTransform.localScale = Vector3.one * (1.28f - .28f * Out(beatTime));
                startCue.rectTransform.anchoredPosition = new Vector2(690, -430 - 60 * (1 - Out(beatTime)));
            }
        }
    }
}
