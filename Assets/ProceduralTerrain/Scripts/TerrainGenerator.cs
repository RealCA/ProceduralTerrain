using System;
using System.Linq;
using Unity.Mathematics;
using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEditor.ShaderGraph;
using UnityEngine;
using Random = UnityEngine.Random;
[Serializable]
public class Elevation 
{
    public string name;
    public bool useTexture;
    public Color color;
    public Texture2D texture;
    [Range(0,1)]
    public float height;
    public float minElevation = 0.0f;
    public float maxElevation = 1.0f;
    public float transitionWidth = 0.1f;
}

[Serializable]
public struct RiverRules
{
    public Vector2 start;
    public int riverDirection;
    public int riverLength;
}
public class TerrainGenerator : MonoBehaviour
{
    public ComputeShader PerlinNoise;
    //public ComputeShader RiverPass;
    public int width = 256;
    public int height = 256;
    public float scale = 20; 
    public float squareGradientSize = 0.2f; // Adjust the size of the square gradient
    public float frequancy = 20;
    public int octaves = 5;
    [Range(0, 1)]
    public float persistence = 0.5f;
    public float lacunarity = 2.0f;
    public float redistribution = 1.1f;

    //public int numRivers = 5;  // Number of rivers
    public float riverDepth = 0.2f;  // Maximum river depth
    public float riverWidth = 5.0f; // Adjust this to control river width
    public int seed = 42; // Seed for consistent results
    public bool RandmizedRivers;

    public RiverRules[] riverRules;

    public RenderTexture texture;

    public Material terrainMaterial;

    private Terrain terrain;
    private ComputeBuffer heightBuffer;
    private float[] _heights;
    private ComputeBuffer riverRulesBuffer;
    private System.Random random;

    void Start()
    {
        terrain = GetComponent<Terrain>();
        GenerateTerrain();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!terrain)
            terrain = GetComponent<Terrain>();
        UnityEditor.EditorApplication.delayCall = GenerateTerrain;
    }
#endif

    private void OnDisable()
    {
        heightBuffer.Release();
        heightBuffer = null;
        riverRulesBuffer.Release();
        riverRulesBuffer = null;
    }

    void GenerateTerrain()
    {
        random = new System.Random(seed);
        terrain.terrainData = GenerateTerrain(terrain.terrainData);
        //ApplyElevationColor();
    }

    TerrainData GenerateTerrain(TerrainData terrainData)
    {
        if(heightBuffer == null || heightBuffer.count != height * width)
            heightBuffer = new ComputeBuffer(height * width,sizeof(float));
        if(!texture)
        {
            texture = new RenderTexture(width, height, 24);
            texture.enableRandomWrite = true;
            texture.Create();
        }
        terrainData.size = new Vector3(width, scale, height);
        UpdateHeightBuffer();
        terrainData.SetHeights(0, 0, GenerateHeights());
        print(terrain.terrainData.alphamapResolution);
        return terrainData;
    }

    void CreateRivers()
    {
        if (RandmizedRivers)
        {
            for (int i = 0; i < riverRules.Length; i++)
            {
                int startX = random.Next(width);
                int startY = random.Next(height);
                riverRules[i].start = new Vector2(startX, startY);
                riverRules[i].riverDirection = random.Next(360);
                riverRules[i].riverLength = random.Next(50, 100);
            }

        }
        if (riverRulesBuffer == null || riverRulesBuffer.count != riverRules.Length)
                riverRulesBuffer = new ComputeBuffer(riverRules.Length, sizeof(float) * 4);
            riverRulesBuffer.SetData(riverRules);
            PerlinNoise.SetBuffer(PerlinNoise.FindKernel("CSRiver"), "riverRules", riverRulesBuffer);
            PerlinNoise.SetBuffer(PerlinNoise.FindKernel("CSRiver"), "heights", heightBuffer);
            PerlinNoise.SetFloat("numRivers", riverRules.Length);
            PerlinNoise.SetFloat("riverDepth", riverDepth);
            PerlinNoise.SetFloat("riverWidth", riverWidth);

            for (int i = 0; i < riverRules.Length; i++)
            {
                PerlinNoise.SetFloat("riverID", i);
                PerlinNoise.Dispatch(PerlinNoise.FindKernel("CSRiver"), riverRules[i].riverLength / 8, 1, 1);
            }
    }

    void UpdateHeightBuffer()
    {
        float centerX = width / 2f; // Calculate the center of the terrain on the X-axis
        float centerY = height / 2f; // Calculate the center of the terrain on the Y-axis
        PerlinNoise.SetBuffer(PerlinNoise.FindKernel("CSMain"), "heights", heightBuffer);
        PerlinNoise.SetTexture(PerlinNoise.FindKernel("CSMain"), "Result", texture);
        PerlinNoise.SetFloat("squareGradientSize", squareGradientSize);
        PerlinNoise.SetFloat("frequancy", frequancy);
        PerlinNoise.SetFloat("octaves", octaves);
        PerlinNoise.SetFloat("persistence", persistence);
        PerlinNoise.SetFloat("lacunarity", lacunarity);
        PerlinNoise.SetFloat("redistribution", redistribution);
        PerlinNoise.SetFloat("centerX", centerX);
        PerlinNoise.SetFloat("centerY", centerY);
        PerlinNoise.SetFloat("width", width);
        PerlinNoise.SetFloat("height", height);
        PerlinNoise.Dispatch(PerlinNoise.FindKernel("CSMain"), width / 8, height / 8, 1);
        _heights = new float[width * height];
        CreateRivers();
        heightBuffer.GetData(_heights);
    }

    float[,] GenerateHeights()
    {
        float[,] heights = new float[width, height];
        int index = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                /*float amplitude = 1;
                float frequency = 1;
                float noiseHeight = 0; 
                // Calculate the distance from the center independently for both X and Y axes
                float distanceFromCenterX = Mathf.Abs(x - centerX) / centerX;
                float distanceFromCenterY = Mathf.Abs(y - centerY) / centerY;
                for (int i = 0; i < octaves; i++)
                {
                    float xCoord = (float)x / width * frequancy * frequency;
                    float yCoord = (float)y / height * frequancy * frequency;
                    float perlinValue = Mathf.PerlinNoise(xCoord, yCoord) * 2 - 1;
                    noiseHeight += perlinValue * amplitude;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                noiseHeight = Mathf.Pow(Mathf.Abs(noiseHeight), redistribution);

                // Apply the square gradient effect based on distance from the center for both axes
                float gradientX = 1f - Mathf.Clamp01(distanceFromCenterX / squareGradientSize);
                float gradientY = 1f - Mathf.Clamp01(distanceFromCenterY / squareGradientSize);
                heights[x, y] = noiseHeight * gradientX * gradientY;*/
                heights[x, y] = _heights[index];
                index++;
            }
        }

        return heights;
    }
}
