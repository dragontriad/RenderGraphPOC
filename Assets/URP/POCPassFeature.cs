using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class POCPassFeature : ScriptableRendererFeature
{
    public Material _material;
    private static readonly int _BlitTextureID = Shader.PropertyToID("_BlitTexture");
    private static readonly int _BlitScaleBiasID = Shader.PropertyToID("_BlitScaleBias");

    private class PassData
    {
        internal Material material;
        internal TextureHandle sampledTexture;
    }
    public class CustomRenderPass : ScriptableRenderPass
    {
        public Material _material;
        internal static Material s_FrameBufferFetchMaterial;
        private string _PassName = "TestPOC";

        private static MaterialPropertyBlock s_SharedPropertyBlock = null;
        public CustomRenderPass(Material material) 
        {
            s_SharedPropertyBlock = new MaterialPropertyBlock();
            this._material = material;
        }
        // This class stores the data needed by the RenderGraph pass.
        // It is passed as a parameter to the delegate function that executes the RenderGraph pass.


        // This static method is passed as the RenderFunc delegate to the RenderGraph render pass.
        // It is used to execute draw commands.
        static void ExecutePass(PassData data, RasterGraphContext context)
        {
        }

        // RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
        // FrameData is a context container through which URP resources can be accessed and managed.
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            // We need a copy of the color texture as input for the blit with material
            var colCopyDesc = renderGraph.GetTextureDesc(resourceData.afterPostProcessColor);
            colCopyDesc.name = "_TempColorCopy";  // Changing the name
            TextureHandle copiedColorTexture = renderGraph.CreateTexture(colCopyDesc);

            // First blit, simply copying color to intermediary texture so it can be used as input in next pass
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(_PassName + "_GetCameraPass", out var passData))
            {
                // Setting the URP active color texture as the source for this pass
                passData.sampledTexture = resourceData.activeColorTexture;

                // Setting input texture to sample
                builder.UseTexture(resourceData.activeColorTexture, AccessFlags.Read);

                // Setting output attachment
                builder.SetRenderAttachment(copiedColorTexture, 0, AccessFlags.Write);

                // Execute step, simple copy
                builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) =>
                {
                    ExecuteCopyColorPass(rgContext.cmd, data.sampledTexture);
                });
            }
            #region Additional Passes Not Allowed
            //using (var builder = renderGraph.AddRasterRenderPass<PassData>(_PassName + "_TestMaterial1Pass", out var passData))
            //{
            //    // Setting the temp color texture as the source for this pass
            //    passData.sampledTexture = copiedColorTexture;
            //    passData.material = _material;

            //    // Setting input texture to sample
            //    builder.UseTexture(copiedColorTexture, AccessFlags.Read);

            //    // Setting output attachment
            //    builder.SetRenderAttachment(copiedColorTexture, 0, AccessFlags.Write);

            //    // Execute step, second blit with the dither effect
            //    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) =>
            //    {
            //        ExecuteMainPass(rgContext.cmd, data.material, data.sampledTexture);
            //    });
            //}

            //using (var builder = renderGraph.AddRasterRenderPass<PassData>(_PassName + "_TestMaterialPass", out var passData))
            //{
            //    // Setting the temp color texture as the source for this pass
            //    passData.sampledTexture = copiedColorTexture;
            //    passData.material = _material;

            //    // Setting input texture to sample
            //    builder.UseTexture(copiedColorTexture, AccessFlags.Read);

            //    // Setting output attachment
            //    builder.SetRenderAttachment(copiedColorTexture, 0, AccessFlags.Write);

            //    // Execute step, second blit with the dither effect
            //    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) =>
            //    {
            //        ExecuteMainPass(rgContext.cmd, data.material, data.sampledTexture);
            //    });
            //}
            #endregion
            // Second blit with material, applying dither effect
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(_PassName + "_CameraUpdatePass", out var passData))
            {
                // Setting the temp color texture as the source for this pass
                passData.sampledTexture = copiedColorTexture;
                passData.material = _material;

                // Setting input texture to sample
                builder.UseTexture(copiedColorTexture, AccessFlags.Read);

                // Setting output attachment
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);

                // Execute step, second blit with the dither effect
                builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) =>
                {
                    ExecuteMainPass(rgContext.cmd, data.material, data.sampledTexture);
                });
            }
        }
        private static void ExecuteCopyColorPass(RasterCommandBuffer cmd, RTHandle sampledTexture)
        {
            if (sampledTexture != null)
            {
                Blitter.BlitTexture(cmd, sampledTexture, new Vector4(1, 1, 0, 0), 0, false);
            }
            else
            {
                // Render Graph with optimization
                cmd.DrawProcedural(Matrix4x4.identity, s_FrameBufferFetchMaterial, 1, MeshTopology.Triangles, 3);
            }
        }
        private static void ExecuteMainPass(RasterCommandBuffer cmd, Material material, RTHandle sampledTexture)
        {
            s_SharedPropertyBlock.Clear();
            if (sampledTexture != null)
            {
                s_SharedPropertyBlock.SetTexture(_BlitTextureID, sampledTexture);
            }

            s_SharedPropertyBlock.SetVector(_BlitScaleBiasID, new Vector4(1, 1, 0, 0));
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, s_SharedPropertyBlock);
        }

        // NOTE: This method is part of the compatibility rendering path, please use the Render Graph API above instead.
        // This method is called before executing the render pass.
        // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
        // When empty this render pass will render to the active camera render target.
        // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
        // The render pipeline will ensure target setup and clearing happens in a performant manner.
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        // NOTE: This method is part of the compatibility rendering path, please use the Render Graph API above instead.
        // Here you can implement the rendering logic.
        // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
        // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
        // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
        }

        // NOTE: This method is part of the compatibility rendering path, please use the Render Graph API above instead.
        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }
    }

    CustomRenderPass m_ScriptablePass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new CustomRenderPass(_material);

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_ScriptablePass);
    }
}
