using Assets.Scripts.Block;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.UniversalDelegates;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using static Atlas;
using static UnityEditor.PlayerSettings;
using static UnityEngine.EventSystems.EventTrigger;

public struct RegionCoord : IComponentData { public int3 Value; }

public struct RegionChunks : IBufferElementData { public Entity ChunkEntity; }

public struct RegionToRender : IComponentData, IEnableableComponent { }


[UpdateInGroup(typeof(ChunkPipelineGroup))]
[UpdateAfter(typeof(InitChunks))]
public partial struct RegionsSystem : ISystem
{

    public void OnUpdate(ref SystemState state)
    {
        // Get all region that update its render //
        foreach ((RefRO<RegionToRender> _, Entity entity) in SystemAPI.Query<RefRO<RegionToRender>>().WithEntityAccess())
        {
            state.EntityManager.SetComponentEnabled<RegionToRender>(entity, false);
            EntityManager entityManager = state.EntityManager;
            VoxelRegion.GenerateMesh(ref entityManager, entity, entityManager.GetComponentData<RegionCoord>(entity).Value);
        }

    }

}

public static class VoxelRegion
{

    public static void AddChunkToRegion(ref SystemState state, int3 position, Entity chunkEntity, int regionSize)
    {

        // Get the entity manager //
        EntityManager entityManager = state.EntityManager;

        // Get the region map //
        ref NativeParallelHashMap<int3, Entity> regionMap = ref VoxelWorld._ChunkManager.regionMap;

        // Get region coord //
        int3 regionCoord = position / regionSize;
        float3 worldPos = position * regionSize;

        // Check if the region exist or create it //
        Entity regionEntity;
        if (!regionMap.TryGetValue(regionCoord, out regionEntity))
        {
            regionEntity = CreateRegion(ref state, ref entityManager, regionCoord, worldPos, regionSize);
            regionMap.Add(regionCoord, regionEntity);
        }
        else
        {
            state.EntityManager.SetComponentEnabled<RegionToRender>(regionEntity, true);
        }

        DynamicBuffer<RegionChunks> buffer = entityManager.GetBuffer<RegionChunks>(regionEntity);
        buffer.Add(new RegionChunks { ChunkEntity = chunkEntity });

    }

    public static Entity CreateRegion(ref SystemState state, ref EntityManager entityManager, int3 regionCoord, float3 worldPos, int regionSize)
    {

        
        // Create the entity //
        Entity regionEntity = entityManager.CreateEntity();

        // Add all component //
        entityManager.AddComponentData(regionEntity, new RegionCoord { Value = regionCoord });
        entityManager.AddComponent<RegionToRender>(regionEntity);
        entityManager.AddBuffer<RegionChunks>(regionEntity);
        entityManager.AddComponentData(regionEntity, LocalTransform.FromPosition(worldPos));

        Mesh mesh = GenerateMesh(ref entityManager, regionEntity, regionCoord);

        // Add the render components //
        EntitiesGraphicsSystem gfx = state.World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
        //BatchMaterialID batchMatID = gfx.RegisterMaterial(VoxelWorld._Instance.Materials[0]);
        BatchMeshID batchMeshID = gfx.RegisterMesh(mesh);
        RenderMeshDescription desc = new RenderMeshDescription(shadowCastingMode: UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows: true);
        MaterialMeshInfo mmi = new MaterialMeshInfo { MeshID = batchMeshID, MaterialID = VoxelWorld._Instance.MaterialID };
        RenderMeshUtility.AddComponents(regionEntity, state.EntityManager, desc, mmi);

        // Set the bounds //
        float3 center = worldPos + new float3(regionSize * 0.5f);
        float3 extents = new float3(regionSize * 0.5f);
        AABB bounds = new AABB { Center = center, Extents = extents };
        state.EntityManager.SetComponentData(regionEntity, new RenderBounds { Value = bounds });

        return regionEntity;

    }

    public static Mesh GenerateMesh(ref EntityManager entityManager, Entity regionEntity, int3 regionCood)
    {

        // Get atlas and blocks count //
        int chunkBlocksCount = VoxelWorld._Instance.chunkBlocksCount;
        AtlasData atlas = VoxelWorld._Instance._Atlas;

        // Create the lists //
        NativeList<float3> verticesList = NativesPool<float3>.GetList(chunkBlocksCount * 6 * 4);
        NativeList<int> trianglesList = NativesPool<int>.GetList(chunkBlocksCount * 6 * 6);
        NativeList<float2> uvsList = NativesPool<float2>.GetList(chunkBlocksCount * 6 * 4);

        // Get the region buffer //
        DynamicBuffer<RegionChunks> chunksBuffer = entityManager.GetBuffer<RegionChunks>(regionEntity);

        // Itinerate all chunks //
        foreach(RegionChunks chunk in chunksBuffer)
        {

            // Check if the chunk was created //
            if (entityManager.HasComponent<ChunkSquareFaces>(chunk.ChunkEntity))
            {
                // Get the squares buffer //
                DynamicBuffer<ChunkSquareFaces> squaresBuffer = entityManager.GetBuffer<ChunkSquareFaces>(chunk.ChunkEntity);

                // Generate the Lists //
                for (int i = 0; i < squaresBuffer.Length; i++)
                {
                    ChunkSquareFaces squareFace = squaresBuffer[i];
                    squareFace.GetSquare(ref verticesList);
                    squareFace.GetTriangles(i * 4, ref trianglesList);
                    squareFace.GetUVs(ref uvsList, atlas);
                }
            }

        }

        // Get the old mesh or get a new one //
        Mesh mesh;
        if (VoxelWorld._ChunkManager.meshMap.TryGetValue(regionCood, out mesh) == false)
            mesh = MeshPoolManager.GetMesh();

        // Set the mesh //
        int3 pos = entityManager.GetComponentData<RegionCoord>(regionEntity).Value;
        mesh.name = $"Region_{pos.x}_{pos.y}_{pos.z}";
        mesh.SetVertices(verticesList.AsArray());
        mesh.SetIndices(trianglesList.AsArray(), MeshTopology.Triangles, 0);
        mesh.SetUVs(0, uvsList.AsArray());
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);

        // Add the mesh to the mesh table //
        VoxelWorld._ChunkManager.meshMap[regionCood] = mesh;

        // Release all natives //
        NativesPool<float3>.ReleaseList(verticesList);
        NativesPool<int>.ReleaseList(trianglesList);
        NativesPool<float2>.ReleaseList(uvsList);

        // Return the mesh //
        return mesh;

    }

}