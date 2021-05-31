using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;


[ExecuteInEditMode]
public class CommandBufferCaustics : MonoBehaviour
{
    const uint MAX_BLUR_KERNEL_RADIUS = 32; //The kernel is symmetric, so the maximum total box filter edge size is 64

    public Shader m_CausticsShader;
    public Shader m_BlurShader;
    public Shader m_ApplyShader;

    [Range(0, 128)]
    public int m_CausticSampleCount;
    [Range(0, 512)]
    public int m_CausticSampleDistance;
    [Range(0, MAX_BLUR_KERNEL_RADIUS)]
    public int m_GaussKernelRadius;
    public float m_CausticStrength;
    public float m_GaussDeviation;

    private Camera m_Camera;
    private RenderTexture m_OutputTexture;

    private Material m_CausticsMaterial;
    private Material m_BlurMaterial;
    private Material m_ApplyMaterial;

    // Going into the shaders
    private Vector4 m_RandomSeed;
    private float[] m_GaussKernel;


    // Assumes expected value/mean = 0 (standard normal)
    private float NormalDistribution(float x, float deviation) 
    {
        float numerator = Mathf.Exp(-0.5f * Mathf.Pow(x / deviation, 2));
        float denominator = deviation * Mathf.Sqrt(2*Mathf.PI);
        return numerator / denominator;
    }

    private void PopulateGaussianKernel(int count)
    {
        Debug.Assert(count <= MAX_BLUR_KERNEL_RADIUS);

        for (int i = 0; i < count; ++i)
        {
            m_GaussKernel[i] = NormalDistribution((i*i/m_GaussKernelRadius), m_GaussDeviation);
        }
    }

    private void UpdateRandomSeed()
    {
        m_RandomSeed = new Vector4(Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f), Random.Range(0.0f, 1.0f)) * (System.DateTime.Now.Second % int.MaxValue);
    }

    public void Start()
    {
        m_CausticsMaterial = new Material(m_CausticsShader);
        m_BlurMaterial = new Material(m_BlurShader);
        m_ApplyMaterial = new Material(m_ApplyShader);
        
        m_RandomSeed = new Vector4();
        m_GaussKernel = new float[MAX_BLUR_KERNEL_RADIUS];

        Camera.main.depthTextureMode = DepthTextureMode.DepthNormals;
        m_OutputTexture = new RenderTexture(Camera.main.pixelWidth, Camera.main.pixelHeight, 1);

        UpdateRandomSeed();
        PopulateGaussianKernel(m_GaussKernelRadius);

        Shader.SetGlobalFloatArray("_GaussKernelValues", m_GaussKernel);
        m_BlurMaterial.SetFloat("_KernelSize", m_GaussKernelRadius);
    }

    public void Update()
    {
        // Update our random seeds for shaders
        UpdateRandomSeed();
        Shader.SetGlobalVector("_RandomSeed", m_RandomSeed);
    }

    public void OnWillRenderObject()
    {

        if (m_Camera == null)
        {
            m_Camera = GetComponentInParent<Camera>();
        }
        else
        { 
            return;
        }

        var causticsGenerationBuffer = new CommandBuffer();
        causticsGenerationBuffer.name = "Screen Space Caustics";

        // We render first to the causticOutputID, first blur pass renders to temporaryTextureID, second renders to blurOutputID
        int temporaryTextureID = Shader.PropertyToID("_CausticBlankTexture");
        int causticOutputID = Shader.PropertyToID("_CausticsOutputTexture");
        int blurOutputID = Shader.PropertyToID("_BlurOutputTexture");

        Shader.SetGlobalFloat("_CausticStrength", m_CausticStrength);
        Shader.SetGlobalInt("_SampleCount", m_CausticSampleCount);
        Shader.SetGlobalInt("_SampleDistance", m_CausticSampleDistance);

        causticsGenerationBuffer.GetTemporaryRT(temporaryTextureID, -1, -1);
        causticsGenerationBuffer.GetTemporaryRT(causticOutputID, -1, -1);
        causticsGenerationBuffer.GetTemporaryRT(blurOutputID, -1, -1);

        // Caustics Pass
        causticsGenerationBuffer.Blit(temporaryTextureID, causticOutputID, m_CausticsMaterial);

        // Horizontal Gaussian Blur Pass
        causticsGenerationBuffer.SetGlobalVector("_PassDirection", new Vector4(1, 0, 0, 0));
        causticsGenerationBuffer.Blit(causticOutputID, blurOutputID, m_BlurMaterial);

        // Vertical Gaussian Blur Pass
        causticsGenerationBuffer.SetGlobalVector("_PassDirection", new Vector4(0, 1, 0, 0));
        causticsGenerationBuffer.Blit(blurOutputID, m_OutputTexture, m_BlurMaterial);

        m_Camera.AddCommandBuffer(CameraEvent.BeforeLighting, causticsGenerationBuffer);

        Shader.SetGlobalTexture("_CausticsBuffer", m_OutputTexture);
        
        var causticsApplicationBuffer = new CommandBuffer();
        causticsGenerationBuffer.name = "Apply Caustics to Render Target";
        
        causticsApplicationBuffer.GetTemporaryRT(temporaryTextureID, m_Camera.pixelWidth, m_Camera.pixelHeight);
        causticsApplicationBuffer.Blit(BuiltinRenderTextureType.CameraTarget, temporaryTextureID, m_ApplyMaterial);
        causticsApplicationBuffer.Blit(temporaryTextureID, BuiltinRenderTextureType.CameraTarget);

        m_Camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, causticsApplicationBuffer);
    }
}
