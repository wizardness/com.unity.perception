Shader "Perception/TestShader2"
{
    Properties
    {
        _BaseColor("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
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
            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct in_vert
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct vertexToFragment
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            vertexToFragment semanticSegmentationVertexStage (in_vert vertWorldSpace)
            {
                vertexToFragment vertScreenSpace;
                vertScreenSpace.vertex = UnityObjectToClipPos(vertWorldSpace.vertex);
                vertScreenSpace.uv = TRANSFORM_TEX(vertWorldSpace.uv, _MainTex);
                return vertScreenSpace;
            }

            fixed4 semanticSegmentationFragmentStage (vertexToFragment vertScreenSpace) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, vertScreenSpace.uv);
                return col * _BaseColor;
            }

            ENDCG
        }
    }
}
