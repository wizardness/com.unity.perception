Shader "Perception/SemanticSegmentation"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
       // _EnableTransparency ("Enable Transparency", Range(0,1)) = 1
       // _TransparencyThreshold ("Transparency Threshold", Range(0,1)) = 1
        _TextureIsSegmentationMask ("Use Texture as Segmentation Mask", Range(0,1)) = 0
        [PerObjectData] LabelingId("Labeling Id", Vector) = (0,0,0,1)
    }

    //HLSLINCLUDE

    // #pragma target 4.5
    // #pragma only_renderers d3d11 ps4 xboxone vulkan metal switch

    //enable GPU instancing support
    //#pragma multi_compile_instancing

    //ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        LOD 100
        //Cull Off

//        Pass
//        {
//            ZWrite On
//            ColorMask 0
//        }

        Pass
        {

            CGPROGRAM

            #pragma vertex semanticSegmentationVertexStage
            #pragma fragment semanticSegmentationFragmentStage

            #include "UnityCG.cginc"

            float4 LabelingId;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _BaseColor;
            //float _TransparencyThreshold;
            float _TextureIsSegmentationMask;
            //float _EnableTransparency;

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

                fixed4 outColor = LabelingId;

                if (_TextureIsSegmentationMask == 1)
                {
                    if (col.r == 0 && col.g == 0 && col.b == 0)
                        col = fixed4(0,0,0,1);

                    return col;
                }

                // //float opacity = col.a * _BaseColor.a;
                // float opacity = 2;
                // if (opacity < _TransparencyThreshold)
                //     outColor.a = 0;
                // else
                //     outColor.a = 1;

                return outColor;
            }

            ENDCG
        }
    }
}
