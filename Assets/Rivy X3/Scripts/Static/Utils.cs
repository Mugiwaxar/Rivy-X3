using Assets.Scripts.Block;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using static BuildMesh;
using static EnumData;
using static UnityEngine.Rendering.DebugUI;

public static class Utils
{
    static public int PosToIndex(int chunkSize, int yChunkSize, int x, int y, int z)
    {
        return x + (chunkSize * (y + (yChunkSize * z)));
    }

    static public int3 IndexToPos(int chunkSize, int yChunkSize, int index)
    {
        int layerSize = chunkSize * yChunkSize;

        int z = index / layerSize;
        int rem = index - (z * layerSize);

        int y = rem / chunkSize;
        int x = rem - (y * chunkSize);

        return new int3(x, y, z);
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

    public static bool TryGetSingletonECS<T>(out T value) where T : unmanaged, IComponentData
    {

        EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;

        var query = em.CreateEntityQuery(ComponentType.ReadOnly<T>());
        if (query.CalculateEntityCount() == 0)
        {
            value = default;
            return false;
        }
        var entity = query.GetSingletonEntity();
        value = em.GetComponentData<T>(entity);
        return true;

    }

    public static int3 ChunkPosToRegionCoord(int3 chunkPos, int regionSize)
    {
        return new int3(
            (int)math.floor((float)chunkPos.x / regionSize),
            chunkPos.y,
            (int)math.floor((float)chunkPos.z / regionSize)
        );
    }

    public static int3 WorldPosToRegionCoord(float3 worldPos, int regionSizeInBlocks, int yChunkSize)
    {
        return new int3(
            (int)math.floor(worldPos.x / regionSizeInBlocks),
            (int)math.floor(worldPos.y / yChunkSize),
            (int)math.floor(worldPos.z / regionSizeInBlocks)
        );
    }

    public static float3 RegionCoordToWorldPos(float3 regionCoord, int regionSizeInBlocks, int yChunkSize)
    {
        return new float3(
            regionCoord.x * regionSizeInBlocks,
            regionCoord.y * yChunkSize,
            regionCoord.z * regionSizeInBlocks
        );
    }


}
