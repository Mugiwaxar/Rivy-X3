using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using static EnumData;


[WorldSystemFilter(WorldSystemFilterFlags.Default)]
public partial class ChunkPipelineGroup : ComponentSystemGroup { }

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

        // Kill all tables pool //
        NativePoolsManager.DisposeAll();

        // Get the chunks map //
        NativeParallelHashMap<int3, Entity> chunksMap = VoxelWorld._ChunkManager.chunksMap;
        chunksMap.Clear();

        // Destroy all previous chunks //
        var ecb = new EntityCommandBuffer(Allocator.Temp);
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
    public Mesh DummyCube;

    void Awake()
    {

        // Init the Map //
        this.chunksMap = new NativeParallelHashMap<int3, Entity>(VoxelWorld._Instance.worldTotalSizeInChunks, Allocator.Persistent);

        // Create the Dummw Cube //
        GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh unityCube = tempCube.GetComponent<MeshFilter>().sharedMesh;
        this.DummyCube = GameObject.Instantiate(unityCube);
        GameObject.DestroyImmediate(tempCube);
        Vector3[] v = unityCube.vertices;
        for (int i = 0; i < v.Length; i++) v[i] *= VoxelWorld._Instance.chunkSize;
        unityCube.vertices = v;
        unityCube.RecalculateBounds();
        unityCube.name = $"DummyChunk_{VoxelWorld._Instance.chunkSize}";
        unityCube.UploadMeshData(false);

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