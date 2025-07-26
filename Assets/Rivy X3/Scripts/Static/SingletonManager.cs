using Assets.Scripts.Block;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static EnumData;


public struct WorldSettings : IComponentData
{

    public byte worldSizeInChunks;
    public byte worldHeightInChunks;
    public int worldTotalSizeInChunks;

    public int regionSize;
    public int chunkSize;
    public int chunkBlocksCount;
    public byte chunkInitListSize;

    public int maxRegionDistance;
    public int nearRegionDistance;
    public int playerContactRegionDistance;
    public int yViewDistance;

    public bool doFloodFill;
    public bool doLinearFloodFill;
    public bool doFacesOcclusion;
    public bool doGreedyMeshing;
    public bool doFaceNormalCheck;
    public bool removeFullAirChunk;

}

public struct DataSingleton : IComponentData
{

    public NativeParallelHashMap<int3, Entity> chunksMap;
    public NativeParallelHashMap<int3, Entity> regionsMap;

    public NativeQueue<Entity> chunkToBuildQueue;
    public NativeList<ChunkData> chunkJobList;

    public BatchMaterialID matID;

    public Entity GetChunk(Vector3Int pos, Direction direction = Direction.None)
    {
        return ChunksManager.GetChunk(this.chunksMap, pos.x, pos.y, pos.z, direction);
    }

}

[UpdateInGroup(typeof(ChunkPipelineGroup))]
public partial struct SingletonManager : ISystem
{

    public void OnCreate(ref SystemState state)
    {

        // Get the world //
        VoxelWorld world = VoxelWorld._Instance;

        // Create the settings singleton //
        Entity WSEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(WSEntity, new WorldSettings{});
        this.UpdateSettingsSingleton();

        // Create data singleton //
        Entity WDEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(WDEntity, new DataSingleton
        {
            chunksMap = new NativeParallelHashMap<int3, Entity>(world.worldTotalSizeInChunk, Allocator.Persistent),
            regionsMap = new NativeParallelHashMap<int3, Entity>(world.worldTotalSizeInChunk, Allocator.Persistent),
            chunkToBuildQueue = new NativeQueue<Entity>(Allocator.Persistent),
            chunkJobList = new NativeList<ChunkData>(Allocator.Persistent),
            matID = world.MaterialID
        });

    }

    public void OnDestroy(ref SystemState state)
    {

        // Destroy the settings singleton //
        if (SystemAPI.HasSingleton<WorldSettings>())
        {
            Entity WS = SystemAPI.GetSingletonEntity<WorldSettings>();
            state.EntityManager.DestroyEntity(WS);
        }

        // Destroy the data singleton //
        if (SystemAPI.HasSingleton<DataSingleton>())
        {
            Entity DS = SystemAPI.GetSingletonEntity<DataSingleton>();
            DestroyVoxelChunkSingleton(SystemAPI.GetSingleton<DataSingleton>());
            state.EntityManager.DestroyEntity(DS);
        }

        // Kill all pools //
        NativePoolManager.DisposeAll();
        MeshPoolManager.DisposeAll();

    }

    public void OnUpdate(ref SystemState state)
    {

        // Update the setting singleton //
        if (VoxelWorld._Instance.MustUpdateSingleton == true)
        {
            this.UpdateSettingsSingleton();
            VoxelWorld._Instance.MustUpdateSingleton = false;
        }

    }

    public void UpdateSettingsSingleton()
    {

        VoxelWorld world = VoxelWorld._Instance;

        SystemAPI.SetSingleton<WorldSettings>(new WorldSettings()
        {

            worldSizeInChunks = world.worldSizeInChunks,
            worldHeightInChunks = world.worldHeightInChunks,
            worldTotalSizeInChunks = world.worldTotalSizeInChunk,

            regionSize = world.regionSize,
            chunkSize = world.chunkSize,
            chunkBlocksCount = world.chunkBlocksCount,
            chunkInitListSize = world.chunkInitListSize,

            maxRegionDistance = world.maxRegionDistance,
            nearRegionDistance = world.nearRegionDistance,
            playerContactRegionDistance = world.playerContactRegionDistance,
            yViewDistance = world.yViewDistance,


            doFloodFill = world.doFloodFill,
            doLinearFloodFill = world.doLinearFloodFill,
            doFacesOcclusion = world.doFacesOcclusion,
            doGreedyMeshing = world.doGreedyMeshing,
            doFaceNormalCheck = world.doFaceNormalCheck,
            removeFullAirChunk = world.removeFullAirChunk

        });

    }

    public void DestroyVoxelChunkSingleton(DataSingleton vcs)
    {

        // Destroy the region map //
        if (vcs.regionsMap.IsCreated)
            vcs.regionsMap.Dispose();

        // Destroy the chunks map //
        if (vcs.chunksMap.IsCreated)
            vcs.chunksMap.Dispose();

        // Destroy the chunks to build queue //
        if (vcs.chunkToBuildQueue.IsCreated)
            vcs.chunkToBuildQueue.Dispose();

        // End all chunk build list //
        if (vcs.chunkJobList.IsCreated)
        {
            for (int i = vcs.chunkJobList.Length - 1; i >= 0; i--)
            {
                ChunkData chunkData = vcs.chunkJobList[i];
                chunkData.job.Complete();
                DisposeVCSAllNatives(chunkData);
            }
            vcs.chunkJobList.Dispose();
        }

    }

    public static void DisposeVCSAllNatives(ChunkData chunkData)
    {

        // Dispose all tables //
        NativesPoolManager<int3>.ReleaseList(chunkData.frontier);
        NativesPoolManager<byte>.ReleaseArray(chunkData.floodVisited);
        NativesPoolManager<byte>.ReleaseArray(chunkData.linearFloodVisited);
        NativesPoolManager<BlockRender>.ReleaseArray(chunkData.blockRenders);
        NativesPoolManager<ChunkSquareFaces>.ReleaseList(chunkData.squareFaces);

    }

}