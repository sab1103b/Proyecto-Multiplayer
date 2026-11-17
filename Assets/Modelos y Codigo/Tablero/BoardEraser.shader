// Shader del borrador: pinta (0,0,0,0) en la Render Texture del tablero
// y restablece la profundidad al plano lejano.
//
// - Alpha 0  -> el Shader Graph del tablero lo trata como "sin dibujo" (transparente).
// - Profundidad lejana -> la Draw Camera no limpia el depth entre frames, así que sin
//   esto los trazos viejos seguirían bloqueando (ZTest) los trazos nuevos en esa zona.
//
// Úsalo SOLO en un objeto de la capa "Eraser", que la Draw Camera renderiza
// y la cámara principal (XR) NO renderiza.
Shader "Custom/BoardEraser"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+100"   // después de los marcadores (Geometry = 2000)
        }

        Pass
        {
            Name "Erase"
            Blend One Zero   // sobrescribe el píxel, sin mezclar
            ZWrite On
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };
            struct FragOut
            {
                half4 color : SV_Target;
                float depth : SV_Depth;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            FragOut frag(Varyings IN)
            {
                FragOut o;
                o.color = half4(0, 0, 0, 0);
                o.depth = UNITY_RAW_FAR_CLIP_VALUE;
                return o;
            }
            ENDHLSL
        }
    }
}
