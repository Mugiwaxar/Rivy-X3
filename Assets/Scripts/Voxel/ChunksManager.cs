using Assets.Scripts.Block;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using static EnumData;


[WorldSystemFilter(WorldSystemFilterFlags.Default)]
public partial class ChunkPipelineGroup : ComponentSystemGroup { }

[UpdateInGroup(typeof(ChunkPipelineGroup))]
[UpdateAfter(typeof(SingletonManager))]
public partial struct InitChunks : ISystem
{

    public void OnUpdate(ref SystemState state)
    {

        // Check if the world must init //
        if (VoxelWorld._Instance.requestWorldInit == false)
            return;

        // Get the world settings //
        VoxelWorld world = VoxelWorld._Instance;

        // Get the chunks map //
        if (SystemAPI.HasSingleton<DataSingleton>() == false) return;
        NativeParallelHashMap<int3,Entity> chunksMap = SystemAPI.GetSingleton<DataSingleton>().chunksMap;
        NativeParallelHashMap<int3,Entity> regionsMap = SystemAPI.GetSingleton<DataSingleton>().regionsMap;

        // Create all chunks //
        //for (int x = 0; x < world.worldSizeInChunks; x++)
        //{
        //    for (int y = 0; y < world.worldHeightInChunks; y++)
        //    {
        //        for (int z = 0; z < world.worldSizeInChunks; z++)
        //        {
        //            // Get the position //
        //            int3 position = new int3(x, y, z);
        //            // Create the chunk entity //
        //            Entity chunkEntity = ChunksGenerator.CreateChunk(ref state, position, world.chunkSize, world.removeFullAirChunk);
        //            // Continue if the entity is null (full air chunk) //
        //            if (chunkEntity == Entity.Null)
        //                continue;
        //            // Add it to the chunks map //
        //            chunksMap.TryAdd(position, chunkEntity);
        //            // Add it to the region //
        //            VoxelRegion.AddChunkToRegion(ref state, position, chunkEntity, world.regionSize, ref regionsMap);
        //        }
        //    }
        //}

        // Set the initialization as done //
        VoxelWorld._Instance.requestWorldInit = false;

    }

    public void OnDestroy(ref SystemState state)
    {

        // Destroy all previous chunks //
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach ((RefRO<ChunkPosition> pos, Entity entity) in SystemAPI.Query<RefRO<ChunkPosition>>().WithEntityAccess())
        {
            ecb.DestroyEntity(entity);
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();

    }

}

//public partial struct UpdateChunks : ISystem
//{

//    public void OnUpdate(ref SystemState state)
//    {

//    }

//}

public class ChunksManager : MonoBehaviour
{



    void Awake()
    {



    }

    void OnDestroy()
    {

    }

    void Update()
    {



    }

    public static void GenerateAllChunksInRegion(ref SystemState state, int3 regionCoord, WorldSettings WS, DataSingleton DS, DynamicBuffer<RegionChunks> buffer, int chunksCount)
    {

        // Create the chunk archetype //
        EntityArchetype archetype = state.EntityManager.CreateArchetype(typeof(ChunkPosition), typeof(LocalTransform), typeof(ChunkNeedBlocks), typeof(ChunkNeedRender), typeof(BlockData), typeof(ChunkSquareFaces));

        // Create all chunks //
        NativeArray<Entity> chunkArray = NativesPoolManager<Entity>.GetArray(chunksCount);
        state.EntityManager.CreateEntity(archetype, chunkArray);

        // Set all entities //
        int i = 0;
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        for (int cx = 0; cx < WS.regionSize; cx++)
            for (int cy = 0; cy < WS.regionSize; cy++)
                for (int cz = 0; cz < WS.regionSize; cz++)
                {
                    Entity chunkEntity = chunkArray[i];
                    int3 chunkCoord = regionCoord * WS.regionSize + new int3(cx, cy, cz);
                    if (ChunksGenerator.CreateChunk(ref state, chunkCoord, chunkEntity, WS.chunkSize, WS.removeFullAirChunk) == false)
                    {
                        ecb.DestroyEntity(chunkEntity);
                        continue;
                    }

                    if (DS.chunksMap.ContainsKey(chunkCoord) == true)
                    {
                        Entity entityToDestroy = DS.chunksMap[chunkCoord];
                        ecb.DestroyEntity(entityToDestroy);
                        DS.chunksMap.Remove(chunkCoord);
                    }

                    DS.chunksMap.Add(chunkCoord, chunkEntity);
                    buffer.Add(new RegionChunks { ChunkEntity = chunkEntity });

                    i++;
                }

        // Playback the entity command buffer //
        ecb.Playback(state.EntityManager);

        // Dispose the tables //
        ecb.Dispose();
        NativesPoolManager<Entity>.ReleaseArray(chunkArray);

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