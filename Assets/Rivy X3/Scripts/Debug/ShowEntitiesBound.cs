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
            int yChunkSize = VoxelWorld._Instance.yChunkSize;
            Gizmos.color = new Color(1f, 0f, 0f, 0.5f);

            for (int i = 0; i < entities.Length; i++)
            {
                DynamicBuffer<BlockData> blocks = entityManager.GetBuffer<BlockData>(entities[i]);
                int3 chunkPos = positions[i].Value;

                for (int x = 0; x < chunkSize; x++)
                {
                    for (int y = 0; y < yChunkSize; y++)
                    {
                        for (int z = 0; z < chunkSize; z++)
                        {
                            int idx = Utils.PosToIndex(chunkSize,yChunkSize, x, y, z);
                            if (idx >= blocks.Length) continue;
                            BlockData block = blocks[idx];
                            if (!block.IsRenderable())
                                continue;

                            Vector3 worldPos = new Vector3(
                                chunkPos.x * chunkSize + x + 0.5f,
                                chunkPos.y * yChunkSize + y + 0.5f,
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
                DebugDrawChunkBounds(pos, VoxelWorld._Instance.chunkSize, VoxelWorld._Instance.yChunkSize, UnityEngine.Color.blue);
            }
        }

        if(showAllRegion == true)
        {
            var regionEntries = DS.regionsMap.GetKeyValueArrays(Allocator.Temp);
            for (int i = 0; i < regionEntries.Length; i++)
            {
                int3 pos = regionEntries.Keys[i];
                DebugDrawRegionBounds(
                    pos,
                    VoxelWorld._Instance.regionSize * VoxelWorld._Instance.chunkSize,
                    VoxelWorld._Instance.yChunkSize,
                    UnityEngine.Color.magenta);
            }
        }

    }

    static public void DebugDrawChunkBounds(int3 chunkPos, int chunkSize, int yChunkSize, Color color)
    {
        Vector3 min = new Vector3(chunkPos.x * chunkSize,
                                  chunkPos.y * yChunkSize,
                                  chunkPos.z * chunkSize);

        Vector3 max = new Vector3(min.x + chunkSize,
                                  min.y + yChunkSize,
                                  min.z + chunkSize);

        Vector3[] corners = new Vector3[8];

        // Base
        corners[0] = new Vector3(min.x, min.y, min.z);
        corners[1] = new Vector3(max.x, min.y, min.z);
        corners[2] = new Vector3(max.x, min.y, max.z);
        corners[3] = new Vector3(min.x, min.y, max.z);

        // Top
        corners[4] = new Vector3(min.x, max.y, min.z);
        corners[5] = new Vector3(max.x, max.y, min.z);
        corners[6] = new Vector3(max.x, max.y, max.z);
        corners[7] = new Vector3(min.x, max.y, max.z);

        // Draw base square
        Debug.DrawLine(corners[0], corners[1], color);
        Debug.DrawLine(corners[1], corners[2], color);
        Debug.DrawLine(corners[2], corners[3], color);
        Debug.DrawLine(corners[3], corners[0], color);

        // Draw top square
        Debug.DrawLine(corners[4], corners[5], color);
        Debug.DrawLine(corners[5], corners[6], color);
        Debug.DrawLine(corners[6], corners[7], color);
        Debug.DrawLine(corners[7], corners[4], color);

        // Connect verticals
        Debug.DrawLine(corners[0], corners[4], color);
        Debug.DrawLine(corners[1], corners[5], color);
        Debug.DrawLine(corners[2], corners[6], color);
        Debug.DrawLine(corners[3], corners[7], color);
    }

    static public void DebugDrawRegionBounds(int3 regionCoord, int regionSizeInBlocks, int yChunkSize, Color color)
    {

        Vector3 min = new Vector3(
            regionCoord.x * regionSizeInBlocks,
            regionCoord.y * yChunkSize,
            regionCoord.z * regionSizeInBlocks);
        Vector3 max = min + new Vector3(
            regionSizeInBlocks,
            yChunkSize,
            regionSizeInBlocks);

        Vector3[] corners = new Vector3[8];

        // Base
        corners[0] = new Vector3(min.x, min.y, min.z);
        corners[1] = new Vector3(max.x, min.y, min.z);
        corners[2] = new Vector3(max.x, min.y, max.z);
        corners[3] = new Vector3(min.x, min.y, max.z);

        // Top
        corners[4] = new Vector3(min.x, max.y, min.z);
        corners[5] = new Vector3(max.x, max.y, min.z);
        corners[6] = new Vector3(max.x, max.y, max.z);
        corners[7] = new Vector3(min.x, max.y, max.z);

        // Draw base square
        Debug.DrawLine(corners[0], corners[1], color);
        Debug.DrawLine(corners[1], corners[2], color);
        Debug.DrawLine(corners[2], corners[3], color);
        Debug.DrawLine(corners[3], corners[0], color);

        // Draw top square
        Debug.DrawLine(corners[4], corners[5], color);
        Debug.DrawLine(corners[5], corners[6], color);
        Debug.DrawLine(corners[6], corners[7], color);
        Debug.DrawLine(corners[7], corners[4], color);

        // Connect verticals
        Debug.DrawLine(corners[0], corners[4], color);
        Debug.DrawLine(corners[1], corners[5], color);
        Debug.DrawLine(corners[2], corners[6], color);
        Debug.DrawLine(corners[3], corners[7], color);
    }

}
#endif