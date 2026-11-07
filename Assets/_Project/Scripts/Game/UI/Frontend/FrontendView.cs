using System;
using Knockbox.Presentation;
using Knockbox.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using static Knockbox.Game.UI.FrontendWidgets;

namespace Knockbox.Game.UI
{
    public enum FrontendScreen { Hidden, Main, Join, Waiting, Versus, Cards, Result }

    /// <summary>승인된 R5 구도. 화면만 그리며 경기 상태는 쓰지 않는다.</summary>
    [DefaultExecutionOrder(200)]
    public sealed partial class FrontendView : MonoBehaviour
    {
        public event Action CreateRoom, OpenJoin, JoinRoom, Practice, Back, CopyCode;
        public event Action Rematch;
        public event Action<int> CardClicked;
        public FrontendScreen Screen { get; private set; }
        [field: SerializeField] public InputField CodeInput { get; private set; }
        [field: SerializeField] public Text Status { get; private set; }
        [field: SerializeField] public Canvas Canvas { get; private set; }
        [field: SerializeField] public FrontendTheme Theme { get; private set; }
        public int HoveredCard { get; private set; }
        public bool CanChoose { get; set; }
        public RectTransform Stage => stage;
        public RectTransform Portrait(int slot) => portraits[slot];
        public Button CardButton(int index) => cards[index];

        [SerializeField] RectTransform stage, menu, lobby, duel, choices, handRoot, inventory;
        [SerializeField] Vector2[] portraitPositions = new Vector2[2];
        readonly Text[,] ownedCards = new Text[2,4];
        [SerializeField] GameObject[] cardActors = new GameObject[2];
        [SerializeField] RectTransform[] portraits = new RectTransform[2];
        [SerializeField] CanvasGroup[] portraitAlpha = new CanvasGroup[2];
        [SerializeField] GameObject[] mainActors = new GameObject[2];
        [SerializeField] Text[] nameTexts = new Text[2];
        [SerializeField] GameObject[] nameplates = new GameObject[2];
        [SerializeField] Button[] cards = new Button[3];
        [SerializeField] FrontendButtonMotion[] menuButtons = new FrontendButtonMotion[3];
        [SerializeField] CanvasGroup[] cardAlpha = new CanvasGroup[3];
        [SerializeField] Image[] cardShade = new Image[3];
        [SerializeField] Text[] cardNames = new Text[3];
        readonly Color[] playerColors = new Color[2];
        readonly string[] names = new string[CardCollectionRules.CardCount];
        [SerializeField] Vector2[] cardPositions = new Vector2[3];
        [SerializeField] FrontendGraphic split;
        [SerializeField] Text logo, lobbyTitle, roomCode, pickTitle, pickHint, vsText, resultTitle, resultScore, resultHint, setScore;
        [SerializeField] Button create, copy, join, practice, lobbyBack, resultBack, joinMenu;
        [SerializeField] Image armA, armB, hand, veil, matte;
        [SerializeField] CanvasGroup menuAlpha;
        [SerializeField] Text[] ownedCardReferences = new Text[8];
        [SerializeField] FrontendHover[] cardHover = new FrontendHover[3];
        [SerializeField] bool authored;
        [SerializeField, HideInInspector] FrontendLayout layout = new FrontendLayout();
        public bool IsAuthored => authored;
        int localSlot, selectingSlot, acquired = -1;
        float enteredAt, acquiredProgress;
        Vector2 handPosition;
        Color ink;

        public void Build(MatchRunner runner, MatchPresenter presenter)
        {
            motionRunner = runner; motionPresenter = presenter;
            Theme = FrontendTheme.Load();
            for(int i=0;i<names.Length;i++)
                names[i]=Augments.TryGet(runner.World.Config,CardCollectionRules.FirstCardId+i,out var card)?card.Name:"카드 "+(i+1);
            ink = Theme.ink;
            playerColors[0] = presenter.Palette.FighterBody(0);
            playerColors[1] = presenter.Palette.FighterBody(1);
            var canvasObject = new GameObject("KNOCKBOX_UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas = canvasObject.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080);
            scaler.matchWidthOrHeight = .5f;
            matte=Image(Canvas.transform,"Screen_background",new Rect(0,0,1920,1080),Theme.background);
            matte.rectTransform.anchorMin=Vector2.zero;matte.rectTransform.anchorMax=Vector2.one;
            matte.rectTransform.offsetMin=matte.rectTransform.offsetMax=Vector2.zero;
            if (EventSystem.current == null)
            {
                var events = new GameObject("KNOCKBOX_UI_input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform,false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            stage = Rect(Canvas.transform,"1920_x_1080_stage",0,0,1920,1080);
            stage.anchorMin=stage.anchorMax=stage.pivot=new Vector2(.5f,.5f);
            stage.anchoredPosition=Vector2.zero;
            var source = new FighterPortraitSource(transform,runner,presenter);

            menu=Rect(stage,"Main_and_lobby",0,0,1920,1080);
            Image(menu,"Background",new Rect(0,0,1920,1080),Theme.background);
            Image(menu,"Floor",new Rect(0,960,1920,2),Theme.floor);
            for(int slot=0;slot<2;slot++)
            {
                var actor=source.Create(menu,slot,new Rect(58,225,730,820),false);
                if(slot==1) actor.localScale=new Vector3(-1,1,1);
                mainActors[slot]=actor.gameObject;
            }
            var mainContent=Rect(menu,"Menu",845,205,960,700);
            logo=Text(mainContent,"Logo",new Rect(0,0,960,150),Theme.logo,Theme.titleFont,128,Theme.white);
            create=Button(mainContent,"Create_room",new Rect(120,240,720,94),Theme.createRoom,Theme,PaletteAsset.Transparent,Theme.white,()=>CreateRoom?.Invoke(),40);
            joinMenu=Button(mainContent,"Join_room",new Rect(120,354,720,94),Theme.joinRoom,Theme,PaletteAsset.Transparent,Theme.white,()=>OpenJoin?.Invoke(),40);
            practice=Button(mainContent,"Practice",new Rect(120,468,720,94),Theme.practice,Theme,PaletteAsset.Transparent,Theme.white,()=>Practice?.Invoke(),40);
            menuButtons[0]=create.GetComponent<FrontendButtonMotion>();
            menuButtons[1]=joinMenu.GetComponent<FrontendButtonMotion>();
            menuButtons[2]=practice.GetComponent<FrontendButtonMotion>();
            menuAlpha=mainContent.gameObject.AddComponent<CanvasGroup>();

            lobby=Rect(menu,"Room",900,230,820,720);
            lobbyTitle=Text(lobby,"Title",new Rect(0,0,820,110),"방 만들기",Theme.titleFont,68,Theme.white);
            roomCode=Text(lobby,"Room_code",new Rect(0,160,820,110),"",Theme.boldFont,90,playerColors[0]);
            var field=Image(lobby,"Join_code",new Rect(30,155,550,100),Theme.input);
            field.raycastTarget=true;
            CodeInput=field.gameObject.AddComponent<InputField>();
            CodeInput.textComponent=Text(field.transform,"Value",new Rect(18,8,514,84),"",Theme.textFont,46,Theme.white);
            CodeInput.placeholder=Text(field.transform,"Placeholder",new Rect(18,8,514,84),"방 코드",Theme.textFont,40,Theme.placeholder);
            CodeInput.characterLimit=12;
            CodeInput.lineType=InputField.LineType.SingleLine;
            CodeInput.contentType=InputField.ContentType.Alphanumeric;
            join=Button(lobby,"Join",new Rect(600,155,190,100),"참가",Theme,playerColors[0],ink,()=>JoinRoom?.Invoke(),34);
            copy=Button(lobby,"Copy_code",new Rect(250,280,320,70),"코드 복사",Theme,Theme.button,Theme.white,()=>CopyCode?.Invoke(),28);
            Status=Text(lobby,"Status",new Rect(0,390,820,140),"",Theme.textFont,30,Theme.status);
            lobbyBack=Button(lobby,"Back",new Rect(250,560,320,75),"돌아가기",Theme,PaletteAsset.Transparent,Theme.back,()=>Back?.Invoke(),28);

            duel=Rect(stage,"Shared_VS_and_cards",0,0,1920,1080);
            split=Rect(duel,"White_divider",0,0,1920,1080).gameObject.AddComponent<FrontendGraphic>();
            split.raycastTarget=false;
            var portraitClip=Rect(duel,"Shared_portrait_framing",0,0,1920,1080);
            portraitClip.gameObject.AddComponent<RectMask2D>();
            for(int slot=0;slot<2;slot++)
            {
                float x=slot==0?Theme.portraitOffset.x:1920-Theme.portraitOffset.x-Theme.portraitSize.x;
                portraits[slot]=source.Create(portraitClip,slot,new Rect(x,Theme.portraitOffset.y,Theme.portraitSize.x,Theme.portraitSize.y),true,slot==0?-Theme.portraitAngle:Theme.portraitAngle);
                portraitPositions[slot]=portraits[slot].anchoredPosition;
                portraitAlpha[slot]=portraits[slot].gameObject.AddComponent<CanvasGroup>();
                var plate=Rect(duel,"P"+(slot+1)+" name",slot==0?-38:1458,162,500,140);
                var graphic=plate.gameObject.AddComponent<FrontendGraphic>();
                graphic.shape=slot==0?FrontendGraphic.Shape.NameLeft:FrontendGraphic.Shape.NameRight;
                graphic.color=playerColors[slot];graphic.raycastTarget=false;
                nameplates[slot]=plate.gameObject;
                nameTexts[slot]=Text(plate,"Player",new Rect(80,10,340,120),"P"+(slot+1),Theme.boldFont,64,ink,slot==0?TextAnchor.MiddleLeft:TextAnchor.MiddleRight);
            }
            vsText=Text(duel,"VS",new Rect(790,365,340,210),"VS",Theme.titleFont,180,Theme.white);
            ReadableOnDivider(vsText,6);
            pickTitle=Text(duel,"Pick_heading",new Rect(300,160,1320,70),"",Theme.titleFont,44,Theme.white);
            var outline=pickTitle.gameObject.AddComponent<Outline>();outline.effectColor=Theme.background;outline.effectDistance=new Vector2(2,-2);
            pickHint=Text(duel,"Effects_deferred",new Rect(450,226,1020,40),"카드 획득만 적용 · 능력 효과는 준비 중",Theme.textFont,21,Theme.hint);
            ReadableOnDivider(pickHint,2);
            choices=Rect(duel,"Cards",0,0,1920,1080);
            for(int i=0;i<3;i++)
            {
                int index=i;
                cardPositions[i]=new Vector2(320+i*420,290);
                var b=Button(choices,"Card "+i,new Rect(cardPositions[i].x,290,440,638),"",Theme,Theme.card,ink,()=>{if(CanChoose)CardClicked?.Invoke(index);});
                cards[i]=b; cardAlpha[i]=b.gameObject.AddComponent<CanvasGroup>();
                ((RectTransform)b.transform).pivot=new Vector2(.5f,.5f);
                b.transform.GetChild(0).gameObject.SetActive(false);
                b.transition=Selectable.Transition.None;
                var border=b.gameObject.AddComponent<Outline>();border.effectDistance=new Vector2(5,-5);border.effectColor=playerColors[0];border.enabled=i==0;
                var hover = b.gameObject.AddComponent<FrontendHover>();
                cardHover[i] = hover;
                // 입장 도중 들어온 정지 포인터도 기억하되 클릭 허용 시각은 별도로 유지한다.
                hover.Hover=point=>{if(Screen==FrontendScreen.Cards && selectingSlot==localSlot && VisualCard<0)HoveredCard=index;};
                hover.Leave=()=>{if(VisualCard<0 && HoveredCard==index)HoveredCard=-1;};
                cardNames[i]=Text(b.transform,"Name",new Rect(20,420,400,90),names[i],Theme.titleFont,48,ink);
                if(i==1)
                {
                    for(int slot=0;slot<2;slot++)
                    {
                        var icon=source.Create(b.transform,slot,new Rect(115,120,210,240),true);
                        icon.name="Actual_fighter_icon_P"+(slot+1);cardActors[slot]=icon.gameObject;
                    }
                }
                else if(i==0)
                {
                    var stick=Image(b.transform,"Arm",new Rect(0,0,35,155),ink,presenter.Shapes.LimbCapsule);
                    Segment(stick,new Vector2(150,330),new Vector2(240,205));
                    Image(b.transform,"Hand",new Rect(190,155,100,100),ink,presenter.Shapes.Circle);
                }
                else Text(b.transform,"Throw_arrow",new Rect(110,140,220,220),"↗",Theme.boldFont,120,Theme.arrow);
                cardShade[i]=Image(b.transform,"Unhovered_shade",new Rect(0,0,440,638),PaletteAsset.WithAlpha(Theme.background,.40f));
            }
            handRoot=Rect(duel,"Selecting_player_s_hand",0,0,1920,1080);
            armA=Image(handRoot,"Upper_arm",new Rect(0,0,32,32),ink,presenter.Shapes.LimbCapsule);
            armB=Image(handRoot,"Lower_arm",new Rect(0,0,32,32),ink,presenter.Shapes.LimbCapsule);
            hand=Image(handRoot,"Hand",new Rect(0,0,52,52),ink,presenter.Shapes.Circle);
            resultTitle=Text(duel,"Winner",new Rect(470,65,980,190),"",Theme.titleFont,100,Theme.white);
            resultScore=Text(duel,"Set_score",new Rect(600,320,720,160),"",Theme.boldFont,110,Theme.white);
            resultHint=Text(duel,"Rematch",new Rect(420,820,1080,140),"",Theme.textFont,32,Theme.white);
            ReadableOnDivider(resultTitle,4);ReadableOnDivider(resultScore,4);ReadableOnDivider(resultHint,2);
            resultBack=Button(duel,"Return_to_menu",new Rect(755,980,410,65),"메인으로",Theme,Theme.background,Theme.white,()=>Back?.Invoke(),26);
            setScore=Text(stage,"Current_set_score",new Rect(740,62,440,52),"",Theme.boldFont,28,Theme.white);
            inventory=Rect(stage,"Acquired_cards",0,0,1920,1080);
            for(int slot=0;slot<2;slot++) for(int i=0;i<4;i++)
            {
                float x=slot==0?35+i*150:1885-(i+1)*150;
                var tile=Image(inventory,"P"+(slot+1)+" acquired "+i,new Rect(x,1010,140,48),Theme.inventory);
                ownedCards[slot,i]=Text(tile.transform,"Card_name",new Rect(4,2,132,44),"",Theme.boldFont,22,playerColors[slot]);
            }
            veil=Image(stage,"Transition",new Rect(0,0,1920,1080),PaletteAsset.Transparent);
            BuildMotionUI();
            Show(FrontendScreen.Main);
            SetLocalSlot(0);
            for (int p=0;p<2;p++) for(int i=0;i<4;i++) ownedCardReferences[p*4+i]=ownedCards[p,i];
        }

        public void SetLocalSlot(int slot)
        {
            localSlot=slot;
            for(int i=0;i<2;i++)
            {
                mainActors[i].SetActive(i==slot);
                nameTexts[i].text="P"+(i+1)+(i==slot?"  나":"");
            }
            foreach(var button in menuButtons)button.UsePointerHighlight(playerColors[slot],ink,Theme.white);
            join.image.color=roomCode.color=playerColors[slot];
        }

        public void Show(FrontendScreen screen)
        {
            if(Screen==screen && enteredAt>0) return;
            Screen=screen;enteredAt=Time.unscaledTime;acquired=-1;acquiredProgress=0;
            bool main=screen==FrontendScreen.Main;
            bool room=screen==FrontendScreen.Join||screen==FrontendScreen.Waiting;
            bool vs=screen==FrontendScreen.Versus, pick=screen==FrontendScreen.Cards, result=screen==FrontendScreen.Result;
            menu.gameObject.SetActive(main||room);menuAlpha.gameObject.SetActive(main);lobby.gameObject.SetActive(room);
            duel.gameObject.SetActive(vs||pick||result);
            matte.gameObject.SetActive(screen!=FrontendScreen.Hidden);
            inventory.gameObject.SetActive(screen==FrontendScreen.Hidden||pick);
            foreach(var n in nameplates)n.SetActive(vs||result);
            vsText.gameObject.SetActive(vs);pickTitle.gameObject.SetActive(pick);pickHint.gameObject.SetActive(pick);
            choices.gameObject.SetActive(pick);handRoot.gameObject.SetActive(pick);
            resultTitle.gameObject.SetActive(result);resultScore.gameObject.SetActive(result);resultHint.gameObject.SetActive(result);
            resultBack.gameObject.SetActive(false);
            setScore.gameObject.SetActive(false);
            for(int i=0;i<2;i++)
            {
                portraits[i].gameObject.SetActive(true);
                portraitAlpha[i].alpha=1;
            }
            CodeInput.gameObject.SetActive(screen==FrontendScreen.Join);join.gameObject.SetActive(screen==FrontendScreen.Join);
            roomCode.gameObject.SetActive(screen==FrontendScreen.Waiting);copy.gameObject.SetActive(screen==FrontendScreen.Waiting);
            CanChoose=false;HoveredCard=-1;
            for(int i=0;i<3;i++) {cardAlpha[i].alpha=pick?0:1;cards[i].interactable=!pick;}
            split.SetSides(Theme.duelLeft,Theme.duelRight);
            split.SetSingleSide(-1);
            split.SetDividerSlant(pick?.06f:.10f);
            split.SetCenter(.5f);split.SetOpening(0);
            handPosition=Vector2.zero;
            BeginScreenMotion(screen);
            if(screen==FrontendScreen.Join) {lobbyTitle.text=Theme.joinRoom;CodeInput.Select();CodeInput.ActivateInputField();}
        }

        public void Room(string title, string code, string message, bool busy, bool canCopy)
        {
            lobbyTitle.text=title;roomCode.text=code;Status.text=message;
            join.interactable=!busy;CodeInput.interactable=!busy;copy.gameObject.SetActive(canCopy);
        }

        public void Cards(int picker, bool canChoose, int chosenIndex, float progress, int secondsRemaining)
        {
            selectingSlot=picker;CanChoose=canChoose && Time.unscaledTime-enteredAt>=CardsReadyAt;acquired=chosenIndex;acquiredProgress=progress;
            for(int i=0;i<2;i++)cardActors[i].SetActive(i==picker);
            for(int i=0;i<2;i++)
            {
                portraits[i].gameObject.SetActive(i==picker);
                portraitAlpha[i].alpha=i==picker?1:0;
            }
            split.SetSingleSide(picker);
            split.SetSides(picker==0?Theme.selectedLeft:Theme.inactiveLeft,picker==1?Theme.selectedRight:Theme.inactiveRight);
            pickTitle.text=chosenIndex>=0?names[chosenIndex]+" 획득":picker==localSlot?"P"+(picker+1)+", 하나를 고르세요":"P"+(picker+1)+" 선택 중";
            pickHint.text=(chosenIndex<0?secondsRemaining+"초 · ":"")+"능력 효과 준비 중";
            for(int i=0;i<3;i++) cards[i].interactable=CanChoose;
        }

        public void Result(int winner,int score0,int score1,bool online,bool ready0,bool ready1)
        {
            resultWinner=winner;resultReady0=ready0;resultReady1=ready1;
            resultTitle.text="P"+(winner+1)+" 승리";
            resultTitle.color=playerColors[winner];resultScore.text=score0+" : "+score1;
            resultHint.text="다시하기?";
        }

        public void Inventory(in AugmentState p1,in AugmentState p2)
        {
            for(int slot=0;slot<2;slot++) for(int i=0;i<4;i++)
            {
                var owned=slot==0?p1:p2;
                int index=i<owned.Count?CardCollectionRules.CardIndex(owned.Get(i)):-1;
                ownedCards[slot,i].transform.parent.gameObject.SetActive(index>=0);
                if(index>=0) ownedCards[slot,i].text=names[index];
            }
        }

        public void Score(int p1,int p2) => setScore.text="세트   "+p1+" : "+p2;

        void ReadableOnDivider(Text text,float width)
        {
            var outline=text.gameObject.AddComponent<Outline>();
            outline.effectColor=Theme.background;outline.effectDistance=new Vector2(width,-width);
        }

        void Update()
        {
            if(stage==null)return;
            if (authored) layout.BeforeAnimation();
            var area=((RectTransform)Canvas.transform).rect.size;
            stage.localScale=Vector3.one*Mathf.Min(area.x/1920f,area.y/1080f);
            AnimateScreen();

        }

        void LateUpdate() { if (authored) layout.AfterAnimation(); }

        void DrawHand()
        {
            int active=VisualCard>=0?VisualCard:HoveredCard;
            Vector2 target=new Vector2(selectingSlot==0?650:1270,1000);
            float side=selectingSlot==0?-1:1;
            Vector2 start=new Vector2((selectingSlot==0?96:1824)+side*1500*CardActorLeave,1134);
            if(active>=0)
            {
                var card=(RectTransform)cards[active].transform;
                // 실제 변환을 사용해 회전·확대 중에도 손이 카드 하단에서 떨어지지 않는다.
                var grip=(VisualCard>=0?handRoot:choices).InverseTransformPoint(card.TransformPoint(new Vector3(0,card.rect.yMin+28,0)));
                target=new Vector2(grip.x,-grip.y);
            }
            if(VisualCard>=0)
            {
                float retract=FrontendMotion.In(FrontendMotion.Range(CardFeedbackSeconds,.30f,.58f));
                handPosition=Vector2.Lerp(target,start,retract);
            }
            else handPosition=Vector2.Lerp(handPosition==Vector2.zero?target:handPosition,target,1-Mathf.Exp(-Time.unscaledDeltaTime*25));
            Vector2 elbow=new Vector2(Mathf.Lerp(start.x,handPosition.x,.58f),Mathf.Min(1058,handPosition.y+160));
            Segment(armA,start,elbow);Segment(armB,elbow,handPosition);
            hand.rectTransform.pivot=new Vector2(.5f,.5f);hand.rectTransform.anchoredPosition=new Vector2(handPosition.x,-handPosition.y);
        }

        static void Segment(Image image,Vector2 a,Vector2 b)
        {
            var d=b-a;var r=image.rectTransform;r.pivot=new Vector2(.5f,.5f);
            r.anchoredPosition=new Vector2((a.x+b.x)*.5f,-(a.y+b.y)*.5f);
            r.sizeDelta=new Vector2(35,d.magnitude+5);
            r.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(-d.y,d.x)*Mathf.Rad2Deg-90);
            image.type=UnityEngine.UI.Image.Type.Sliced;image.pixelsPerUnitMultiplier=2;
        }
    }
}
