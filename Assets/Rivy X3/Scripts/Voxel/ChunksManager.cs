using Assets.Scripts.Block;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using static ChunksGenerator;
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

    public static void GenerateAllChunksInRegion(ref SystemState state, int3 regionCoord, WorldSettings WS, DataSingleton DS, DynamicBuffer<RegionChunks> buffer)
    {

        // Create the blocks table //
        NativeArray<BlockData> regionBlocks = NativesPoolManager<BlockData>.GetArray(WS.regionBlocksCount);

        // Get the region position in blocks //
        int3 regionBasePos = new int3(
                regionCoord.x * WS.regionSize * WS.chunkSize,
                regionCoord.y * WS.yRegionSize * WS.chunkSize,
                regionCoord.z * WS.regionSize * WS.chunkSize);

        // Create the job //
        new FillChunkJob()
        {
            blocks = regionBlocks,
            chunkSize = WS.chunkSize,
            regionRealPosition = regionBasePos
        }.Schedule(WS.regionBlocksCount, 64).Complete();

        // Set all Chunks //
        int i = 0;
        for (int cx = 0; cx < WS.regionSize; cx++)
            for (int cy = 0; cy < WS.yRegionSize; cy++)
                for (int cz = 0; cz < WS.regionSize; cz++)
                {

                    // Get the chunk coord //
                    int3 chunkCoord = new int3(
                        regionCoord.x * WS.regionSize + cx,
                        regionCoord.y * WS.yRegionSize + cy,
                        regionCoord.z * WS.regionSize + cz
                    );

                    // Check if a old chunk exist and destroy it //
                    if (DS.chunksMap.ContainsKey(chunkCoord) == true)
                    {
                        Entity entityToDestroy = DS.chunksMap[chunkCoord];
                        DS.chunksMap.Remove(chunkCoord);
                        ChunksPoolManager.ReleaseChunk(entityToDestroy);
                    }

                    // Generate the chunk //
                    Entity chunkEntity = ChunksGenerator.CreateChunk(ref state, ref regionBlocks, i * WS.chunkBlocksCount, chunkCoord, WS);

                    // Increase i //
                    i++;

                    // Check if the chunk is full air //
                    if (chunkEntity == Entity.Null)
                        continue;

                    // Add the chunk to the map and the buffer //
                    DS.chunksMap.Add(chunkCoord, chunkEntity);
                    buffer.Add(new RegionChunks { ChunkEntity = chunkEntity, ChunkCoord = chunkCoord });

                }

        // Release the blocks table //
        NativesPoolManager<BlockData>.ReleaseArray(regionBlocks);

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