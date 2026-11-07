using Knockbox.Presentation;
using UnityEngine;

namespace Knockbox.Game.UI
{
    public sealed partial class FrontendView
    {
        const float RematchDimOpacity = .98f;
        [SerializeField] FrontendGraphic rematchDim;
        public FrontendGraphic RematchDim => rematchDim;

        /// <summary>초상·눈·명판·배경을 합성한 뒤 같은 검정 덮개로 감광한다. 결과 질문과 O/X는 그 위에 둔다.</summary>
        void BuildRematchDim()
        {
            rematchDim = FrontendWidgets.Rect(duel, "Uniform_rematch_dim", 0, 0, 1920, 1080)
                .gameObject.AddComponent<FrontendGraphic>();
            rematchDim.shape = FrontendGraphic.Shape.Split;
            rematchDim.thickness = 0;
            rematchDim.line = PaletteAsset.Transparent;
            rematchDim.raycastTarget = false;
            rematchDim.transform.SetSiblingIndex(resultTitle.transform.GetSiblingIndex());
            ShowRematchDim(false);
        }

        void SetRematchDim(float center, float opening, float leftOpacity, float rightOpacity)
        {
            if (rematchDim == null) return;
            rematchDim.SetCenter(center);
            rematchDim.SetOpening(opening);
            rematchDim.SetSides(PaletteAsset.WithAlpha(PaletteAsset.Black, Mathf.Clamp01(leftOpacity)),
                PaletteAsset.WithAlpha(PaletteAsset.Black, Mathf.Clamp01(rightOpacity)));
        }

        void ShowRematchDim(bool visible)
        {
            if (rematchDim == null) return;
            rematchDim.gameObject.SetActive(visible);
            if (!visible) SetRematchDim(.5f, 0, 0, 0);
        }
    }
}
