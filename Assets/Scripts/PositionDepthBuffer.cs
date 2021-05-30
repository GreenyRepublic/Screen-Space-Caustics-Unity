using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;


public class PositionDepthBuffer : MonoBehaviour
{
    public Shader m_PositionDepthShader;
    private Camera m_Camera;

    private RenderTexture m_OutputTexture;

    public void Start()
    {
        var mainCamera = GetComponentInParent<Camera>();
        m_Camera = gameObject.AddComponent<Camera>();
        m_Camera.CopyFrom(mainCamera);
        m_Camera.depthTextureMode = DepthTextureMode.None;
        
        m_OutputTexture = new RenderTexture(m_Camera.pixelWidth, m_Camera.pixelHeight, 1, RenderTextureFormat.ARGBFloat);
        m_Camera.targetTexture = m_OutputTexture;
        m_Camera.SetReplacementShader(m_PositionDepthShader, "RenderType");
    }

    public void Update()
    {
    }

    public void OnPreRender()
    {
        Shader.SetGlobalTexture("_WorldPositionDepthTexture", m_OutputTexture);
    }
}
