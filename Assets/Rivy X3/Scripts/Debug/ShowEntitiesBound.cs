#if UNITY_EDITOR
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Dessine tous les RenderBounds DOTS dans la Scene View.
/// À placer dans Assets/Editor/ ou n’importe où sous Editor.
/// </summary>
public class RenderBoundsGizmoDrawer : MonoBehaviour
{

    public bool showAllBound = false;
    public bool showAllCubes = false;
    public bool showAllChunks = false;
    public bool showAllRegion = false;


    void OnDrawGizmos()
    {

        if (showAllBound == true)
        {
            var world = World.DefaultGameObjectInjectionWorld;          // monde Editor ou Play
            if (world == null) return;

            var entityManager = world.EntityManager;
            var renderBoundsType = ComponentType.ReadOnly<RenderBounds>();
            var localToWorldType = ComponentType.ReadOnly<LocalToWorld>();
            var query = entityManager.CreateEntityQuery(renderBoundsType, localToWorldType);

            using var boundsArray = query.ToComponentDataArray<RenderBounds>(Allocator.Temp);
            using var localToWorldArray = query.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

            Gizmos.color = Color.yellow;

            for (int i = 0; i < boundsArray.Length; i++)
            {
                var aabb = boundsArray[i].Value;
                var l2w = localToWorldArray[i].Value;

                // Transforme le center local en world position
                Vector3 worldCenter = l2w.TransformPoint(aabb.Center);

                // NB : extents (tailles) ne sont PAS affectés par la translation,
                // mais ils doivent être éventuellement adaptés si tu fais du scaling non uniforme.
                Vector3 worldExtents = aabb.Extents; // Si pas de scale, sinon multiplie par scale

                Gizmos.DrawWireCube(worldCenter, worldExtents * 2f);
            }
        }

        if (showAllCubes == true)
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

    public void Update()
    {

        if (Utils.TryGetSingletonECS<DataSingleton>(out DataSingleton DS) == false)
            return;

        if (showAllChunks == true)
        {
            var chunksEntries = DS.chunksMap.GetKeyValueArrays(Allocator.Temp);
            for (int i = 0; i < chunksEntries.Length; i++)
            {
                int3 pos = chunksEntries.Keys[i];
                Utils.DebugDrawChunkBounds(pos, VoxelWorld._Instance.chunkSize, UnityEngine.Color.blue);
            }
        }

        if(showAllRegion == true)
        {
            var regionEntries = DS.regionsMap.GetKeyValueArrays(Allocator.Temp);
            for (int i = 0; i < regionEntries.Length; i++)
            {
                int3 pos = regionEntries.Keys[i];
                Utils.DebugDrawRegionBounds(pos, VoxelWorld._Instance.regionBlocksCount, VoxelWorld._Instance.chunkSize, UnityEngine.Color.magenta);
            }
        }

    }

}
#endif