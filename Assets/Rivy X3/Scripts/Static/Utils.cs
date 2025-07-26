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
            (int)math.floor((float)chunkPos.y / regionSize),
            (int)math.floor((float)chunkPos.z / regionSize)
        );
    }

    public static int3 WorldPosToRegionCoord(float3 worldPos, int regionSizeInBlocks)
    {
        return new int3(
            (int)math.floor(worldPos.x / regionSizeInBlocks),
            (int)math.floor(worldPos.y / regionSizeInBlocks),
            (int)math.floor(worldPos.z / regionSizeInBlocks)
        );
    }

    public static float3 RegionCoordToWorldPos(float3 regionCoord, int regionSizeInBlocks)
    {
        return new float3(
            math.floor(regionCoord.x * regionSizeInBlocks),
            math.floor(regionCoord.y * regionSizeInBlocks),
            math.floor(regionCoord.z * regionSizeInBlocks)
        );
    }

    static public void DebugDrawChunkBounds(int3 chunkPos, int chunkSize, Color color)
    {
        Vector3 min = new Vector3(chunkPos.x, chunkPos.y, chunkPos.z) * chunkSize;
        Vector3 max = min + Vector3.one * chunkSize;

        Vector3[] corners = new Vector3[8];

        // Base
        corners[0] = new Vector3(min.x, min.y, min.z);
        corners[1] = new Vector3(max.x, min.y, min.z);
        corners[2] = new Vector3(max.x, min.y, max.z);
        corners[3] = new Vector3(min.x, min.y, max.z);

        // Top
        corners[4] = new Vector3(min.x, max.y, min.z);
        corners[5] = new Vector3(max.x, max.y, min.z);
        corners[6] = new Vector3(max.x, max.y, max.z);
        corners[7] = new Vector3(min.x, max.y, max.z);

        // Draw base square
        Debug.DrawLine(corners[0], corners[1], color);
        Debug.DrawLine(corners[1], corners[2], color);
        Debug.DrawLine(corners[2], corners[3], color);
        Debug.DrawLine(corners[3], corners[0], color);

        // Draw top square
        Debug.DrawLine(corners[4], corners[5], color);
        Debug.DrawLine(corners[5], corners[6], color);
        Debug.DrawLine(corners[6], corners[7], color);
        Debug.DrawLine(corners[7], corners[4], color);

        // Connect verticals
        Debug.DrawLine(corners[0], corners[4], color);
        Debug.DrawLine(corners[1], corners[5], color);
        Debug.DrawLine(corners[2], corners[6], color);
        Debug.DrawLine(corners[3], corners[7], color);
    }

    static public void DebugDrawRegionBounds(int3 regionCoord, int regionSizeInChunks, int chunkSize, Color color)
    {
        int regionSize = regionSizeInChunks * chunkSize;

        Vector3 min = new Vector3(regionCoord.x, regionCoord.y, regionCoord.z) * regionSize;
        Vector3 max = min + Vector3.one * regionSize;

        Vector3[] corners = new Vector3[8];

        // Base
        corners[0] = new Vector3(min.x, min.y, min.z);
        corners[1] = new Vector3(max.x, min.y, min.z);
        corners[2] = new Vector3(max.x, min.y, max.z);
        corners[3] = new Vector3(min.x, min.y, max.z);

        // Top
        corners[4] = new Vector3(min.x, max.y, min.z);
        corners[5] = new Vector3(max.x, max.y, min.z);
        corners[6] = new Vector3(max.x, max.y, max.z);
        corners[7] = new Vector3(min.x, max.y, max.z);

        // Draw base square
        Debug.DrawLine(corners[0], corners[1], color);
        Debug.DrawLine(corners[1], corners[2], color);
        Debug.DrawLine(corners[2], corners[3], color);
        Debug.DrawLine(corners[3], corners[0], color);

        // Draw top square
        Debug.DrawLine(corners[4], corners[5], color);
        Debug.DrawLine(corners[5], corners[6], color);
        Debug.DrawLine(corners[6], corners[7], color);
        Debug.DrawLine(corners[7], corners[4], color);

        // Connect verticals
        Debug.DrawLine(corners[0], corners[4], color);
        Debug.DrawLine(corners[1], corners[5], color);
        Debug.DrawLine(corners[2], corners[6], color);
        Debug.DrawLine(corners[3], corners[7], color);
    }


}
