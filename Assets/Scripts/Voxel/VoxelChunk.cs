using Assets.Scripts.Block;
using System.Linq;
using System.Linq.Expressions;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.LightTransport;
using UnityEngine.Rendering;
using static BuildMesh;
using static ChunksGenerator;
using static Unity.Collections.AllocatorManager;
using static UnityEngine.Rendering.VirtualTexturing.Debugging;
using static VoxelRaycast;

public struct ChunkPosition : IComponentData {public int3 Value;}
public struct ChunkJustCreated : IComponentData, IEnableableComponent { }

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
    public NativeList<ChunkSquareFaces> squareFaces;

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
        ref VoxelChunkSingleton vcs = ref SystemAPI.GetSingletonRW<VoxelChunkSingleton>().ValueRW;

        // Check and get the voxel manager settings singleton //
        if (SystemAPI.HasSingleton<VoxelManagerSettings>() == false)
            return;
        VoxelManagerSettings vms = SystemAPI.GetSingleton<VoxelManagerSettings>();

        // Get all chunks that must be updated //
        foreach ((RefRO<ChunkJustCreated> _, Entity entity) in SystemAPI.Query<RefRO<ChunkJustCreated>>().WithEntityAccess())
        {
            vcs.chunkToBuildQueue.Enqueue(entity);
            state.EntityManager.SetComponentEnabled<ChunkJustCreated>(entity, false);
        }

        // Add jobs //
        int totalBlock = vms.chunkBlocksCount;
        int chunkSize = vms.chunkSize;
        while (vcs.chunkToBuildQueue.Count > 0 && vcs.chunkJobList.Length < vms.chunkInitListSize)
        {

            // Create the chunk data //
            ChunkData chunkData;

            // Get the entity //
            chunkData.chunk = vcs.chunkToBuildQueue.Dequeue();

            chunkData.frontier = NativesPool<int3>.GetList(totalBlock);
            chunkData.floodVisited = NativesPool<byte>.GetArray(totalBlock);
            chunkData.linearFloodVisited = NativesPool<byte>.GetArray(totalBlock);
            chunkData.blockRenders = NativesPool<BlockRender>.GetArray(totalBlock);
            chunkData.squareFaces = NativesPool<ChunkSquareFaces>.GetList(totalBlock*6);

            // Create the job //
            GenerateChunksGraphics jobStruct = new GenerateChunksGraphics
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
                squareFaces = chunkData.squareFaces

            };

            // Schedule the job //
            chunkData.job = jobStruct.Schedule();

            // Add to the list //
            vcs.chunkJobList.Add(chunkData);

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

            // Add all squares to the buffer //
            DynamicBuffer<ChunkSquareFaces> buffer = state.EntityManager.GetBuffer<ChunkSquareFaces>(chunkData.chunk);
            buffer.Clear();
            for (int j = 0; j < chunkData.squareFaces.Length; j++)
            {
                ChunkSquareFaces square = chunkData.squareFaces[j];
                buffer.Add(square);
            }

            // Get the region coord //
            int3 chunkPos = state.EntityManager.GetComponentData<ChunkPosition>(chunkData.chunk).Value;
            int3 regionCoord = Utils.ChunkPosToRegionCoord(chunkPos, vms.regionSize);

            // Set the region to render //
            if (VoxelWorld._ChunkManager.regionMap.TryGetValue(regionCoord, out Entity region) == true)
            {
                state.EntityManager.SetComponentEnabled<RegionToRender>(region, true);
            }

            // Dispose all natives //
            Utils.DisposeVCSAllNatives(chunkData);

            // Remove the chunkData //
            vcs.chunkJobList.RemoveAtSwapBack(i);

            //}

        }

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

    public void Test(ref SystemState state)
    {

        VoxelManagerSettings vms = SystemAPI.GetSingleton<VoxelManagerSettings>();

        // Init all values //
        if (this.init == false)
        {
            this.rayCount = vms.voxelRaysCount;
            this.maxDistance = vms.viewDistance;
            this.chunkSize = vms.chunkSize;
            this.rayCasts = new NativeArray<RayCast>(rayCount, Allocator.Persistent);
            this.chunksHit = new NativeList<ChunkHit>(rayCount, Allocator.Persistent);
            this.init = true;
            this.needNewJob = true;
        }

        // Check the camera //
        if (Camera.main == null)
            return;

        // Check if the job is completed //
        if (this.jobHandle.IsCompleted == true && this.needNewJob == false)
        {
            this.jobHandle.Complete();
            foreach (ChunkHit chunkHit in this.chunksHit)
            {
                state.EntityManager.SetComponentEnabled<ChunkJustCreated>(chunkHit.chunk, true);
            }
            this.needNewJob = true;
            return;
        }

        // Check if a new job is needed //
        if (this.needNewJob == false)
            return;

        // Get random raycast //
        for (int i = 0; i < this.rayCount; i++)
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
            rayCast.distance = this.maxDistance;
            this.rayCasts[i] = rayCast;

        }

        // Clear the chunks hit list //
        this.chunksHit.Clear();

        // Set the job //
        RaycastJobParallel job = new RaycastJobParallel
        {
            rayCasts = this.rayCasts,
            chunksHit = this.chunksHit.AsParallelWriter(),

            ChunkMap = VoxelWorld._ChunkManager.chunksMap,
            BlocksLookup = SystemAPI.GetBufferLookup<BlockData>(true),
            ChunkSize = this.chunkSize,

        };

        // Start the job //
        this.jobHandle = job.Schedule(rayCount, 32);
        this.needNewJob = false;

    }

}