using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine.Perception.GroundTruth
{
    internal abstract class GroundTruthCrossPipelinePass : IGroundTruthGenerator
    {
        bool m_IsActivated;

        protected GroundTruthCrossPipelinePass()
        {
        }

        public virtual void Setup()
        {
            EnsureActivated();
        }

        public void Execute(ScriptableRenderContext renderContext, CommandBuffer cmd, Camera camera, CullingResults cullingResult)
        {
            ExecutePass(renderContext, cmd, camera, cullingResult);
        }

        protected abstract void ExecutePass(ScriptableRenderContext renderContext, CommandBuffer cmd, Camera camera, CullingResults cullingResult);

        public void EnsureActivated()
        {
            if (!m_IsActivated)
            {
                var labelSetupSystem = World.DefaultGameObjectInjectionWorld?.GetExistingSystem<GroundTruthLabelSetupSystem>();
                labelSetupSystem?.Activate(this);
                m_IsActivated = true;
            }
        }

        public void Cleanup()
        {
            var labelSetupSystem = World.DefaultGameObjectInjectionWorld?.GetExistingSystem<GroundTruthLabelSetupSystem>();
            labelSetupSystem?.Deactivate(this);
        }

        protected RendererListDesc CreateRendererListDesc(Camera camera, CullingResults cullingResult, string overrideMaterialPassName, int overrideMaterialPassIndex, Material overrideMaterial, LayerMask layerMask /*, PerObjectData perObjectData*/)
        {
            var shaderPasses = new[]
            {
                new ShaderTagId("Forward"), // HD Lit shader
                new ShaderTagId("ForwardOnly"), // HD Unlit shader
                new ShaderTagId("SRPDefaultUnlit"), // Cross SRP Unlit shader
                new ShaderTagId("UniversalForward"), // URP Forward
                new ShaderTagId("LightweightForward"), // LWRP Forward
                new ShaderTagId(overrideMaterialPassName), // The override material shader
            };

            var stateBlock = new RenderStateBlock(0)
            {
                depthState = new DepthState(true, CompareFunction.LessEqual),
            };

            var result = new RendererListDesc(shaderPasses, cullingResult, camera)
            {
                rendererConfiguration = PerObjectData.None,
                renderQueueRange = new RenderQueueRange { lowerBound = 0, upperBound = 5000 },
                sortingCriteria = SortingCriteria.CommonOpaque,
                excludeObjectMotionVectors = false,
                overrideMaterial = overrideMaterial,
                overrideMaterialPassIndex = overrideMaterialPassIndex,
                stateBlock = stateBlock,
                layerMask = layerMask,
            };
            return result;
        }

        public static void DrawRendererList(ScriptableRenderContext renderContext, CommandBuffer cmd, RendererList rendererList)
        {
            if (!rendererList.isValid)
                throw new ArgumentException("Invalid renderer list provided to DrawRendererList");

            // This is done here because DrawRenderers API lives outside command buffers so we need to make call this before doing any DrawRenders or things will be executed out of order
            renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            if (rendererList.stateBlock == null)
                renderContext.DrawRenderers(rendererList.cullingResult, ref rendererList.drawSettings, ref rendererList.filteringSettings);
            else
            {
                var renderStateBlock = rendererList.stateBlock.Value;
                renderContext.DrawRenderers(rendererList.cullingResult, ref rendererList.drawSettings, ref rendererList.filteringSettings, ref renderStateBlock);
            }
        }

        public abstract void SetupMaterialProperties(MaterialPropertyBlock mpb, Renderer meshRenderer, Labeling labeling, uint instanceId);
    }
}
