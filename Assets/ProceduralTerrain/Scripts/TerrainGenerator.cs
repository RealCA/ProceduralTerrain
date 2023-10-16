using System;
using System.Linq;
using Unity.Mathematics;
using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEditor.ShaderGraph;
using UnityEngine;
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
public class TerrainGenerator : MonoBehaviour
{
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

    public Material terrainMaterial;

    private Terrain terrain;
    public float[,,] splats;

    public Elevation[] Elevations = new Elevation[]
    {
        new Elevation()
        {
            name = "Snow",
            color=new Color(216 / 255f, 222 / 255f, 233 / 255f),
            height= 0.85f
        },
        new Elevation()
        {
            name = "Snow0",
            color =new Color(121 / 255f, 133 / 255f, 159 / 255f),
            height= 0.45f
        },

        new Elevation()
        {
            name = "Snow1",
            color = new Color(76 / 255f, 86 / 255f, 106 / 255f),
            height= 0.38f
        },
        new Elevation()
        {
            name = "Grass0",
            color = new Color(163 / 255f, 190 / 255f, 140 / 255f),
            height= 0.20f
        },
        new Elevation()
        {
            name = "Grass",
            color=new Color(143 / 255f, 176 / 255f, 115 / 255f),
            height= 0.15f
        },
        new Elevation()
        {
            name = "Sand",
            color=new Color(235 / 255f, 203 / 255f, 139 / 255f),
            height= 0.115f
        },
        new Elevation()
        {
            name = "Water0",
            color=new Color(115 / 255f, 146 / 255f, 183 / 255f),
            height= 0.1f
        },
        new Elevation()
        {
            name = "Water",
            color=new Color(94 / 255f, 129 / 255f, 172 / 255f),
            height = 0
        },
    };

    void Start()
    {
        terrain = GetComponent<Terrain>();
        splats = new float[terrain.terrainData.alphamapWidth, terrain.terrainData.alphamapHeight, Elevations.Length];
        GenerateTerrain();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!terrain)
            terrain = GetComponent<Terrain>();
        if (splats == null)
            splats = new float[terrain.terrainData.alphamapWidth, terrain.terrainData.alphamapHeight, Elevations.Length];
        UnityEditor.EditorApplication.delayCall = GenerateTerrain;
    }
#endif

    void GenerateTerrain()
    {
        repeated = new int[Elevations.Length];
        terrain.terrainData.terrainLayers = Elevations.Select(x =>
        new TerrainLayer()
        {
            name = x.name,
            diffuseTexture = (x.useTexture)? x.texture : CreateTextureFromColor(x.color, width, height),
            smoothness = 0,
            metallic = 0,
        }
        ).ToArray();
        terrain.terrainData = GenerateTerrain(terrain.terrainData);
        //ApplyElevationColor();
    }

    TerrainData GenerateTerrain(TerrainData terrainData)
    {
        terrainData.heightmapResolution = width + 1;
        terrainData.size = new Vector3(width, scale, height);
        terrainData.SetHeights(0, 0, GenerateHeights());
        print(terrain.terrainData.alphamapResolution);
        terrain.terrainData.SetAlphamaps(0, 0, splats);
        return terrainData;
    }

    float[,] GenerateHeights()
    {
        float[,] heights = new float[width, height];
        float centerX = width / 2f; // Calculate the center of the terrain on the X-axis
        float centerY = height / 2f; // Calculate the center of the terrain on the Y-axis

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float amplitude = 1;
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
                heights[x, y] = noiseHeight * gradientX * gradientY;
                ApplyElevationColor(x, y, heights[x, y]);
            }
        }

        return heights;
    }
    int lastpixely;
    int[] repeated;
    int totalpaints;
    void ApplyElevationColor(int x, int y, float elevation)
    {
        if (Elevations.Length < 1)
            return;
        TerrainData terrainData = terrain.terrainData;
        var _height = 1f;
        var layer = 0;
        //if (terrainMaterial != null)
        for (int i = 0; i < Elevations.Length; i++)
        {
            if (elevation >= Elevations[i].height && Elevations[i].height < _height)
            {
                _height = Elevations[i].height;
                layer = i;
            }
        }
        float tx = (float)x / width * terrainData.alphamapWidth;
        float ty = (float)y / height * terrainData.alphamapHeight;
        if (((int)ty) != lastpixely)
        {
            for (int i = 0; i < repeated.Length; i++)
                splats[(int)tx, (int)ty, i] = repeated[i] / totalpaints;
            lastpixely = (int)ty;
            repeated = new int[Elevations.Length];
            totalpaints = 0;
        }
        repeated[layer]++;
        totalpaints++;
        //terrainMaterial.SetTexture("_MainTex", CreateTextureFromColors(colors, width, height));
    }

    Color CalculateColor(float elevation)
    {
        if (elevation >= 0.85)
        {
            return new Color(216 / 255f, 222 / 255f, 233 / 255f);
        }
        else if (elevation >= 0.45)
        {
            return new Color(121 / 255f, 133 / 255f, 159 / 255f);
        }
        else if (elevation >= 0.38)
        {
            return new Color(76 / 255f, 86 / 255f, 106 / 255f);
        }
        else if (elevation >= 0.20)
        {
            return new Color(163 / 255f, 190 / 255f, 140 / 255f);
        }
        else if (elevation >= 0.15)
        {
            return new Color(143 / 255f, 176 / 255f, 115 / 255f);
        }
        else if (elevation >= 0.115)
        {
            return new Color(235 / 255f, 203 / 255f, 139 / 255f);
        }
        else if (elevation >= 0.1)
        {
            return new Color(115 / 255f, 146 / 255f, 183 / 255f);
        }
        else
        {
            return new Color(94 / 255f, 129 / 255f, 172 / 255f);
        }
    }

    Texture2D CreateTextureFromColors(Color[] colors, int width, int height)
    {
        Texture2D texture = new Texture2D(width, height,TextureFormat.RGB24,true);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.SetPixels(colors);
        texture.Apply();
        return texture;
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
}
