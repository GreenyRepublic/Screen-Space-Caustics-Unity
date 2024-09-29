using UnityEngine;
using UnityEngine.Rendering;

public class CommandBufferCaustics : MonoBehaviour
{
    const uint MAX_BLUR_KERNEL_RADIUS = 32; //The kernel is symmetric, so the maximum total box filter edge size is 64
    const string COMMAND_BUFFER_NAME = "Screen Space Caustics";

    const string CAUSTIC_OUTPUT_SHADER_PROPERTY_NAME = "_CausticsOutputTexture";
    const string BLUR_OUTPUT_SHADER_PROPERTY_NAME = "_BlurOutputTexture";
    const string CAUSTIC_INPUT_SHADER_PROPERTY_NAME = "_CausticsBuffer";

    const string CAUSTIC_STRENGTH_SHADER_PROPERTY_NAME = "_CausticStrength";
    const string CAUSTIC_SAMPLE_COUNT_SHADER_PROPERTY_NAME = "_SampleCount";
    const string CAUSTIC_SAMPLE_DISTANCE_SHADER_PROPERTY_NAME = "_SampleDistance";

    const string GAUSS_KERNEL_VALUES_SHADER_PROPERTY_NAME = "_GaussKernelValues";
    const string GAUSS_KERNEL_SIZE_SHADER_PROPERTY_NAME = "_KernelSize";
    const string RANDOM_SEED_SHADER_PROPERTY_NAME = "_RandomSeed";

    const string BLUR_DIRECTION_SHADER_PROPERTY_NAME = "_PassDirection";

    const CameraEvent GENERATE_CAMERA_EVENT = CameraEvent.BeforeLighting;
    const CameraEvent COMPOSITE_CAMERA_EVENT = CameraEvent.BeforeImageEffectsOpaque;

    private Camera m_Camera;
    private RenderTexture m_OutputTexture;

    // Shader inputs
    //
    private Vector4 m_RandomSeed;
    private float[] m_GaussKernel;

    [Range(0, 64)]
    public int m_CausticStrength;

    [Range(0, 512)]
    public int m_CausticSampleCount;

    [Range(0, 512)]
    public int m_CausticSampleDistance;

    [Range(0, MAX_BLUR_KERNEL_RADIUS)]
    public int m_GaussKernelRadius;

    public float m_GaussDeviation;

    [SerializeField]
    private Shader m_CausticsShader;
    [SerializeField]
    private Shader m_BlurShader;
    [SerializeField]
    private Shader m_FinalBlitShader;
    [SerializeField]
    private Shader m_MedianFilterShader;

    private Material m_CausticsMaterial;
    private Material m_BlurMaterial;
    private Material m_FinalBlitMaterial;
    private Material m_MedianFilterMaterial;


    private CommandBuffer m_CausticGenerationBuffer;
    private CommandBuffer m_CausticsCompositionBuffer;


    // Assumes expected value/mean = 0 (standard normal)
    private float NormalDistribution(float x, float deviation) 
    {
        float numerator = Mathf.Exp(-0.5f * Mathf.Pow(x / deviation, 2));
        float denominator = deviation * Mathf.Sqrt(2*Mathf.PI);
        return numerator / denominator;
    }

    private float[] InitGaussKernel(int count)
    {
        Debug.Assert(count <= MAX_BLUR_KERNEL_RADIUS);

        var kernel = new float[MAX_BLUR_KERNEL_RADIUS];

        for (int i = 0; i < count; ++i)
        {
            kernel[i] = NormalDistribution((i*i/m_GaussKernelRadius), m_GaussDeviation);
        }
        return kernel;
    }

    private void InitRandomSeed()
    {
        m_RandomSeed = new Vector4(Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f)) * (System.DateTime.Now.Second % int.MaxValue);
    }

    public void Start()
    {
        m_Camera = GetComponentInParent<Camera>();
        Debug.Assert(m_Camera != null);

        Debug.Assert(m_CausticsShader != null);
        Debug.Assert(m_BlurShader != null);
        Debug.Assert(m_FinalBlitShader != null);

        m_CausticsMaterial = new Material(m_CausticsShader);
        m_BlurMaterial = new Material(m_BlurShader);
        m_FinalBlitMaterial = new Material(m_FinalBlitShader);
        m_MedianFilterMaterial = new Material(m_MedianFilterShader);

        m_RandomSeed = new Vector4();
        m_GaussKernel = new float[MAX_BLUR_KERNEL_RADIUS];

        Camera.main.depthTextureMode = DepthTextureMode.DepthNormals;
        m_OutputTexture = new RenderTexture(Camera.main.pixelWidth, Camera.main.pixelHeight, 1);

        InitRandomSeed();
        m_GaussKernel = InitGaussKernel(m_GaussKernelRadius);

        Shader.SetGlobalFloatArray(GAUSS_KERNEL_VALUES_SHADER_PROPERTY_NAME, m_GaussKernel);
        m_BlurMaterial.SetFloat(GAUSS_KERNEL_SIZE_SHADER_PROPERTY_NAME, m_GaussKernelRadius);
    }

    public void Update()
    {
        //  Update the material properties
        //
        m_CausticsMaterial.SetFloat(CAUSTIC_STRENGTH_SHADER_PROPERTY_NAME, m_CausticStrength);
        m_CausticsMaterial.SetInteger(CAUSTIC_SAMPLE_COUNT_SHADER_PROPERTY_NAME, m_CausticSampleCount);
        m_CausticsMaterial.SetFloat(CAUSTIC_SAMPLE_DISTANCE_SHADER_PROPERTY_NAME, m_CausticSampleDistance);

        // Update our random seeds for shaders
        InitRandomSeed();
        Shader.SetGlobalVector(RANDOM_SEED_SHADER_PROPERTY_NAME, m_RandomSeed);

        GenerateCommandBuffers();
    }

    public void GenerateCommandBuffers()
    {
        m_Camera.RemoveAllCommandBuffers();

        m_CausticGenerationBuffer = new CommandBuffer();
        m_CausticGenerationBuffer.name = COMMAND_BUFFER_NAME;

        // We render first to the causticOutputID, first blur pass renders to temporaryTextureID, second renders to blurOutputID
        //
        int causticOutputTargetID = Shader.PropertyToID(CAUSTIC_OUTPUT_SHADER_PROPERTY_NAME);
        int blurOutputTargetID = Shader.PropertyToID(BLUR_OUTPUT_SHADER_PROPERTY_NAME);

        int newWidth = m_Camera.pixelWidth;
        int newHeight = m_Camera.pixelHeight;

        m_CausticGenerationBuffer.GetTemporaryRT(causticOutputTargetID, newWidth, newHeight);
        m_CausticGenerationBuffer.GetTemporaryRT(blurOutputTargetID, newWidth, newHeight);

        // Caustics Pass
        m_CausticGenerationBuffer.Blit(null, causticOutputTargetID, m_CausticsMaterial);

        //  Test a median filter pass
        //
        m_CausticGenerationBuffer.Blit(causticOutputTargetID, blurOutputTargetID, m_MedianFilterMaterial);


        // Horizontal Gaussian Blur Pass
        m_CausticGenerationBuffer.SetGlobalVector(BLUR_DIRECTION_SHADER_PROPERTY_NAME, new Vector4(1, 0, 0, 0));
        m_CausticGenerationBuffer.Blit(blurOutputTargetID, causticOutputTargetID, m_BlurMaterial);

        // Vertical Gaussian Blur Pass
        m_CausticGenerationBuffer.SetGlobalVector(BLUR_DIRECTION_SHADER_PROPERTY_NAME, new Vector4(0, 1, 0, 0));
        m_CausticGenerationBuffer.Blit(causticOutputTargetID, m_OutputTexture, m_BlurMaterial);        


        m_Camera.AddCommandBuffer(GENERATE_CAMERA_EVENT, m_CausticGenerationBuffer);

        Shader.SetGlobalTexture(CAUSTIC_INPUT_SHADER_PROPERTY_NAME, m_OutputTexture);
        
        m_CausticsCompositionBuffer = new CommandBuffer();
        m_CausticsCompositionBuffer.name = "Apply Caustics to Final Target";

        m_CausticsCompositionBuffer.Blit(null, BuiltinRenderTextureType.CameraTarget, m_FinalBlitMaterial);

        m_Camera.AddCommandBuffer(COMPOSITE_CAMERA_EVENT, m_CausticsCompositionBuffer);
    }
}
