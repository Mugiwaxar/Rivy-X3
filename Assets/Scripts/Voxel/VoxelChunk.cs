using Assets.Scripts.Block;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.LightTransport;
using UnityEngine.Rendering;
using static BuildMesh;
using static ChunksGenerator;
using static Unity.Collections.AllocatorManager;
using static UnityEngine.Rendering.VirtualTexturing.Debugging;
using static VoxelRaycast;

public struct ChunkPosition : IComponentData {public int3 Value;}
public struct JustCreated : IComponentData, IEnableableComponent { }

public struct VoxelChunkSingleton : IComponentData
{

    public NativeQueue<Entity> chunkToBuildQueue;
    public NativeList<ChunkData> chunkJobList;
    public BatchMaterialID matID;

}

public struct ChunkData
{

    public Entity chunk;
    public JobHandle job;

    public NativeList<int3> frontier;
    public NativeArray<byte> floodVisited;
    public NativeArray<byte> linearFloodVisited;
    public NativeArray<BlockRender> blockRenders;
    public NativeList<SquareFace> squareList;

    public NativeList<float3> verticesList;
    public NativeList<int> trianglesList;
    public NativeList<float2> uvsList;


}

[UpdateInGroup(typeof(ChunkPipelineGroup))]
[UpdateAfter(typeof(InitChunks))]
public partial struct BuildMesh : ISystem
{

    public void OnDestroy(ref SystemState state)
    {
        // Destroy the voxel chunk singleton if exist //
        Entity vcsEntity = SystemAPI.GetSingletonEntity<VoxelChunkSingleton>();
        if (state.EntityManager.Exists(vcsEntity))
        {
            if (SystemAPI.HasSingleton<VoxelChunkSingleton>())
                Utils.DestroyVoxelChunkSingleton(SystemAPI.GetSingleton<VoxelChunkSingleton>());
            state.EntityManager.DestroyEntity(vcsEntity);
        }
    }

    public void OnUpdate(ref SystemState state)
    {

        // Check and get the voxel chunk singleton //
        if (SystemAPI.HasSingleton<VoxelChunkSingleton>() == false)
            return;
        VoxelChunkSingleton vcs = SystemAPI.GetSingleton<VoxelChunkSingleton>();

        // Check and get the voxel manager settings singleton //
        if (SystemAPI.HasSingleton<VoxelManagerSettings>() == false)
            return;
        VoxelManagerSettings vms = SystemAPI.GetSingleton<VoxelManagerSettings>();

        // Get all chunks that must be updated //
        foreach ((RefRO<JustCreated> _, Entity entity) in SystemAPI.Query<RefRO<JustCreated>>().WithEntityAccess())
        {
            vcs.chunkToBuildQueue.Enqueue(entity);
            state.EntityManager.SetComponentEnabled<JustCreated>(entity, false);
        }

        // Check all jobs //
        for (int i = vcs.chunkJobList.Length - 1; i >= 0; i--)
        {

            // Get the chunk data //
            ChunkData chunkData = vcs.chunkJobList[i];

            //if (chunkData.job.IsCompleted == true)
            //{

            // Complete the job //
            chunkData.job.Complete();

            //}

        }

        // Generate all mesh //
        for (int i = vcs.chunkJobList.Length - 1; i >= 0; i--)
        {

            // Get the chunk data //
            ChunkData chunkData = vcs.chunkJobList[i];

            // Build the mesh //
            this.generateMesh(ref state, vcs, chunkData.chunk, chunkData.verticesList, chunkData.trianglesList, chunkData.uvsList);

            // Dispose all natives //
            Utils.DisposeVCSAllNatives(chunkData);

            // Remove the chunkData //
            vcs.chunkJobList.RemoveAtSwapBack(i);

        }

        // Add jobs //
        int totalBlock = vms.totalBlocks;
        while (vcs.chunkToBuildQueue.Count > 0 && vcs.chunkJobList.Length <= vms.chunkInitListSize)
        {

            // Create the chunk data //
            ChunkData chunkData;

            // Get the entity //
            chunkData.chunk = vcs.chunkToBuildQueue.Dequeue();

            chunkData.frontier = NativesPool<int3>.GetList(totalBlock);
            chunkData.floodVisited = NativesPool<byte>.GetArray(totalBlock);
            chunkData.linearFloodVisited = NativesPool<byte>.GetArray(totalBlock);
            chunkData.blockRenders = NativesPool<BlockRender>.GetArray(totalBlock);
            chunkData.squareList = NativesPool<SquareFace>.GetList(totalBlock * 3);

            chunkData.verticesList = NativesPool<float3>.GetList(totalBlock * 6 * 4);
            chunkData.trianglesList = NativesPool<int>.GetList(totalBlock * 6 * 6);
            chunkData.uvsList = NativesPool<float2>.GetList(totalBlock * 6 * 4);

            // Create the job //
            int chunkSize = vms.chunkSize;
            chunkData.job = new GenerateChunksGraphics
            {

                vms = vms,

                pos = state.EntityManager.GetComponentData<ChunkPosition>(chunkData.chunk).Value,
                chunkCenter = new float3(chunkSize * 0.5f, chunkSize * 0.5f, chunkSize * 0.5f),
                cameraPosition = Camera.main.transform.position,
                chunkMap = VoxelWorld._ChunkManager.chunksMap,
                blocksLookup = SystemAPI.GetBufferLookup<BlockData>(true),
                atlas = VoxelWorld._Instance._Atlas,

                frontier = chunkData.frontier,
                floodVisited = chunkData.floodVisited,
                linearFloodVisited = chunkData.linearFloodVisited,
                blockRenders = chunkData.blockRenders,
                squareList = chunkData.squareList,

                verticesList = chunkData.verticesList,
                trianglesList = chunkData.trianglesList,
                uvsList = chunkData.uvsList

            }.Schedule();

            // Add to the list //
            vcs.chunkJobList.Add(chunkData);

        }

    }


    private void generateMesh(ref SystemState state, VoxelChunkSingleton vcs, Entity entity, NativeList<float3> verticesList, NativeList<int> trianglesList, NativeList<float2> uvsList)
    {

        // Get a mesh from the mesh pool //
        Mesh mesh = MeshPoolManager.GetMesh();

        // Set the mesh //
        mesh.name = "Chunk";
        mesh.SetVertices(verticesList.AsArray());
        mesh.SetIndices(trianglesList.AsArray(), MeshTopology.Triangles, 0);
        mesh.SetUVs(0, uvsList.AsArray());
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);

        // Add the render components //
        EntitiesGraphicsSystem gfx = state.World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
        BatchMaterialID batchMatID = gfx.RegisterMaterial(VoxelWorld._Instance.Materials[0]);
        BatchMeshID batchMeshID = gfx.RegisterMesh(mesh);
        RenderMeshDescription desc = new RenderMeshDescription(shadowCastingMode: UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows: true);
        MaterialMeshInfo mmi = new MaterialMeshInfo { MeshID = batchMeshID, MaterialID = vcs.matID };
        RenderMeshUtility.AddComponents(entity, state.EntityManager, desc, mmi);

        // Set the bounds //
        int chunkSize = VoxelWorld._Instance.chunkSize;
        float3 center = new float3(chunkSize * 0.5f, chunkSize * 0.5f, chunkSize * 0.5f);
        float3 extents = new float3(chunkSize * 0.5f, chunkSize * 0.5f, chunkSize * 0.5f);
        AABB bounds = new AABB { Center = center, Extents = extents };
        state.EntityManager.SetComponentData(entity, new Unity.Rendering.RenderBounds { Value = bounds });

        // Send back the mesh to the mesh pool //
        // MeshPool.ReleaseMesh(mesh);

    }

}

[UpdateInGroup(typeof(ChunkPipelineGroup))]
[UpdateAfter(typeof(BuildMesh))]
public partial struct UpdateChunksVisibility : ISystem
{

    public bool init;
    public bool needNewJob;
    public byte rayCount;
    public int maxDistance;
    public int chunkSize;
    public JobHandle jobHandle;
    public NativeArray<RayCast> rayCasts;
    public NativeList<ChunkHit> chunksHit;

    public struct RayCast
    {
        public float3 origin;
        public float3 direction;
        public float distance;
    }

    public struct ChunkHit
    {
        public Entity chunk;
        public int3 position;
        public int3 hitNormal;
    }

    public void OnDestroy(ref SystemState state)
    {
        this.jobHandle.Complete();
        this.rayCasts.Dispose();
        this.chunksHit.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {

        // Init all values //
        if (this.init == false)
        {
            this.rayCount = 1;
            this.maxDistance = 1000;
            this.chunkSize = VoxelWorld._Instance.chunkSize;
            this.rayCasts = new NativeArray<RayCast>(rayCount, Allocator.Persistent);
            this.chunksHit = new NativeList<ChunkHit>(rayCount, Allocator.Persistent);
            this.init = true;
            this.needNewJob = true;
        }

        // Return if VoxexCast occlusion is disabled //
        if (VoxelWorld._Instance.doVoxelCastOcclusion == false)
            return;

        // Check the camera //
        if (Camera.main == null)
            return;

        // Check if the job is completed //
        if (this.jobHandle.IsCompleted == true)
        {
            this.jobHandle.Complete();
            foreach (ChunkHit chunkHit in this.chunksHit)
            {
                state.EntityManager.SetComponentEnabled<JustCreated>(chunkHit.chunk, true);
            }
            this.needNewJob = true;
            return;
        }

        // Check if a new job is needed //
        if (this.needNewJob == false)
            return;

        // Get random raycast //
        for (int i = 0; i < rayCount; i++)
        {

            Vector3 screenPoint = new Vector3(
                UnityEngine.Random.Range(0.0f, 1.0f),
                UnityEngine.Random.Range(0.0f, 1.0f),
                0f
            );

            Ray ray = Camera.main.ViewportPointToRay(screenPoint);

            RayCast rayCast = new RayCast();
            rayCast.origin = ray.origin;
            rayCast.direction = math.normalize((float3)ray.direction);
            rayCast.distance = maxDistance;
            this.rayCasts[i] = rayCast;

        }

        // Clear the chunks hit list //
        this.chunksHit.Clear();

        // Set the job //
        RaycastJobParallel job = new RaycastJobParallel
        {
            rayCasts = this.rayCasts,
            chunksHit = this.chunksHit.AsParallelWriter(),

            ChunkMap = VoxelWorld._Instance.ChunkSManager.chunksMap,
            BlocksLookup = SystemAPI.GetBufferLookup<BlockData>(true),
            ChunkSize = chunkSize,

        };

        // Start the job //
        this.jobHandle = job.Schedule(rayCount, 32);
        this.needNewJob = false;

    }

}