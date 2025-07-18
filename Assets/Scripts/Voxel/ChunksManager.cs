using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
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

    public byte viewDistance;
    public byte yViewDistance;
    public int worldTotalSizeInChunks
    {
        get { return worldSizeInChunks * worldSizeInChunks * worldHeightInChunks; }
    }
    public int chunkSize;
    public int totalBlocks
    {
        get { return this.chunkSize * this.chunkSize * this.chunkSize;  }
    }
    public byte chunkInitListSize;

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

        // Destroy the old voxel chunk singleton if exist //
        if (SystemAPI.HasSingleton<VoxelChunkSingleton>())
        {
            Entity vcsOldEntity = SystemAPI.GetSingletonEntity<VoxelChunkSingleton>();
            Utils.DestroyVoxelChunkSingleton(SystemAPI.GetSingleton<VoxelChunkSingleton>());
            state.EntityManager.DestroyEntity(vcsOldEntity);
        }

        // Create the voxel chunk singleton //
        Entity vcsEntity = state.EntityManager.CreateEntity();
        EntitiesGraphicsSystem gfxSys = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<EntitiesGraphicsSystem>();
        state.EntityManager.AddComponentData(vcsEntity, new VoxelChunkSingleton
        {
            chunkToBuildQueue = new NativeQueue<Entity>(Allocator.Persistent),
            chunkJobList = new NativeList<ChunkData>(Allocator.Persistent),
            matID = gfxSys.RegisterMaterial(VoxelWorld._Instance.Materials[0])
        });

        // Destroy the old settings singleton //
        if (SystemAPI.HasSingleton<VoxelManagerSettings>())
        {
            Entity vmsOldEntity = SystemAPI.GetSingletonEntity<VoxelManagerSettings>();
            state.EntityManager.DestroyEntity(vmsOldEntity);
        }

        // Create the settings singleton //
        Entity vmsEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(vmsEntity, new VoxelManagerSettings
        {
            worldSizeInChunks = worldSizeInChunks,
            worldHeightInChunks = worldHeightInChunks,

            viewDistance = world.viewDistance,
            yViewDistance = world.yViewDistance,
            chunkSize = chunkSize,
            chunkInitListSize = world.chunkInitListSize,

            doFloodFill = world.doFloodFill,
            doLinearFloodFill = world.doLinearFloodFill,
            doFacesOcclusion = world.doFacesOcclusion,
            doGreedyMeshing = world.doGreedyMeshing,
            doFaceNormalCheck = world.doFaceNormalCheck,
            doVoxelCastOcclusion = world.doVoxelCastOcclusion
        });

        // Kill all pools //
        NativePoolsManager.DisposeAll();
        MeshPoolManager.DisposeAll();

        // Get the chunks map //
        NativeParallelHashMap<int3, Entity> chunksMap = VoxelWorld._ChunkManager.chunksMap;
        chunksMap.Clear();

        // Destroy all previous chunks //
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach ((RefRO<ChunkPosition> pos, Entity entity) in SystemAPI.Query<RefRO<ChunkPosition>>().WithEntityAccess())
        {
            ecb.DestroyEntity(entity);
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();

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

        // Set the initialization as done //
        VoxelWorld._Instance.requestWorldInit = false;

    }

}

//public partial struct UpdateChunks : ISystem
//{

//    public void OnUpdate(ref SystemState state)
//    {

//    }

//}

public class ChunkSManager : MonoBehaviour
{

    public NativeParallelHashMap<int3, Entity> chunksMap;

    void Awake()
    {

        // Init the Map //
        this.chunksMap = new NativeParallelHashMap<int3, Entity>(VoxelWorld._Instance.worldTotalSizeInChunks, Allocator.Persistent);

    }

    void OnDestroy()
    {
        if (this.chunksMap.IsCreated)
            this.chunksMap.Dispose();
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