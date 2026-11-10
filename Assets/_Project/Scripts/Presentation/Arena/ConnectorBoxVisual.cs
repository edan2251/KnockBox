using UnityEngine;
namespace Knockbox.Presentation
{
    /// <summary>The same translucent fill and outline in MapAuthoring and the match; the body remains a solid physical rectangle.</summary>
    public static class ConnectorBoxVisual
    {
        public static void Apply(SpriteRenderer body,ShapeLibrary shapes,bool connector,Vector2 size)
        {
            body.enabled=true;
            if(connector)body.color=PaletteAsset.ConnectorFillColor;
            var root=body.transform.Find("Connector_Outline");
            if(root==null && !connector)return;
            if(root==null)
            {
                root=new GameObject("Connector_Outline").transform;root.SetParent(body.transform,false);
                for(int i=0;i<4;i++)ShapeLibrary.Create(root,"Edge_"+i,shapes.Square,SortingLayers.Terrain,6,PropView.ConnectorColor);
            }
            root.gameObject.SetActive(connector);if(!connector)return;
            var units=(Vector2)body.sprite.bounds.size;
            float tx=Mathf.Min(.18f,.08f/Mathf.Max(.05f,size.x))*units.x;
            float ty=Mathf.Min(.18f,.08f/Mathf.Max(.05f,size.y))*units.y;
            for(int i=0;i<4;i++)
            {
                var sr=root.GetChild(i).GetComponent<SpriteRenderer>();bool horizontal=i<2;float sign=(i&1)==0?-1:1;
                sr.transform.localPosition=horizontal?new Vector3(0,sign*(units.y-ty)*.5f,0):new Vector3(sign*(units.x-tx)*.5f,0,0);
                ShapeLibrary.SetSize(sr,horizontal?units.x:tx,horizontal?ty:units.y);
            }
        }
    }
}
