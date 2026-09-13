using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

class ScreenSpaceCausticsRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class ScreenSpaceCausticsSettings
    {
        public bool IsEnabled = true;
        public Material CausticSampleMaterial;
        public Material GaussFilterMaterial;
        public Material MedianFilterMaterial;
        public Material FinalBlendMaterial;
        public int RenderScaleFactor;
        [Range(1,64)]
        public int GaussKernelRadius;
        public float GaussDeviation = 1.0f;
    }

    private const string CausticOutputTextureName = "_CAUSTIC_OUT";
    
    const uint MAX_BLUR_KERNEL_RADIUS = 32; //  The kernel is symmetric, so the maximum total box filter edge size is 64

    // MUST be named "settings" (lowercase) to be shown in the Render Features inspector
    //
    public ScreenSpaceCausticsSettings settings = new ScreenSpaceCausticsSettings();

    private const string GenerateLabel = "Generate Caustic Map";
    private const string FinalBlendLabel = "Blend Caustic Map";
    private const RenderPassEvent GenerateCausticsEvent = RenderPassEvent.BeforeRenderingDeferredLights;
    private const RenderPassEvent BlendCausticsEvent = RenderPassEvent.AfterRenderingOpaques;

    private ScreenSpaceCausticsGenerationRenderPass CausticsGenerationPass;
    private ScreenSpaceCausticsFinalBlendRenderPass CausticsFinalBlendPass;

    private RenderTexture CausticOutputTexture;


    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!IsValid())
        {
            return;
        }

        CausticOutputTexture = new RenderTexture(renderingData.cameraData.targetTexture.width, renderingData.cameraData.targetTexture.height, 1);

        renderer.EnqueuePass(CausticsGenerationPass);
        renderer.EnqueuePass(CausticsFinalBlendPass);
    }

    public override void Create()
    {
        float[] gaussKernel = InitGaussKernel(settings.GaussKernelRadius, settings.GaussDeviation);
        CausticsGenerationPass = new ScreenSpaceCausticsGenerationRenderPass(
            GenerateLabel,
            GenerateCausticsEvent,
            settings.CausticSampleMaterial,
            settings.GaussFilterMaterial,
            settings.MedianFilterMaterial,
            gaussKernel,
            CausticOutputTexture
            );

        CausticsFinalBlendPass = new ScreenSpaceCausticsFinalBlendRenderPass(
            FinalBlendLabel,
            BlendCausticsEvent,
            settings.FinalBlendMaterial,
            CausticOutputTexture
            );
    }

    private bool IsValid()
    {
        return settings.IsEnabled &&
            settings.CausticSampleMaterial &&
            settings.GaussFilterMaterial &&
            settings.MedianFilterMaterial &&
            settings.FinalBlendMaterial;
    }
    
    // Assumes expected value/mean = 0 (standard normal)
    private float NormalDistribution(float x, float deviation) 
    {
        float numerator = Mathf.Exp(-0.5f * Mathf.Pow(x / deviation, 2));
        float denominator = deviation * Mathf.Sqrt(2*Mathf.PI);
        return numerator / denominator;
    }

    private float[] InitGaussKernel(int count, float deviation)
    {
        Debug.Assert(count <= MAX_BLUR_KERNEL_RADIUS);

        var kernel = new float[count];

        for (int i = 0; i < count; ++i)
        {
            kernel[i] = NormalDistribution((i*i/count), deviation);
        }
        return kernel;
    }

    private void InitRandomSeed()
    {
        //m_RandomSeed = new Vector4(Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f)) * (System.DateTime.Now.Second % int.MaxValue);
    }
}

class ScreenSpaceCausticsGenerationRenderPass : ScriptableRenderPass
{
    const string CAUSTIC_OUTPUT_SHADER_PROPERTY_NAME = "_CausticsOutputTexture";
    const string BLUR_OUTPUT_SHADER_PROPERTY_NAME = "_BlurOutputTexture";
    const string BLUR_DIRECTION_SHADER_PROPERTY_NAME = "_PassDirection";

    private string ProfilerTag;
    private RenderPassEvent RenderEvent;
    private Material CausticPassMaterial;
    private Material BlurPassMaterial;
    private Material MedianPassMaterial;
    private RenderTexture OutputTarget;
    private float[] GaussianKernel;

    public ScreenSpaceCausticsGenerationRenderPass(
        string profilerTag,
        RenderPassEvent passEvent,
        Material causticPassMat,
        Material blurPassMat,
        Material medianPassMat,
        float[] gaussKernel,
        RenderTexture outputTexture)
    {
        ProfilerTag = profilerTag;
        RenderEvent = passEvent;

        CausticPassMaterial = causticPassMat;
        BlurPassMaterial = blurPassMat;
        MedianPassMaterial = medianPassMat;
        OutputTarget = outputTexture;
        GaussianKernel = gaussKernel;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer buffer = CommandBufferPool.Get(ProfilerTag);

        // We render first to the causticOutputID, first blur pass renders to temporaryTextureID, second renders to blurOutputID
        //
        int causticOutputTargetID = Shader.PropertyToID(CAUSTIC_OUTPUT_SHADER_PROPERTY_NAME);
        int blurOutputTargetID = Shader.PropertyToID(BLUR_OUTPUT_SHADER_PROPERTY_NAME);

        int newWidth = renderingData.cameraData.targetTexture.width;
        int newHeight = renderingData.cameraData.targetTexture.height;

        buffer.SetGlobalFloatArray("_GaussKernelValues", GaussianKernel);
        buffer.SetGlobalInt("_KernelSize", GaussianKernel.Length);
        buffer.GetTemporaryRT(causticOutputTargetID, newWidth, newHeight);
        buffer.GetTemporaryRT(blurOutputTargetID, newWidth, newHeight);

        // Caustics Pass
        buffer.Blit(null, causticOutputTargetID, CausticPassMaterial);

        //  Test a median filter pass
        //
        buffer.Blit(causticOutputTargetID, blurOutputTargetID, MedianPassMaterial);
        
        // Horizontal Gaussian Blur Pass
        buffer.SetGlobalVector(BLUR_DIRECTION_SHADER_PROPERTY_NAME, new Vector4(1, 0, 0, 0));
        buffer.Blit(blurOutputTargetID, causticOutputTargetID, BlurPassMaterial);

        // Vertical Gaussian Blur Pass
        buffer.SetGlobalVector(BLUR_DIRECTION_SHADER_PROPERTY_NAME, new Vector4(0, 1, 0, 0));
        buffer.Blit(causticOutputTargetID, OutputTarget, BlurPassMaterial);
        
        buffer.ReleaseTemporaryRT(causticOutputTargetID);
        buffer.ReleaseTemporaryRT(blurOutputTargetID);
        
        context.ExecuteCommandBuffer(buffer);
    }
}

class ScreenSpaceCausticsFinalBlendRenderPass : ScriptableRenderPass
{
    private string ProfilerTag;
    private RenderPassEvent RenderEvent;
    private Material FinalBlendMaterial;
    private RenderTexture InputTexture;

    public ScreenSpaceCausticsFinalBlendRenderPass(string profilerTag, RenderPassEvent passEvent, Material finalBlendMat, RenderTexture inputTex)
    {
        ProfilerTag = profilerTag;
        RenderEvent = passEvent;

        FinalBlendMaterial = finalBlendMat;
        InputTexture = inputTex;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer buffer = CommandBufferPool.Get(ProfilerTag);
        buffer.Blit(null, renderingData.cameraData.targetTexture, FinalBlendMaterial);
        context.ExecuteCommandBuffer(buffer);
    }
}