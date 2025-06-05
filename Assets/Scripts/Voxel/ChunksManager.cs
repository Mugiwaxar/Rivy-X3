using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static EnumData;
using static UnityEditor.PlayerSettings;

public struct VoxelSettings : IComponentData
{
    public byte chunkSize;
    public byte worldSizeInChunks;
    public byte worldHeightInChunks;
    public bool doFloodFill;
    public bool doLinearFloodFill;
    public bool doFacesOcclusion;
    public bool doGreedyMeshing;
    public bool doFaceNormalCheck;
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct InitChunks : ISystem
{

    public void OnCreate(ref SystemState state)
    {

        // Get the world settings //
        VoxelSettings settings = SystemAPI.GetSingleton<VoxelSettings>();
        byte worldSizeInChunks = settings.worldSizeInChunks;
        byte worldHeightInChunks = settings.worldHeightInChunks;
        int chunkSize = settings.chunkSize;

        // Get the chunks map //
        NativeParallelHashMap<int3, Entity> chunksMap = VoxelWorld._ChunkManager.chunksMap;

        // Create all chunks //
        for (int x = 0; x < worldSizeInChunks; x++)
        {
            for (int y = 0; y < worldHeightInChunks; y++)
            {
                for (int z = 0; z < worldSizeInChunks; z++)
                {
                    // Get the position //
                    int3 position = new int3(x, y, z);
                    chunksMap.TryAdd(position, ChunksGenerator.CreateChunk(ref state, position, chunkSize));
                }
            }
        }

    }

    public void OnUpdate(ref SystemState state) { }

}

public class ChunkSManager : MonoBehaviour
{

    public NativeParallelHashMap<int3, Entity> chunksMap;

    void Awake()
    {
        chunksMap = new NativeParallelHashMap<int3, Entity>(VoxelWorld._Instance.worldTotalSizeInChunks, Allocator.Persistent);
    }

    void OnDestroy()
    {
        if (chunksMap.IsCreated)
            chunksMap.Dispose();
    }

    public Entity GetChunk(Vector3Int pos, Direction direction = Direction.None)
    {
        return GetChunk(this.chunksMap, pos.x, pos.y, pos.z, direction);
    }

    public static Entity GetChunk(NativeParallelHashMap<int3, Entity> chunksMap, int x, int y, int z, Direction direction = Direction.None)
    {
        int3 newPos;
        switch (direction)
        {
            case Direction.None:
                newPos = new int3(x, y, z);
                break;
            case Direction.Left:
                newPos = new int3(x - 1, y, z);
                break;
            case Direction.Right:
                newPos = new int3(x + 1, y, z);
                break;
            case Direction.Bottom:
                newPos = new int3(x, y - 1, z);
                break;
            case Direction.Top:
                newPos = new int3(x, y + 1, z);
                break;
            case Direction.Back:
                newPos = new int3(x, y, z - 1);
                break;
            case Direction.Front:
                newPos = new int3(x, y, z + 1);
                break;
            default:
                return Entity.Null;
        }

        if (chunksMap.TryGetValue(newPos, out var chunk))
            return chunk;

        return Entity.Null;
    }

}

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct LoadChunksAroundCamera : ISystem
{
    public int loadRadius;

    public void OnCreate(ref SystemState state)
    {
        loadRadius = 1;
    }

    public void OnUpdate(ref SystemState state)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        VoxelSettings settings = SystemAPI.GetSingleton<VoxelSettings>();
        int chunkSize = settings.chunkSize;

        float3 camPos = cam.transform.position;
        int3 camChunk = new int3(
            (int)math.floor(camPos.x / chunkSize),
            (int)math.floor(camPos.y / chunkSize),
            (int)math.floor(camPos.z / chunkSize));

        var chunksMap = VoxelWorld._ChunkManager.chunksMap;

        for (int dx = -loadRadius; dx <= loadRadius; dx++)
        {
            for (int dy = -loadRadius; dy <= loadRadius; dy++)
            {
                for (int dz = -loadRadius; dz <= loadRadius; dz++)
                {
                    int3 pos = camChunk + new int3(dx, dy, dz);

                    if (pos.x < 0 || pos.y < 0 || pos.z < 0 ||
                        pos.x >= settings.worldSizeInChunks ||
                        pos.y >= settings.worldHeightInChunks ||
                        pos.z >= settings.worldSizeInChunks)
                        continue;

                    if (!chunksMap.ContainsKey(pos))
                    {
                        chunksMap.TryAdd(pos, ChunksGenerator.CreateChunk(ref state, pos, chunkSize));
                    }
                }
            }
        }
    }
}
