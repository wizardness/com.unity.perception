Shader "Perception/TestShader"
{
    Properties
    {
        _BaseColor("Color", Color) = (1,1,1,1)
        [PerObjectData] LabelingId("Labeling Id", Vector) = (0,0,0,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Lighting Off
        LOD 100


        Pass
        {
            CGPROGRAM

            #pragma vertex semanticSegmentationVertexStage
            #pragma fragment semanticSegmentationFragmentStage

            #include "UnityCG.cginc"

            fixed4 _BaseColor;
            float4 LabelingId;

            struct in_vert
            {
                float4 vertex : POSITION;
            };

            struct vertexToFragment
            {
                float4 vertex : SV_POSITION;
            };

            vertexToFragment semanticSegmentationVertexStage (in_vert vertWorldSpace)
            {
                vertexToFragment vertScreenSpace;
                vertScreenSpace.vertex = UnityObjectToClipPos(vertWorldSpace.vertex);
                return vertScreenSpace;
            }

            fixed4 semanticSegmentationFragmentStage (vertexToFragment vertScreenSpace) : SV_Target
            {
                fixed4 color = LabelingId;
                return color;
            }

            ENDCG
        }
    }
}
