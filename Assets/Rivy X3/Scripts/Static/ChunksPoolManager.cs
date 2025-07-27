using Assets.Scripts.Block;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;


[UpdateInGroup(typeof(ChunkPipelineGroup))]
[UpdateAfter(typeof(SingletonManager))]
public partial struct ChunksPoolManagerSystem : ISystem
{

    public void OnCreate(ref SystemState state)
    {

        // Get the world //
        VoxelWorld world = VoxelWorld._Instance;

        // Create the chunk archetype //
        EntityArchetype archetype = state.EntityManager.CreateArchetype(typeof(ChunkPosition), typeof(LocalTransform), typeof(ChunkNeedBlocks), typeof(ChunkNeedRender), typeof(BlockData), typeof(ChunkSquareFaces));

        // Create all chunks //
        int regions = ((world.maxRegionDistance*2)+1) * ((world.maxRegionDistance * 2) + 1) * (world.yViewDistance + 2);
        int chunksToCreate = regions * world.regionSizeInChunks;
        chunksToCreate = (int)(chunksToCreate * 1.2f);
        NativeArray<Entity> chunkArray = new NativeArray<Entity>(chunksToCreate, Allocator.Temp);
        state.EntityManager.CreateEntity(archetype, chunkArray);

        // Add all chunks to the Stack //
        ChunksPoolManager.AddChunks(chunkArray);

        // Dispose the Array //
        chunkArray.Dispose();

    }


}

public class ChunksPoolManager
{

    private static Stack<Entity> Pool = new Stack<Entity>();
    private static readonly object Locker = new();

    public static void AddChunks(NativeArray<Entity> chunkArray)
    {
        foreach (Entity entity in chunkArray)
            Pool.Push(entity);
    }

    public static Entity GetChunk()
    {
        lock (Locker)
        {
            if (Pool.Count > 0)
            {
                Entity entity= Pool.Pop();
                return entity;
            }
        }
        Debug.LogWarning("ChunkPool overhead ...");
        return Entity.Null;
    }

    public static void ReleaseChunk(Entity chunk)
    {
        if (chunk == Entity.Null) return;
        lock (Locker)
        {
            Pool.Push(chunk);
        }
    }

    public static void DisposeAll()
    {
        EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
        foreach (Entity entity in Pool)
            em.DestroyEntity(entity);
    }

    public static string GetStats()
    {
        return $"Chunks released: {Pool.Count}";
    }

}
