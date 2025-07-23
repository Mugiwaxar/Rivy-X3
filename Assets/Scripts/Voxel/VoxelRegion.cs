using Assets.Scripts.Block;
using System;
using System.Drawing;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using static Atlas;
using static EnumData;
using static UnityEngine.EventSystems.EventTrigger;
using static UnityEngine.Rendering.HighDefinition.ScalableSettingLevelParameter;

public struct RegionCoord : IComponentData { public int3 Value; }
public struct RegionChunks : IBufferElementData { public Entity ChunkEntity; }
public struct RegionLOD : IComponentData { public LODLevel Level; }
public struct RegionNeedChunks : IComponentData, IEnableableComponent { }
public struct RegionNeedRender : IComponentData, IEnableableComponent { }

public struct RegionsInfo
{
    public int3 coord;
    public Entity entity;
    public LODLevel level;
}


[UpdateInGroup(typeof(ChunkPipelineGroup))]
[UpdateAfter(typeof(InitChunks))]
public partial struct PopulateRegionSystem : ISystem
{

    NativeQueue<Entity> nativeQueueJob;

    public void OnCreate(ref SystemState state)
    {
        this.nativeQueueJob = new NativeQueue<Entity>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        this.nativeQueueJob.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {

        // Get the singletons //
        if (SystemAPI.TryGetSingleton<DataSingleton>(out DataSingleton DS) == false) return;
        if (SystemAPI.TryGetSingleton<WorldSettings>(out WorldSettings WS) == false) return;

        // Get all regions that need to populate with chunks //
        foreach ((var _, Entity regionEntity) in SystemAPI.Query<RefRO<RegionNeedChunks>>().WithEntityAccess())
        {

            // Disable the need chunks //
            SystemAPI.SetComponentEnabled<RegionNeedChunks>(regionEntity, false);

            // Add the entity to the queue //
            this.nativeQueueJob.Enqueue(regionEntity);



        }

        // Populate regions one by one //
        if (this.nativeQueueJob.Count > 0)
        {
            
            // Get the region //
            Entity regionEntity = nativeQueueJob.Dequeue();

            // Get the coord //
            int3 coord = state.EntityManager.GetComponentData<RegionCoord>(regionEntity).Value;

            // Get the chunks buffer //
            DynamicBuffer<RegionChunks> chunksBuffer = SystemAPI.GetBuffer<RegionChunks>(regionEntity);

            // Create all chunks //
            ChunksManager.GenerateAllChunksInRegion(ref state, coord, WS, DS, chunksBuffer, WS.regionSize * WS.regionSize * WS.regionSize);

        }

    }

}

[UpdateInGroup(typeof(ChunkPipelineGroup))]
[UpdateAfter(typeof(InitChunks))]
public partial struct UpdateRegionsSystem : ISystem
{

    public void OnUpdate(ref SystemState state)
    {

        // Get the singletons //
        if (SystemAPI.TryGetSingleton<DataSingleton>(out DataSingleton DS) == false) return;
        if (SystemAPI.TryGetSingleton<WorldSettings>(out WorldSettings WS) == false) return;

        // Create the native list //
        NativeList<RegionsInfo> regionsToDestroy = new NativeList<RegionsInfo>(Allocator.Temp);

        // Get all region that need to update its render //
        foreach ((var _, var lodLevelRef, var coord, Entity regionEntity) in SystemAPI.Query<RefRO<RegionNeedRender>, RefRO<RegionLOD>, RefRO<RegionCoord>>().WithDisabled<RegionNeedChunks>().WithEntityAccess())
        {

            // Disable the need to render //
            state.EntityManager.SetComponentEnabled<RegionNeedRender>(regionEntity, false);

            // Remove too far region //
            if (lodLevelRef.ValueRO.Level == LODLevel.TooFar)
            {
                regionsToDestroy.Add(new RegionsInfo() { coord = coord.ValueRO.Value, level = lodLevelRef.ValueRO.Level, entity = regionEntity });
            }
            // Update region that need to be updated //
            else
            {
                EntityManager entityManager = state.EntityManager;
                VoxelRegion.GenerateMesh(ref entityManager, regionEntity, coord.ValueRO.Value, WS.regionSize);
            }

        }

        // Destroy all needed regions //
        foreach (RegionsInfo info in regionsToDestroy)
        {
            DS.regionsMap.Remove(info.coord);
            VoxelRegion.RemoveRegion(ref state, info.entity, ref DS);
        }

        // Dispose the list //
        regionsToDestroy.Dispose();

    }

}

[UpdateInGroup(typeof(ChunkPipelineGroup))]
[UpdateAfter(typeof(InitChunks))]
public partial struct RegionManagerSystem : ISystem
{

    public void OnUpdate(ref SystemState state)
    {

        // Get the world setting singleton //
        if (SystemAPI.TryGetSingleton<WorldSettings>(out WorldSettings WS) == false) return;

        // Get the region LOD list //
        if (SystemAPI.TryGetSingleton<DataSingleton>(out DataSingleton DS) == false) return;

        // Get the player coord //
        int3 playerCoord = Utils.WorldPosToRegionCoord(Camera.main.transform.position, WS.regionSize * WS.chunkSize);

        // Create the nativeList //
        NativeList<RegionsInfo> regionsToCreate = new NativeList<RegionsInfo>(Allocator.Temp);

        // Check all regions around //
        for (int dx = -WS.maxRegionDistance; dx <= WS.maxRegionDistance; dx++)
            for (int dy = -WS.maxRegionDistance; dy <= WS.maxRegionDistance; dy++)
                for (int dz = -WS.maxRegionDistance; dz <= WS.maxRegionDistance; dz++)
                {

                    // Get the region coord //
                    int3 regionCoord = playerCoord + new int3(dx, dy, dz);

                    // Get the distance //
                    int distance = math.abs(regionCoord.x - playerCoord.x)
                        + math.abs(regionCoord.y - playerCoord.y)
                        + math.abs(regionCoord.z - playerCoord.z);

                    // Continue if too far //
                    if (distance > WS.maxRegionDistance)
                        continue;

                    // Get the wanted LOD //
                    LODLevel level = LODLevel.TooFar;
                    if (distance <= WS.playerContactRegionDistance)
                        level = LODLevel.PlayerContact;
                    else if (distance <= WS.nearRegionDistance)
                        level = LODLevel.Near;
                    else if (distance <= WS.maxRegionDistance)
                        level = LODLevel.Far;

                    // Get or create the region //
                    if (DS.regionsMap.TryGetValue(regionCoord, out Entity regionEntity))
                    {
                        RefRW<RegionLOD> regionLOD = SystemAPI.GetComponentRW<RegionLOD>(regionEntity);
                        if (regionLOD.ValueRO.Level != level)
                        {
                            regionLOD.ValueRW.Level = level;
                            state.EntityManager.SetComponentEnabled<RegionNeedRender>(regionEntity, true);
                        }
                    }
                    else
                    {
                        RegionsInfo region = new RegionsInfo() { coord=regionCoord, level=level };
                        regionsToCreate.Add(region);
                    }

                }

        // Remove all too far regions //
        foreach ((RefRO<RegionCoord> coord, Entity entity) in SystemAPI.Query<RefRO<RegionCoord>>().WithEntityAccess())
        {

            // Get the distance //
            int distance = math.abs(coord.ValueRO.Value.x - playerCoord.x)
                + math.abs(coord.ValueRO.Value.y - playerCoord.y)
                + math.abs(coord.ValueRO.Value.z - playerCoord.z);

            // Check if the region must be removed //
            if (distance > WS.maxRegionDistance)
            {
                state.EntityManager.SetComponentData<RegionLOD>(entity, new RegionLOD() { Level = LODLevel.TooFar });
                state.EntityManager.SetComponentEnabled<RegionNeedRender>(entity, true);
            }

        }

        // Create all needed regions //
        foreach (RegionsInfo info in regionsToCreate)
        {
            Entity regionEntity = VoxelRegion.CreateRegion(ref state, info.coord, WS, info.level);
            DS.regionsMap.Add(info.coord, regionEntity);
        }

        // Dispose the list //
        regionsToCreate.Dispose();

    }

}

public static class VoxelRegion
{

    //public static void AddChunkToRegion(ref SystemState state, int3 position, Entity chunkEntity, int regionSize, ref NativeParallelHashMap<int3, Entity> regionMap)
    //{

    //    // Get the entity manager //
    //    EntityManager entityManager = state.EntityManager;

    //    // Get region coord //
    //    int3 regionCoord = position / regionSize;
    //    float3 worldPos = regionCoord * regionSize * VoxelWorld._Instance.chunkSize;

    //    // Check if the region exist or create it //
    //    Entity regionEntity;
    //    if (!regionMap.TryGetValue(regionCoord, out regionEntity))
    //    {
    //        regionEntity = CreateRegion(ref state, ref entityManager, regionCoord, worldPos, regionSize);
    //        regionMap.Add(regionCoord, regionEntity);
    //    }
    //    else
    //    {
    //        state.EntityManager.SetComponentEnabled<RegionToRender>(regionEntity, true);
    //    }

    //    DynamicBuffer<RegionChunks> buffer = entityManager.GetBuffer<RegionChunks>(regionEntity);
    //    buffer.Add(new RegionChunks { ChunkEntity = chunkEntity });

    //}

    public static Entity CreateRegion(ref SystemState state, int3 regionCoord, WorldSettings WS, LODLevel lodLevel)
    {

        // Create the entity //
        EntityManager entityManager = state.EntityManager;
        Entity regionEntity = entityManager.CreateEntity();

        // Add all component //
        entityManager.AddComponentData(regionEntity, new RegionCoord { Value = regionCoord });
        entityManager.AddComponentData(regionEntity, new RegionLOD { Level = lodLevel });
        entityManager.AddComponent<RegionNeedChunks>(regionEntity);
        entityManager.AddComponent<RegionNeedRender>(regionEntity);
        entityManager.SetComponentEnabled<RegionNeedRender>(regionEntity, false);
        entityManager.AddBuffer<RegionChunks>(regionEntity);
        entityManager.AddComponentData(regionEntity, LocalTransform.FromPosition(Utils.RegionCoordToWorldPos(regionCoord, WS.regionSize * WS.chunkSize)));

        // Add the render components //
        Mesh mesh = MeshPoolManager.GetMesh();
        MeshPoolManager.SaveMesh(regionCoord, mesh);
        EntitiesGraphicsSystem gfx = state.World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
        //BatchMaterialID batchMatID = gfx.RegisterMaterial(VoxelWorld._Instance.Materials[0]);
        BatchMeshID batchMeshID = gfx.RegisterMesh(mesh);
        RenderMeshDescription desc = new RenderMeshDescription(shadowCastingMode: UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows: true);
        MaterialMeshInfo mmi = new MaterialMeshInfo { MeshID = batchMeshID, MaterialID = VoxelWorld._Instance.MaterialID };
        RenderMeshUtility.AddComponents(regionEntity, state.EntityManager, desc, mmi);

        // Set the bounds //
        int chunkSize = VoxelWorld._Instance.chunkSize;
        float3 center = new float3(WS.regionSize * chunkSize * 0.5f);
        float3 extents = new float3(WS.regionSize * chunkSize * 0.5f);
        AABB bounds = new AABB { Center = center, Extents = extents };
        state.EntityManager.SetComponentData(regionEntity, new RenderBounds { Value = bounds });

        // Return the Entity //
        return regionEntity;

    }

    public static void RemoveRegion(ref SystemState state, Entity regionEntity, ref DataSingleton DS)
    {

        // Get the coord //
        int3 regionCoord = state.EntityManager.GetComponentData<RegionCoord>(regionEntity).Value;

        // Release the mesh //
        MeshPoolManager.ReleaseSavedMesh(regionCoord);

        // Remove all chunks in the region //
        DynamicBuffer<RegionChunks> buffer = state.EntityManager.GetBuffer<RegionChunks>(regionEntity);
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach (RegionChunks rchunk in buffer)
        {
            int3 coord = state.EntityManager.GetComponentData<ChunkPosition>(rchunk.ChunkEntity).Value;
            DS.chunksMap.Remove(coord);
            ecb.DestroyEntity(rchunk.ChunkEntity);
        }

        // Remove the entity //
        state.EntityManager.DestroyEntity(regionEntity);
            
    }

    public static Mesh GenerateMesh(ref EntityManager entityManager, Entity regionEntity, int3 regionCoord, int3 regionSize)
    {

        // Get atlas and blocks count //
        int chunkSize = VoxelWorld._Instance.chunkSize;
        int chunkBlocksCount = VoxelWorld._Instance.chunkBlocksCount;
        int regionBlocksCount = VoxelWorld._Instance.regionBlocksCount;
        AtlasData atlas = VoxelWorld._Instance._Atlas;

        // Get the region buffer //
        DynamicBuffer<RegionChunks> chunksBuffer = entityManager.GetBuffer<RegionChunks>(regionEntity);

        // Count the faces //
        int totalFaces = 0;
        foreach (RegionChunks chunk in chunksBuffer)
        {
            if (entityManager.HasComponent<ChunkSquareFaces>(chunk.ChunkEntity))
                totalFaces += entityManager.GetBuffer<ChunkSquareFaces>(chunk.ChunkEntity).Length;
        }

        // Create the lists //
        NativeList<float3> verticesList = new NativeList<float3>(totalFaces * 4, Allocator.Temp);
        NativeList<int> trianglesList = new NativeList<int>(totalFaces * 6, Allocator.Temp);
        NativeList<float2> uvsList = new NativeList<float2>(totalFaces * 4, Allocator.Temp);

        // Itinerate all chunks //
        foreach(RegionChunks chunk in chunksBuffer)
        {

            // Check if the chunk was created //
            if (entityManager.HasComponent<ChunkPosition>(chunk.ChunkEntity) && entityManager.HasComponent<ChunkSquareFaces>(chunk.ChunkEntity))
            {

                // Get the chunk position //
                int3 chunkPos = entityManager.GetComponentData<ChunkPosition>(chunk.ChunkEntity).Value;

                // Get the squares buffer //
                DynamicBuffer<ChunkSquareFaces> squaresBuffer = entityManager.GetBuffer<ChunkSquareFaces>(chunk.ChunkEntity);

                // Calcule the offset //
                int3 offset = (chunkPos * chunkSize) - (regionCoord * regionSize * chunkSize);

                // Generate the Lists //
                for (int i = 0; i < squaresBuffer.Length; i++)
                {
                    ChunkSquareFaces squareFace = squaresBuffer[i];
                    int startIndex = verticesList.Length;
                    squareFace.GetSquare(ref verticesList, offset);
                    squareFace.GetTriangles(startIndex, ref trianglesList);
                    squareFace.GetUVs(ref uvsList, atlas);
                }
            }

        }

        // Get the current mesh or get a new one //
        Mesh mesh = MeshPoolManager.GetSavedMesh(regionCoord);

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
        MeshPoolManager.SaveMesh(regionCoord, mesh);

        // Release all natives //
        verticesList.Dispose();
        trianglesList.Dispose();
        uvsList.Dispose();

        // Return the mesh //
        return mesh;

    }

}