using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.VisualScripting;
using UnityEngine;
using static BuildMesh;
using static EnumData;
using static UnityEngine.EventSystems.EventTrigger;


[WorldSystemFilter(WorldSystemFilterFlags.Default)]
public partial class ChunkPipelineGroup : ComponentSystemGroup { }

public struct VoxelManagerSettings : IComponentData
{

    public byte worldSizeInChunks;
    public byte worldHeightInChunks;
    public int worldTotalSizeInChunks;

    public int regionSize;
    public int chunkSize;
    public int chunkBlocksCount;
    public byte chunkInitListSize;

    public byte viewDistance;
    public byte yViewDistance;

    public bool doFloodFill;
    public bool doLinearFloodFill;
    public bool doFacesOcclusion;
    public bool doGreedyMeshing;
    public bool doFaceNormalCheck;
    public bool doVoxelCastOcclusion;

}

[UpdateInGroup(typeof(ChunkPipelineGroup))]
public partial struct InitChunks : ISystem
{

    public void OnUpdate(ref SystemState state)
    {

        // Check if the world must init //
        if (VoxelWorld._Instance.requestWorldInit == false)
            return;

        // Get the world settings //
        VoxelWorld world = VoxelWorld._Instance;
        byte worldSizeInChunks = world.worldSizeInChunks;
        byte worldHeightInChunks = world.worldHeightInChunks;
        int chunkSize = world.chunkSize;

        // Create the voxel chunk singleton //
        Entity vcsEntity = state.EntityManager.CreateEntity();

        state.EntityManager.AddComponentData(vcsEntity, new VoxelChunkSingleton
        {
            chunkToBuildQueue = new NativeQueue<Entity>(Allocator.Persistent),
            chunkJobList = new NativeList<ChunkData>(Allocator.Persistent),
            matID = world.MaterialID
        });

        // Create the settings singleton //
        Entity vmsEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(vmsEntity, new VoxelManagerSettings
        {
            worldSizeInChunks = worldSizeInChunks,
            worldHeightInChunks = worldHeightInChunks,
            worldTotalSizeInChunks = world.worldTotalSizeInChunk,

            regionSize = world.regionSize,
            chunkSize = chunkSize,
            chunkBlocksCount = world.chunkBlocksCount,
            chunkInitListSize = world.chunkInitListSize,

            viewDistance = world.viewDistance,
            yViewDistance = world.yViewDistance,


            doFloodFill = world.doFloodFill,
            doLinearFloodFill = world.doLinearFloodFill,
            doFacesOcclusion = world.doFacesOcclusion,
            doGreedyMeshing = world.doGreedyMeshing,
            doFaceNormalCheck = world.doFaceNormalCheck,
            doVoxelCastOcclusion = world.doVoxelCastOcclusion
        });

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
                    // Create the chunk entity //
                    Entity chunkEntity = ChunksGenerator.CreateChunk(ref state, position, chunkSize);
                    // Add it to the chunks map //
                    chunksMap.TryAdd(position, chunkEntity);
                    // Add it to the region //
                    VoxelRegion.AddChunkToRegion(ref state, position, chunkEntity, world.regionSize);
                }
            }
        }

        // Set the initialization as done //
        VoxelWorld._Instance.requestWorldInit = false;

    }

    public void OnDestroy(ref SystemState state)
    {

        // Destroy the old voxel chunk singleton if exist //
        if (SystemAPI.HasSingleton<VoxelChunkSingleton>())
        {
            Entity vcsOldEntity = SystemAPI.GetSingletonEntity<VoxelChunkSingleton>();
            Utils.DestroyVoxelChunkSingleton(SystemAPI.GetSingleton<VoxelChunkSingleton>());
            state.EntityManager.DestroyEntity(vcsOldEntity);
        }

        // Destroy the old settings singleton //
        if (SystemAPI.HasSingleton<VoxelManagerSettings>())
        {
            Entity vmsOldEntity = SystemAPI.GetSingletonEntity<VoxelManagerSettings>();
            state.EntityManager.DestroyEntity(vmsOldEntity);
        }

        // Kill all pools //
        NativePoolsManager.DisposeAll();
        MeshPoolManager.DisposeAll();

        // Destroy all previous chunks //
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach ((RefRO<ChunkPosition> pos, Entity entity) in SystemAPI.Query<RefRO<ChunkPosition>>().WithEntityAccess())
        {
            ecb.DestroyEntity(entity);
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();

        // Clear the chunks map //
        NativeParallelHashMap<int3, Entity> chunksMap = VoxelWorld._ChunkManager.chunksMap;
        if(chunksMap.IsCreated)
            chunksMap.Clear();

        // Clear the region map //
        NativeParallelHashMap<int3, Entity> regionMap = VoxelWorld._ChunkManager.regionMap;
        if (regionMap.IsCreated)
            regionMap.Clear();

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

    public NativeParallelHashMap<int3, Entity> chunksMap;
    public NativeParallelHashMap<int3, Entity> regionMap;
    public Dictionary<int3, Mesh> meshMap;

    void Awake()
    {

        // Init the maps //
        this.chunksMap = new NativeParallelHashMap<int3, Entity>(VoxelWorld._Instance.worldTotalSizeInChunk, Allocator.Persistent);
        this.regionMap = new NativeParallelHashMap<int3, Entity>(VoxelWorld._Instance.worldTotalSizeInChunk, Allocator.Persistent);
        this.meshMap = new Dictionary<int3, Mesh>();

}

    void OnDestroy()
    {
        if (this.chunksMap.IsCreated)
            this.chunksMap.Dispose();
        if (this.regionMap.IsCreated)
            this.regionMap.Dispose();
    }

    void Update()
    {
        //var cunksEntries = this.chunksMap.GetKeyValueArrays(Allocator.Temp);

        //for (int i = 0; i < cunksEntries.Length; i++)
        //{
        //    int3 pos = cunksEntries.Keys[i];
        //    Utils.DebugDrawChunkBounds(pos, VoxelWorld._Instance.chunkSize, UnityEngine.Color.blue);
        //}

        var regionEntries = this.regionMap.GetKeyValueArrays(Allocator.Temp);

        for (int i = 0; i < regionEntries.Length; i++)
        {
            int3 pos = regionEntries.Keys[i];
            Utils.DebugDrawRegionBounds(pos, VoxelWorld._Instance.regionSize, VoxelWorld._Instance.chunkSize, UnityEngine.Color.magenta);
        }
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