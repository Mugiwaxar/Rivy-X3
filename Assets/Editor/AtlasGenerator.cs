using UnityEngine;
using System.IO;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class VoxelAtlasGenerator : EditorWindow
{
    private int textureSize = 32;
    private string atlasName = "Atlas";

    [MenuItem("Tools/Voxel Atlas Generator")]
    public static void ShowWindow()
    {
        GetWindow<VoxelAtlasGenerator>("Voxel Atlas Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("Atlas Settings", EditorStyles.boldLabel);

        textureSize = EditorGUILayout.IntField("Texture Size (px)", textureSize);
        atlasName = EditorGUILayout.TextField("Atlas Name", atlasName);

        if (GUILayout.Button("Generate Atlas"))
        {
            GenerateAtlas();
        }
    }

    private void GenerateAtlas()
    {
        string blocksPath = "Assets/Data/Blocks/";
        string outputPath = "Assets/Data/Atlas/";

        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        List<BlockTextures> allBlocks = new List<BlockTextures>();

        // Trouver tous les blocs
        string[] blockFolders = Directory.GetDirectories(blocksPath);
        foreach (string folder in blockFolders)
        {
            string texturesFolder = Path.Combine(folder, "Textures");
            if (Directory.Exists(texturesFolder))
            {
                BlockTextures block = new BlockTextures();
                block.blockName = Path.GetFileName(folder);
                block.textures = new List<Texture2D>();

                string[] textures = Directory.GetFiles(texturesFolder, "*.png");
                foreach (string texPath in textures)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath.Replace("\\", "/"));
                    if (tex != null)
                    {
                        block.textures.Add(tex);
                    }
                }

                allBlocks.Add(block);
            }
        }

        if (allBlocks.Count == 0)
        {
            Debug.LogError("No blocks found! Make sure Assets/Data/Blocks/*/Textures/*.png exists.");
            return;
        }

        // Calcul dimensions de l'atlas
        int maxFacesPerBlock = 0;
        foreach (var block in allBlocks)
            maxFacesPerBlock = Mathf.Max(maxFacesPerBlock, block.textures.Count);

        int atlasWidth = allBlocks.Count;
        int atlasHeight = maxFacesPerBlock;

        Texture2D atlas = new Texture2D(atlasWidth * textureSize, atlasHeight * textureSize, TextureFormat.RGBA32, false);

        Color32[] clearColors = new Color32[textureSize * textureSize];
        for (int i = 0; i < clearColors.Length; i++) clearColors[i] = new Color32(0, 0, 0, 0);

        // Remplir l'atlas : gauche à droite !
        for (int x = 0; x < allBlocks.Count; x++)
        {
            BlockTextures block = allBlocks[x];
            for (int y = 0; y < atlasHeight; y++)
            {
                if (y < block.textures.Count)
                    CopyTextureToAtlas(block.textures[y], atlas, x, y, textureSize);
                else
                    atlas.SetPixels32(x * textureSize, y * textureSize, textureSize, textureSize, clearColors);
            }
        }

        atlas.Apply();

        byte[] bytes = atlas.EncodeToPNG();
        File.WriteAllBytes(Path.Combine(outputPath, atlasName + ".png"), bytes);
        AssetDatabase.Refresh();

        Debug.Log("Voxel Atlas generated successfully at " + outputPath + atlasName + ".png");
    }

    private static void CopyTextureToAtlas(Texture2D source, Texture2D atlas, int blockX, int faceY, int textureSize)
    {
        Texture2D readableTex = GetReadableTexture(source);

        if (readableTex.width != textureSize || readableTex.height != textureSize)
        {
            readableTex = ResizeTexture(readableTex, textureSize, textureSize);
        }

        Color[] colors = readableTex.GetPixels();
        atlas.SetPixels(blockX * textureSize, faceY * textureSize, textureSize, textureSize, colors);
    }

    private static Texture2D GetReadableTexture(Texture2D tex)
    {
        RenderTexture tmp = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        Graphics.Blit(tex, tmp);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = tmp;
        Texture2D readableTex = new Texture2D(tex.width, tex.height);
        readableTex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        readableTex.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(tmp);
        return readableTex;
    }

    private static Texture2D ResizeTexture(Texture2D source, int newWidth, int newHeight)
    {
        RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
        rt.filterMode = FilterMode.Point;

        RenderTexture.active = rt;
        Graphics.Blit(source, rt);
        Texture2D result = new Texture2D(newWidth, newHeight);
        result.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        result.Apply();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    private struct BlockTextures
    {
        public string blockName;
        public List<Texture2D> textures;
    }
}
