#if UNITY_EDITOR
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Draws a wire cube at the position of every renderable block in the world.
/// Attach to any GameObject to visualize blocks in the Scene view.
/// </summary>
public class ShowBlocks : MonoBehaviour
{
    void OnDrawGizmos()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || VoxelWorld._Instance == null)
            return;

        var entityManager = world.EntityManager;
        var query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ChunkPosition>(),
            ComponentType.ReadOnly<BlockData>());

        using var entities = query.ToEntityArray(Allocator.Temp);
        using var positions = query.ToComponentDataArray<ChunkPosition>(Allocator.Temp);

        int chunkSize = VoxelWorld._Instance.chunkSize;
        Gizmos.color = new Color(1f, 0f, 0f, 0.5f);

        for (int i = 0; i < entities.Length; i++)
        {
            DynamicBuffer<BlockData> blocks = entityManager.GetBuffer<BlockData>(entities[i]);
            int3 chunkPos = positions[i].Value;

            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int idx = Utils.PosToIndex(chunkSize, x, y, z);
                        if (idx >= blocks.Length) continue;
                        BlockData block = blocks[idx];
                        if (!block.IsRenderable())
                            continue;

                        Vector3 worldPos = new Vector3(
                            chunkPos.x * chunkSize + x + 0.5f,
                            chunkPos.y * chunkSize + y + 0.5f,
                            chunkPos.z * chunkSize + z + 0.5f);
                        Gizmos.DrawWireCube(worldPos, Vector3.one);
                    }
                }
            }
        }
    }
}
#endif