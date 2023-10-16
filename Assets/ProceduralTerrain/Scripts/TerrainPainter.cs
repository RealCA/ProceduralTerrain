using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[ExecuteInEditMode]
public class TerrainPainter : MonoBehaviour
{
    public ComputeShader paintShader;
    public bool m_ApplyBlur;
    public ComputeShader blurKernelShader;
    public ComputeShader blurShader;
    private Terrain terrain;
    public float[,,] splats;

    public Elevation[] textureRanges = new Elevation[]
    {
        new Elevation()
        {
            name = "Snow",
            color=new Color(216 / 255f, 222 / 255f, 233 / 255f),
            height= 0.85f,
            minElevation = 0.45f,
            maxElevation = 0.85f
        },
        new Elevation()
        {
            name = "Snow0",
            color =new Color(121 / 255f, 133 / 255f, 159 / 255f),
            height= 0.45f,
            minElevation = 0.85f,
            maxElevation = 0.38f
        },

        new Elevation()
        {
            name = "Snow1",
            color = new Color(76 / 255f, 86 / 255f, 106 / 255f),
            height= 0.38f,
            minElevation = 0.38f,
            maxElevation = 0.20f
        },
        new Elevation()
        {
            name = "Grass0",
            color = new Color(163 / 255f, 190 / 255f, 140 / 255f),
            height= 0.20f,
            minElevation = 0.20f,
            maxElevation = 0.15f
        },
        new Elevation()
        {
            name = "Grass",
            color=new Color(143 / 255f, 176 / 255f, 115 / 255f),
            height= 0.15f,
            minElevation = 0.15f,
            maxElevation = 0.115f
        },
        new Elevation()
        {
            name = "Sand",
            color=new Color(235 / 255f, 203 / 255f, 139 / 255f),
            height= 0.115f,
            minElevation = 0.115f,
            maxElevation = 0.1f
        },
        new Elevation()
        {
            name = "Water0",
            color=new Color(115 / 255f, 146 / 255f, 183 / 255f),
            height= 0.1f,
            minElevation = 0.1f,
            maxElevation = 0.0f
        },
        new Elevation()
        {
            name = "Water",
            color=new Color(94 / 255f, 129 / 255f, 172 / 255f),
            height = 0,
            minElevation = 0.0f,
            maxElevation = 0.0f
        },
    }; 
    public int blurRadius = 3; // Adjust this value to control the blur strength.
    private float[] Heights;
    private ComputeBuffer HeightsBuffer;
    private TextureRanges[] _textureRanges;
    private ComputeBuffer textureRangesBuffer;
    private ComputeBuffer splatmapDataBuffer;
    private ComputeBuffer kernelBuffer;
    public RenderTexture contourRTexture;
    private struct TextureRanges
    {
        public float minElevation;
        public float maxElevation;
        public float transitionWidth;
    }
    void Start()
    {
        terrain = GetComponent<Terrain>();
        if (Heights == null || Heights.Length == 0)
        {
            UpdateHeightBuffer();
        }
        PaintTerrain();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!terrain)
            terrain = GetComponent<Terrain>();

        if (HeightsBuffer == null || HeightsBuffer.count == 0 || Heights == null || Heights.Length == 0 || Heights.Length != terrain.terrainData.alphamapResolution * 2)
        {
            UpdateHeightBuffer();
        }
        if (!contourRTexture)
        {
            contourRTexture = new RenderTexture(terrain.terrainData.alphamapWidth, terrain.terrainData.alphamapHeight, 32);
            contourRTexture.enableRandomWrite = true;
            contourRTexture.Create();
        }
        UnityEditor.EditorApplication.delayCall = PaintTerrain;
    }
#endif

    public void UpdateHeightBuffer()
    {
        Heights = new float[terrain.terrainData.alphamapWidth * terrain.terrainData.alphamapHeight];
        int index = 0;
        for (int x = 0; x < terrain.terrainData.alphamapWidth; x++)
        {
            for (int z = 0; z < terrain.terrainData.alphamapHeight; z++)
            {

                float normalizedZ = (float)x / (terrain.terrainData.alphamapWidth - 1);
                float normalizedX = (float)z / (terrain.terrainData.alphamapHeight - 1);

                float elevation = terrain.terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);

                Heights[index] = elevation;
                index++;
            }
        }

        HeightsBuffer = new ComputeBuffer(Heights.Length, sizeof(float));
        HeightsBuffer.SetData(Heights);
    }

    private void OnDisable()
    {
        HeightsBuffer.Release();
        textureRangesBuffer.Release();
        splatmapDataBuffer.Release();
        kernelBuffer.Release();
        HeightsBuffer = null;
        textureRangesBuffer = null;
        splatmapDataBuffer = null;
        kernelBuffer = null;
    }

    void OnTerrainChanged(TerrainChangedFlags flags)
    {
        if (!terrain) 
            terrain = GetComponent<Terrain>();
        if (flags == TerrainChangedFlags.Heightmap || flags == TerrainChangedFlags.HeightmapResolution || flags == TerrainChangedFlags.DelayedHeightmapUpdate)
        {
            UpdateHeightBuffer();
        }
    }

    public void PaintTerrain()
    {
        ApplyTextures(terrain);
    }
    void ApplyTextures(Terrain terrain)
    {
        terrain.terrainData.terrainLayers = textureRanges.Select(x =>
        new TerrainLayer()
        {
            name = x.name,
            diffuseTexture = (x.useTexture) ? x.texture : CreateTextureFromColor(x.color, 1024, 1024),
            smoothness = 0,
            metallic = 0,
        }
        ).ToArray();
        _textureRanges = textureRanges.Select(x => 
        new TextureRanges() 
        { 
            minElevation = x.minElevation, 
            maxElevation = x.maxElevation, 
            transitionWidth = x.transitionWidth 
        }).ToArray();
        if(textureRangesBuffer == null || textureRangesBuffer.count != _textureRanges.Length)
            textureRangesBuffer = new ComputeBuffer(_textureRanges.Length,sizeof(float)*3);
        if(splatmapDataBuffer == null || splatmapDataBuffer.count != terrain.terrainData.alphamapWidth * terrain.terrainData.alphamapHeight * _textureRanges.Length)
            splatmapDataBuffer = new ComputeBuffer(terrain.terrainData.alphamapWidth * terrain.terrainData.alphamapHeight * _textureRanges.Length,sizeof(float));
        textureRangesBuffer.SetData(_textureRanges);
        //float[,] heights = terrain.terrainData.GetHeights(0, 0, terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution);
        float[,,] splatmapData = new float[terrain.terrainData.alphamapWidth, terrain.terrainData.alphamapHeight, textureRanges.Length];

        /*for (int x = 0; x < terrain.terrainData.alphamapWidth; x++)
        {
            for (int z = 0; z < terrain.terrainData.alphamapHeight; z++)
            {
                float normalizedZ = (float)x / (terrain.terrainData.alphamapWidth - 1);
                float normalizedX = (float)z / (terrain.terrainData.alphamapHeight - 1);

                float elevation = terrain.terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);



                for (int i = 0; i < textureRanges.Length; i++)
                {
                    float min = textureRanges[i].minElevation;
                    float max = textureRanges[i].maxElevation;

                    float transitionFactor = Mathf.Clamp01((elevation - min) / textureRanges[i].transitionWidth) *
                        Mathf.Clamp01((max - elevation) / textureRanges[i].transitionWidth);

                    splatmapData[x, z, i] = transitionFactor;
                }

                // Normalize the transition values to ensure they add up to 1.
                float totalTransition = 0f;
                for (int i = 0; i < textureRanges.Length; i++)
                {
                    totalTransition += splatmapData[x, z, i];
                }

                if (totalTransition > 0f)
                {
                    for (int i = 0; i < textureRanges.Length; i++)
                    {
                        splatmapData[x, z, i] /= totalTransition;
                    }
                }
            }
        }*/


        paintShader.SetTexture(0, "result", contourRTexture);
        paintShader.SetBuffer(0, "HeightMap", HeightsBuffer);
        paintShader.SetBuffer(0, "textureRanges", textureRangesBuffer);
        paintShader.SetBuffer(0, "splatmapData", splatmapDataBuffer);
        paintShader.SetFloat("HeightScale", terrain.terrainData.size.y);
        paintShader.SetFloat("resolution", terrain.terrainData.alphamapResolution);

        paintShader.Dispatch(0, terrain.terrainData.alphamapWidth / 8, terrain.terrainData.alphamapHeight / 8, 1);
        
        float[] _splatmapData = new float[terrain.terrainData.alphamapWidth * terrain.terrainData.alphamapHeight * _textureRanges.Length];

        // Apply a blur to the splatmap data.
        if(m_ApplyBlur)
        ApplyBlur(terrain.terrainData.alphamapWidth, terrain.terrainData.alphamapHeight);

        splatmapDataBuffer.GetData(_splatmapData);
        int index = 0;
        for (int x = 0; x < terrain.terrainData.alphamapWidth; x++)
        {
            for (int z = 0; z < terrain.terrainData.alphamapHeight; z++)
            {
                for (int i = 0; i < _textureRanges.Length; i++)
                {
                    splatmapData[x, z, i] = _splatmapData[index];
                    index++;
                }
            }
        }

        terrain.terrainData.SetAlphamaps(0, 0, splatmapData);
    }

    Texture2D CreateTextureFromColor(Color color, int width, int height)
    {
        Color[] colors = new Color[width * height];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = color;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, true);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.SetPixels(colors);
        texture.Apply();
        return texture;
    }
    // Apply a simple Gaussian blur to the splatmap data.
    void ApplyBlur(int width, int height)
    {
        float[] kernel = GaussianBlurKernel(blurRadius);

        if(kernelBuffer == null || kernelBuffer.count != kernel.Length)
            kernelBuffer = new ComputeBuffer(kernel.Length,sizeof(float));
        kernelBuffer.SetData(kernel);
        blurShader.SetBuffer(0, "kernel", kernelBuffer);
        blurShader.SetBuffer(0, "splatmapData", splatmapDataBuffer);
        blurShader.SetFloat("blurRadius", blurRadius);
        blurShader.SetFloat("textureRanges", textureRanges.Length);
        blurShader.SetFloat("resolution", terrain.terrainData.alphamapResolution);

        blurShader.Dispatch(0, terrain.terrainData.alphamapWidth / 8, terrain.terrainData.alphamapHeight / 8, 1);
    }

    // Generate a Gaussian blur kernel based on the radius.
    float[] GaussianBlurKernel(int radius)
    {

        int size = 2 * radius + 1;
        float[] kernel = new float[size * size];
        float sigma = radius / 3.0f;
        float twoSigmaSquared = 2 * sigma * sigma;
        float oneOver2PiSigmaSquared = 1.0f / (2 * Mathf.PI * sigma * sigma);
        float total = 0;

        for (int i = -radius; i <= radius; i++)
        {
            for (int j = -radius; j <= radius; j++)
            {
                float exponent = (i * i + j * j) / twoSigmaSquared;
                kernel[(size * (i + radius)) + (j + radius)] = oneOver2PiSigmaSquared * Mathf.Exp(-exponent);
                total += kernel[(size * (i + radius)) + (j + radius)];
            }
        }

        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                kernel[(size * i) + j] /= total;
            }
        }

        return kernel;
    }
}
