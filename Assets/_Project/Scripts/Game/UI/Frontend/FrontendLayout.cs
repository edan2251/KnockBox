using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Knockbox.Game.UI
{
    /// <summary>
    /// Generated reference poses are captured once by the editor migration. At runtime only
    /// authored differences are added to the existing motion. Never writes back to the asset.
    /// Text/font/sprite properties remain on the actual scene components.
    /// </summary>
    [Serializable]
    public sealed class FrontendLayout
    {
        [Flags] public enum Channels { Position=1, Size=2, Scale=4, Rotation=8, Color=16 }
        [Serializable] sealed class Pose
        {
            public RectTransform rect;
            public Vector2 position, size;
            public Vector3 scale;
            public Quaternion rotation;
            public Graphic graphic;
            public Color color;
        }
        sealed class Override
        {
            public Pose source;
            public Vector2 position, size, rawPosition, rawSize;
            public Vector3 scale, rawScale;
            public Quaternion rotation, rawRotation;
            public Color color, rawColor;
            public bool colorChanged, applied;
        }
        [SerializeField] List<Pose> defaults = new List<Pose>();
        readonly List<Override> edits = new List<Override>();

        public void Capture(Transform root)
        {
            defaults.Clear();
            foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                var graphic = rect.GetComponent<Graphic>();
                defaults.Add(new Pose { rect=rect, position=rect.anchoredPosition, size=rect.sizeDelta,
                    scale=rect.localScale, rotation=rect.localRotation, graphic=graphic,
                    color=graphic == null ? default : graphic.color });
            }
        }

        public void Bind(Dictionary<RectTransform,Channels> animated)
        {
            edits.Clear();
            foreach (var p in defaults)
            {
                if (p.rect == null) continue;
                if(!animated.TryGetValue(p.rect,out var channels))continue;
                bool colorChanged = (channels&Channels.Color)!=0 && p.graphic != null && p.graphic.color != p.color;
                var position=(channels&Channels.Position)!=0?p.rect.anchoredPosition-p.position:Vector2.zero;
                var size=(channels&Channels.Size)!=0?p.rect.sizeDelta-p.size:Vector2.zero;
                var scale=(channels&Channels.Scale)!=0?new Vector3(Ratio(p.rect.localScale.x,p.scale.x),
                    Ratio(p.rect.localScale.y,p.scale.y),Ratio(p.rect.localScale.z,p.scale.z)):Vector3.one;
                var rotation=(channels&Channels.Rotation)!=0?Quaternion.Inverse(p.rotation)*p.rect.localRotation:Quaternion.identity;
                if(position==Vector2.zero&&size==Vector2.zero&&scale==Vector3.one&&rotation==Quaternion.identity&&!colorChanged)continue;
                edits.Add(new Override { source=p, position=position,size=size,scale=scale,rotation=rotation,
                    colorChanged=colorChanged, color=colorChanged?p.graphic.color:default });
            }
        }
        static float Ratio(float edited, float initial) => Mathf.Abs(initial)<.00001f ? 1 : edited/initial;
        public void BeforeAnimation()
        {
            foreach (var e in edits)
            {
                if (!e.applied || e.source.rect == null) continue;
                var r=e.source.rect;
                r.anchoredPosition=e.rawPosition; r.sizeDelta=e.rawSize;
                r.localScale=e.rawScale; r.localRotation=e.rawRotation;
                if(e.colorChanged)e.source.graphic.color=e.rawColor;
                e.applied=false;
            }
        }
        // Cards must receive their authored pose before computing the hand's actual grip.
        public void Apply(RectTransform rect)
        {
            foreach(var e in edits) if(e.source.rect==rect) Apply(e);
        }
        public void AfterAnimation() { foreach(var e in edits) Apply(e); }
        static void Apply(Override e)
        {
            if(e.applied || e.source.rect==null)return;
            var r=e.source.rect;
            e.rawPosition=r.anchoredPosition; e.rawSize=r.sizeDelta;
            e.rawScale=r.localScale; e.rawRotation=r.localRotation;
            r.anchoredPosition+=e.position; r.sizeDelta+=e.size;
            r.localScale=Vector3.Scale(r.localScale,e.scale); r.localRotation*=e.rotation;
            if(e.colorChanged)
            {
                e.rawColor=e.source.graphic.color;
                var c=e.color; c.a*=e.rawColor.a;
                e.source.graphic.color=c;
            }
            e.applied=true;
        }
    }
}
