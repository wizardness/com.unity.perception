Shader "Perception/InstanceSegmentation"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _EnableTransparency ("Enable Transparency", Range(0,1)) = 1
        _TransparencyThreshold ("Transparency Threshold", Range(0,1)) = 1
        _TextureIsSegmentationMask ("Use Texture as Segmentation Mask", Range(0,1)) = 0
        [PerObjectData] _SegmentationId("Segmentation ID", vector) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        LOD 100

        Pass
        {
            Tags { "LightMode" = "SRP" }


            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Packing.hlsl"

            sampler2D _MainTex;
            fixed4 _BaseColor;
            float4 _MainTex_ST;
            float _TransparencyThreshold;
            float _TextureIsSegmentationMask;
            float _EnableTransparency;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float4 _SegmentationId;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                fixed4 outColor = _SegmentationId;

                if (_TextureIsSegmentationMask == 1)
                {
                    if (col.r == 0 && col.g == 0 && col.b == 0)
                    {
                        outColor = fixed4(0,0,0,1);
                    }
                    return outColor;
                }

                float opacity = 1 * _BaseColor.a;
                if (opacity < _TransparencyThreshold)
                    outColor.a = 0;
                else
                    outColor.a = 1;
                return outColor;
            }
            ENDCG
        }
    }
}
