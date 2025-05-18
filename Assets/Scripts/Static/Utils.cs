using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static EnumData;

public static class Utils
{
    static public int PosToIndex(int chunkSize, int x, int y, int z)
    {
        return x + (chunkSize * (y + (chunkSize * z)));
    }

    static public uint2 IDToAtlasIndex(BlocksID id)
    {
        switch (id)
        {
            case BlocksID.Grass: return new uint2(1, 0);
            case BlocksID.Dirt: return new uint2(2, 0);
            case BlocksID.Stone: return new uint2(3, 0);
            default: return new uint2(0, 0);
        }
    }

}
