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

    private int chunkPoolDesiredCount;
    EntityArchetype chunkArchetype;

    public void OnCreate(ref SystemState state)
    {

        // Get the world //
        VoxelWorld world = VoxelWorld._Instance;

        // Get the Entity Manager //
        EntityManager em = state.EntityManager;

        // Set the max chunks //
        this.chunkPoolDesiredCount = world.regionSizeInChunks * (world.maxRegionGenerationPerFrame * 2);

        // Create the chunk archetype //
        this.chunkArchetype = em.CreateArchetype(typeof(ChunkPosition), typeof(LocalTransform), typeof(ChunkNeedBlocks), typeof(ChunkNeedRender), typeof(BlockData), typeof(ChunkSquareFaces));

        //// Create all chunks //
        //int regions = ((world.maxRegionDistance * 2) + 1) * ((world.maxRegionDistance * 2) + 1);
        //int chunksToCreate = regions * world.regionSizeInChunks;
        //chunksToCreate = (int)(chunksToCreate * 1.2f);
        //NativeArray<Entity> chunkArray = new NativeArray<Entity>(chunksToCreate, Allocator.Temp);
        //em.CreateEntity(chunkArchetype, chunkArray);

        //// Add all chunks to the Stack //
        //ChunksPoolManager.AddChunks(ref em, chunkArray);

        //// Dispose the Array //
        //chunkArray.Dispose();

    }

    public void OnUpdate(ref SystemState state)
    {

        // Chunks pool regulation //
        EntityManager em = state.EntityManager;
        ChunksPoolManager.PoolRegulation(ref em, this.chunkPoolDesiredCount, this.chunkArchetype);

    }


}

public class ChunksPoolManager
{

    private static Stack<Entity> Pool = new Stack<Entity>();
    private static readonly object Locker = new();

    public static void AddChunks(ref EntityManager em, NativeArray<Entity> chunkArray)
    {
        foreach (Entity entity in chunkArray)
        {
            Pool.Push(entity);
            em.SetComponentEnabled<ChunkNeedBlocks>(entity, false);
            em.SetComponentEnabled<ChunkNeedRender>(entity, false);
        }
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

    public static void PoolRegulation(ref EntityManager em, int count, EntityArchetype chunkArchetype)
    {
        if (Pool.Count > count)
        {
            for (int i = 0; i < Mathf.Min(30, Pool.Count - count); i++)
            {
                Entity chunk = Pool.Pop();
                em.DestroyEntity(chunk);
            }
        }
        else if (Pool.Count < count)
        {
            NativeArray<Entity> chunkArray = new NativeArray<Entity>(count - Pool.Count, Allocator.Temp);
            em.CreateEntity(chunkArchetype, chunkArray);
            ChunksPoolManager.AddChunks(ref em, chunkArray);
            chunkArray.Dispose();
        }
    }

    public static void DisposeAll()
    {
        if (World.DefaultGameObjectInjectionWorld != null && World.DefaultGameObjectInjectionWorld.EntityManager != null)
        {
            EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
            foreach (Entity entity in Pool)
                em.DestroyEntity(entity);
        }
    }

    public static string GetStats()
    {
        return $"Chunks released: {Pool.Count}";
    }

}
