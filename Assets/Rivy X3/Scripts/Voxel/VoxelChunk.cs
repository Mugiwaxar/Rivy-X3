using Assets.Scripts.Block;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static ChunksGenerator;
using static VoxelRaycast;

public struct ChunkPosition : IComponentData {public int3 Value;}
public struct ChunkNeedBlocks : IComponentData, IEnableableComponent { }
public struct ChunkNeedRender : IComponentData, IEnableableComponent { }



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

    }

    public void OnUpdate(ref SystemState state)
    {

        // Check and get the voxel chunk singleton //
        if (SystemAPI.HasSingleton<DataSingleton>() == false)
            return;
        ref DataSingleton DS = ref SystemAPI.GetSingletonRW<DataSingleton>().ValueRW;

        // Check and get the voxel manager settings singleton //
        if (SystemAPI.HasSingleton<WorldSettings>() == false)
            return;
        WorldSettings WS = SystemAPI.GetSingleton<WorldSettings>();

        // Get all chunks that must be updated //
        foreach ((RefRO<ChunkNeedRender> _, Entity entity) in SystemAPI.Query<RefRO<ChunkNeedRender>>().WithEntityAccess())
        {
            DS.chunkToBuildQueue.Enqueue(entity);
            state.EntityManager.SetComponentEnabled<ChunkNeedRender>(entity, false);
        }







        // Add jobs //
        int totalBlock = WS.chunkBlocksCount;
        int chunkSize = WS.chunkSize;
        int yChunkSize = WS.yChunkSize;
        while (DS.chunkToBuildQueue.Count > 0 && DS.chunkJobList.Length < WS.chunkGenerationMaxJob)
        {

            // Create the chunk data //
            ChunkData chunkData;

            // Get the entity //
            chunkData.chunk = DS.chunkToBuildQueue.Dequeue();

            // Check if the entity still exist //
            if (state.EntityManager.Exists(chunkData.chunk) == false)
                continue;

            chunkData.frontier = NativesPoolManager<int3>.GetList(totalBlock);
            chunkData.floodVisited = NativesPoolManager<byte>.GetArray(totalBlock);
            chunkData.linearFloodVisited = NativesPoolManager<byte>.GetArray(totalBlock);
            chunkData.blockRenders = NativesPoolManager<BlockRender>.GetArray(totalBlock);
            chunkData.squareFaces = NativesPoolManager<ChunkSquareFaces>.GetList(totalBlock*6);

            // Create the job //
            GenerateChunksGraphics jobStruct = new GenerateChunksGraphics
            {

                WS = WS,

                pos = state.EntityManager.GetComponentData<ChunkPosition>(chunkData.chunk).Value,
                chunkCenter = new float3(chunkSize * 0.5f, yChunkSize * 0.5f, chunkSize * 0.5f),
                cameraPosition = Camera.main.transform.position,
                chunkMap = DS.chunksMap,
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
            DS.chunkJobList.Add(chunkData);

        }

        // Check all jobs //
        for (int i = DS.chunkJobList.Length - 1; i >= 0; i--)
        {

            // Get the chunk data //
            ChunkData chunkData = DS.chunkJobList[i];

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
            int3 regionCoord = Utils.ChunkPosToRegionCoord(chunkPos, WS.regionSize);

            // Set the region to render //
            if (DS.regionsMap.TryGetValue(regionCoord, out Entity region) == true)
            {
                state.EntityManager.SetComponentEnabled<RegionDirty>(region, true);
            }

            // Dispose all natives //
            SingletonManager.DisposeVCSAllNatives(chunkData);

            // Remove the chunkData //
            DS.chunkJobList.RemoveAtSwapBack(i);

            //}

        }

    }

}


//[UpdateInGroup(typeof(ChunkPipelineGroup))]
//[UpdateAfter(typeof(BuildMesh))]
//public partial struct UpdateChunksVisibility : ISystem
//{

//    public bool init;
//    public bool needNewJob;
//    public byte rayCount;
//    public int maxDistance;
//    public int chunkSize;
//    public JobHandle jobHandle;
//    public NativeArray<RayCast> rayCasts;
//    public NativeList<ChunkHit> chunksHit;

//    public void OnDestroy(ref SystemState state)
//    {
//        this.jobHandle.Complete();
//        this.rayCasts.Dispose();
//        this.chunksHit.Dispose();
//    }

//    public void Test(ref SystemState state)
//    {

//        WorldSettings vms = SystemAPI.GetSingleton<WorldSettings>();

//         Init all values //
//        if (this.init == false)
//        {
//            this.rayCount = vms.voxelRaysCount;
//            this.maxDistance = vms.viewDistance;
//            this.chunkSize = vms.chunkSize;
//            this.rayCasts = new NativeArray<RayCast>(rayCount, Allocator.Persistent);
//            this.chunksHit = new NativeList<ChunkHit>(rayCount, Allocator.Persistent);
//            this.init = true;
//            this.needNewJob = true;
//        }

//         Check the camera //
//        if (Camera.main == null)
//            return;

//         Check if the job is completed //
//        if (this.jobHandle.IsCompleted == true && this.needNewJob == false)
//        {
//            this.jobHandle.Complete();
//            foreach (ChunkHit chunkHit in this.chunksHit)
//            {
//                state.EntityManager.SetComponentEnabled<ChunkJustCreated>(chunkHit.chunk, true);
//            }
//            this.needNewJob = true;
//            return;
//        }

//         Check if a new job is needed //
//        if (this.needNewJob == false)
//            return;

//         Get random raycast //
//        for (int i = 0; i < this.rayCount; i++)
//        {

//            Vector3 screenPoint = new Vector3(
//                UnityEngine.Random.Range(0.0f, 1.0f),
//                UnityEngine.Random.Range(0.0f, 1.0f),
//                0f
//            );

//            Ray ray = Camera.main.ViewportPointToRay(screenPoint);

//            RayCast rayCast = new RayCast();
//            rayCast.origin = ray.origin;
//            rayCast.direction = math.normalize((float3)ray.direction);
//            rayCast.distance = this.maxDistance;
//            this.rayCasts[i] = rayCast;

//        }

//         Clear the chunks hit list //
//        this.chunksHit.Clear();

//         Set the job //
//        RaycastJobParallel job = new RaycastJobParallel
//        {
//            rayCasts = this.rayCasts,
//            chunksHit = this.chunksHit.AsParallelWriter(),

//            ChunkMap = VoxelWorld._ChunkManager.chunksMap,
//            BlocksLookup = SystemAPI.GetBufferLookup<BlockData>(true),
//            ChunkSize = this.chunkSize,

//        };

//         Start the job //
//        this.jobHandle = job.Schedule(rayCount, 32);
//        this.needNewJob = false;

//    }

//}