using UnityEngine;

public class Atlas : MonoBehaviour
{
    public struct AtlasData
    {
        public int AtlasWidth;
        public int AtlasHeight;
        public float CellWidthUV;
        public float CellHeightUV;
    }

    public static AtlasData GetAtlasData(Texture2D atlasTexture, int cellSizePixels = 32)
    {
        AtlasData data;

        int pixelWidth = atlasTexture.width;
        int pixelHeight = atlasTexture.height;

        data.AtlasWidth = pixelWidth / cellSizePixels;
        data.AtlasHeight = pixelHeight / cellSizePixels;

        data.CellWidthUV = 1f / data.AtlasWidth;
        data.CellHeightUV = 1f / data.AtlasHeight;

        return data;
    }

}
