using System;
using Knockbox.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    public sealed partial class FrontendView
    {
        /// <summary>Connect behavior only. Main never calls the procedural UI builder.</summary>
        public void BindAuthored(MatchRunner runner, MatchPresenter presenter)
        {
            if (!authored || Canvas==null || Theme==null || stage==null || points==null)
                throw new InvalidOperationException("Frontend prefab references are incomplete.");
            motionRunner=runner; motionPresenter=presenter;
            layout.Bind(AnimationChannels());
            ink=Theme.ink;
            for(int p=0;p<2;p++)
            {
                playerColors[p]=presenter.Palette.FighterBody(p);
                for(int i=0;i<4;i++)ownedCards[p,i]=ownedCardReferences[p*4+i];
            }
            for(int i=0;i<names.Length;i++)
                names[i]=Knockbox.Simulation.Augments.TryGet(runner.World.Config,
                    Knockbox.Simulation.CardCollectionRules.FirstCardId+i,out var card)?card.Name:"카드 "+(i+1);
            points.Bind(Theme,presenter);
            Bind(create,()=>CreateRoom?.Invoke()); Bind(joinMenu,()=>OpenJoin?.Invoke());
            Bind(join,()=>JoinRoom?.Invoke()); Bind(practice,()=>Practice?.Invoke());
            Bind(copy,()=>CopyCode?.Invoke()); Bind(lobbyBack,()=>Back?.Invoke());
            Bind(resultBack,()=>Back?.Invoke()); Bind(resultReady,()=>Rematch?.Invoke());
            for(int i=0;i<cards.Length;i++)
            {
                int index=i;
                Bind(cards[i],()=>{if(CanChoose)CardClicked?.Invoke(index);});
                cardHover[i].Hover=point=>{if(Screen==FrontendScreen.Cards && selectingSlot==localSlot && VisualCard<0)HoveredCard=index;};
                cardHover[i].Leave=()=>{if(VisualCard<0 && HoveredCard==index)HoveredCard=-1;};
            }
            Show(FrontendScreen.Main); SetLocalSlot(0);
        }
        static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if(button==null)throw new InvalidOperationException("Missing authored button reference.");
            button.onClick.RemoveAllListeners(); button.onClick.AddListener(action);
        }
        System.Collections.Generic.Dictionary<RectTransform,FrontendLayout.Channels> AnimationChannels()
        {
            var result=new System.Collections.Generic.Dictionary<RectTransform,FrontendLayout.Channels>();
            void Add(RectTransform rect,FrontendLayout.Channels c)
            {if(rect!=null)result[rect]=result.TryGetValue(rect,out var previous)?previous|c:c;}
            var pos=FrontendLayout.Channels.Position;var scale=FrontendLayout.Channels.Scale;var color=FrontendLayout.Channels.Color;
            foreach(var r in new[]{(RectTransform)menuAlpha.transform,lobby,handRoot,pickTitle.rectTransform,pickHint.rectTransform,
                resultTitle.rectTransform,resultScore.rectTransform,resultHint.rectTransform,(RectTransform)resultReady.transform,(RectTransform)resultBack.transform})Add(r,pos);
            foreach(var r in menuEntries)Add(r,pos);foreach(var r in roomEntries)Add(r,pos);
            foreach(var r in portraits)Add(r,pos);foreach(var actor in mainActors)Add((RectTransform)actor.transform,pos);
            foreach(var plate in nameplates)Add((RectTransform)plate.transform,pos|color);
            foreach(var text in nameTexts)Add(text.rectTransform,color);
            foreach(var card in cards)Add((RectTransform)card.transform,pos|scale|FrontendLayout.Channels.Rotation|color);
            foreach(var shade in cardShade)Add(shade.rectTransform,color);
            foreach(var button in GetComponentsInChildren<Button>(true))Add((RectTransform)button.transform,scale);
            foreach(var button in menuButtons)
            {Add((RectTransform)button.transform,color);Add(button.GetComponentInChildren<Text>(true).rectTransform,color);}
            foreach(var text in resultReady.GetComponentsInChildren<Text>(true))Add(text.rectTransform,color);
            foreach(var text in resultBack.GetComponentsInChildren<Text>(true))Add(text.rectTransform,color);
            Add(resultTitle.rectTransform,FrontendLayout.Channels.Size|color);Add(join.image.rectTransform,color);Add(roomCode.rectTransform,color);
            Add(vsText.rectTransform,scale|color);Add(startCue.rectTransform,pos|scale);Add(stage,scale);
            Add(matte.rectTransform,color);Add(veil.rectTransform,color);
            foreach(var image in new[]{armA,armB})Add(image.rectTransform,pos|FrontendLayout.Channels.Size|FrontendLayout.Channels.Rotation);
            Add(hand.rectTransform,pos);points.AddAnimationChannels(result);
            return result;
        }

#if UNITY_EDITOR
        /// <summary>One-time migration only. Never recapture after a team member edits the prefab.</summary>
        public void CaptureAuthoredLayout()
        {
            // Canonical resting layout. Runtime adds the same existing motion plus authored differences.
            menuAlpha.alpha=lobbyAlpha.alpha=1;
            foreach(var group in menuEntryAlpha)group.alpha=1;
            foreach(var group in roomEntryAlpha)group.alpha=1;
            for(int i=0;i<3;i++)
            {
                var r=(RectTransform)cards[i].transform;
                r.anchoredPosition=new Vector2(cardPositions[i].x+440,-cardPositions[i].y-319);
                r.localScale=Vector3.one; r.localRotation=Quaternion.identity;
                cardAlpha[i].alpha=1;
            }
            resultTitle.rectTransform.sizeDelta=new Vector2(700,170);
            resultTitle.rectTransform.anchoredPosition=new Vector2(170,-350);
            resultScore.rectTransform.anchoredPosition=new Vector2(160,-510);
            for(int p=0;p<2;p++)portraits[p].anchoredPosition=portraitPositions[p];
            layout.Capture(stage);
            authored=true;
            PreviewScreen(FrontendScreen.Main);
        }

        /// <summary>Visibility-only preview on the same serialized objects. Does not run or save animation poses.</summary>
        public void PreviewScreen(FrontendScreen screen)
        {
            if(Application.isPlaying)return;
            bool main=screen==FrontendScreen.Main,room=screen==FrontendScreen.Join||screen==FrontendScreen.Waiting;
            bool vs=screen==FrontendScreen.Versus,pick=screen==FrontendScreen.Cards,result=screen==FrontendScreen.Result;
            menu.gameObject.SetActive(main||room);menuAlpha.gameObject.SetActive(main);lobby.gameObject.SetActive(room);
            menuAlpha.alpha=lobbyAlpha.alpha=1;
            duel.gameObject.SetActive(vs||pick||result); matte.gameObject.SetActive(screen!=FrontendScreen.Hidden);
            foreach(var n in nameplates)n.SetActive(vs||result);
            for(int p=0;p<2;p++){portraits[p].gameObject.SetActive(!pick||p==0);portraitAlpha[p].alpha=1;plateAlpha[p].alpha=1;}
            vsText.gameObject.SetActive(vs);pickTitle.gameObject.SetActive(pick);pickHint.gameObject.SetActive(pick);
            choices.gameObject.SetActive(pick);handRoot.gameObject.SetActive(pick);
            handRoot.anchoredPosition=Vector2.zero;handAlpha.alpha=1;
            resultTitle.gameObject.SetActive(result); resultScore.gameObject.SetActive(result);
            resultHint.gameObject.SetActive(result);resultReady.gameObject.SetActive(result);resultBack.gameObject.SetActive(result);
            winAlpha.alpha=scoreAlpha.alpha=questionAlpha.alpha=pickHeadingAlpha.alpha=pickHintAlpha.alpha=1;
            inventory.gameObject.SetActive(pick||screen==FrontendScreen.Hidden);
            points.Root.gameObject.SetActive(pick||screen==FrontendScreen.Hidden);
            setScore.gameObject.SetActive(false);startCue.gameObject.SetActive(false);rematchDim.gameObject.SetActive(false);
            CodeInput.gameObject.SetActive(screen==FrontendScreen.Join);join.gameObject.SetActive(screen==FrontendScreen.Join);
            roomCode.gameObject.SetActive(screen==FrontendScreen.Waiting);copy.gameObject.SetActive(screen==FrontendScreen.Waiting);
            split.SetSides(pick?Theme.selectedLeft:Theme.duelLeft,Theme.duelRight);
            split.SetSingleSide(pick?0:-1);split.SetCenter(pick?.92f:.5f);split.SetDividerSlant(pick?.06f:.1f);split.SetOpening(0);
            for(int i=0;i<3;i++){cardAlpha[i].alpha=1;}
            pickTitle.text="P1, 하나를 고르세요";resultTitle.text="P1 승리";resultScore.text="5 : 3";resultHint.text="다시하기?";
            // Preview visibility is serialized for clarity. Runtime always explicitly starts at Main.
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
