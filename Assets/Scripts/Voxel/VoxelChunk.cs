using Assets.Scripts.Block;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using static BuildMesh;
using static ChunksGenerator;

public struct ChunkPosition : IComponentData {public int3 Value;}
public struct JustCreated : IComponentData, IEnableableComponent { }

[UpdateInGroup(typeof(ChunkPipelineGroup))]
[UpdateAfter(typeof(InitChunks))]
public partial struct BuildMesh : ISystem
{

    private NativeQueue<Entity> chunkToBuildQueue;
    private NativeList<ChunkData> chunkJobList;
    private bool initDone;
    BatchMaterialID matID;

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

    public void OnDestroy(ref SystemState state)
    {
        if (this.chunkToBuildQueue.IsCreated)
            this.chunkToBuildQueue.Dispose();
        if (this.chunkJobList.IsCreated)
        {
            for (int i = this.chunkJobList.Length - 1; i >= 0; i--)
            {
                ChunkData chunkData = this.chunkJobList[i];
                chunkData.job.Complete();
                this.disposeAllNatives(chunkData);
            }
            this.chunkJobList.Dispose();
        }
    }

    public void OnUpdate(ref SystemState state)
    {

        // Do the init //
        if (this.initDone == false)
        {
            // Create the queue and the list //
            this.chunkToBuildQueue = new NativeQueue<Entity>(Allocator.Persistent);
            this.chunkJobList = new NativeList<ChunkData>(Allocator.Persistent);
            // Set all chunks matérial //
            EntitiesGraphicsSystem gfxSys = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            this.matID = gfxSys.RegisterMaterial(VoxelWorld._Instance.Materials[0]);
            // Set the initialization as done //
            this.initDone = true;
        }

        // Get all chunks that must be updated //
        foreach ((RefRO<JustCreated> _, Entity entity) in SystemAPI.Query<RefRO<JustCreated>>().WithEntityAccess())
        {
            this.chunkToBuildQueue.Enqueue(entity);
            state.EntityManager.SetComponentEnabled<JustCreated>(entity, false);
        }

        // Check all jobs //
        for (int i = this.chunkJobList.Length - 1; i >= 0; i--)
        {
            ChunkData chunkData = this.chunkJobList[i];
            if (chunkData.job.IsCompleted == true)
            {

                // Complete the job //
                chunkData.job.Complete();

                // Build the mesh //
                this.generateMesh(ref state, chunkData.chunk, chunkData.verticesList, chunkData.trianglesList, chunkData.uvsList);

                // Dispose all natives //
                this.disposeAllNatives(chunkData);

                // Remove the chunkData //
                this.chunkJobList.RemoveAtSwapBack(i);

            }
        }

        // Get the parameters //
        VoxelWorld world = VoxelWorld._Instance;
        int chunkSize = world.chunkSize;
        int totalBlock = chunkSize * chunkSize * chunkSize;

        // Add job //
        while (this.chunkToBuildQueue.Count > 0 && this.chunkJobList.Length <= VoxelWorld._Instance.chunkInitListSize)
        {

            // Create the chunk data //
            ChunkData chunkData;

            // Get the entity //
            chunkData.chunk = this.chunkToBuildQueue.Dequeue();

            chunkData.frontier = NativesPool<int3>.GetList(totalBlock);
            chunkData.floodVisited = NativesPool<byte>.GetArray(totalBlock);
            chunkData.linearFloodVisited = NativesPool<byte>.GetArray(totalBlock);
            chunkData.blockRenders = NativesPool<BlockRender>.GetArray(totalBlock);
            chunkData.squareList = NativesPool<SquareFace>.GetList(totalBlock*3);

            chunkData.verticesList = NativesPool<float3>.GetList(totalBlock*6*4);
            chunkData.trianglesList = NativesPool<int>.GetList(totalBlock*6*6);
            chunkData.uvsList = NativesPool<float2>.GetList(totalBlock*6*4);

            // Create the job //
            chunkData.job = new GenerateChunksGraphics
            {

                chunkSize = chunkSize,
                totalBlocks = totalBlock,
                doFloodFill = world.doFloodFill,
                doLinearFloodFill = world.doLinearFloodFill,
                doFacesOcclusion = world.doFacesOcclusion,
                doGreedyMeshing = world.doGreedyMeshing,
                doFaceNormalCheck = world.doFaceNormalCheck,

                pos = state.EntityManager.GetComponentData<ChunkPosition>(chunkData.chunk).Value,
                chunkCenter = new float3(chunkSize * 0.5f, chunkSize * 0.5f, chunkSize * 0.5f),
                cameraPosition = Camera.main.transform.position,
                chunkMap = world.ChunkSManager.chunksMap,
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
            this.chunkJobList.Add(chunkData);

        }

    }


    private void generateMesh(ref SystemState state, Entity entity, NativeList<float3> verticesList, NativeList<int> trianglesList, NativeList<float2> uvsList)
    {

        // Get a mesh from the mesh pool //
        Mesh mesh = MeshPool.GetMesh();

        // Set the mesh //
        //Mesh mesh = new Mesh { name = "Chunk", indexFormat = IndexFormat.UInt32 };
        mesh.name = "Chunk";
        mesh.SetVertices(verticesList.AsArray());
        mesh.SetIndices(trianglesList.AsArray(), MeshTopology.Triangles, 0);
        mesh.SetUVs(0, uvsList.AsArray());
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.UploadMeshData(false);

        // Set the mesh to render //
        EntitiesGraphicsSystem gfxSys = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<EntitiesGraphicsSystem>();
        BatchMeshID meshID = gfxSys.RegisterMesh(mesh);
        var mmi = new MaterialMeshInfo { MeshID = meshID, MaterialID = this.matID };
        state.EntityManager.SetComponentData(entity, mmi);

        // Send back the mesh to the mesh pool //
        // MeshPool.ReleaseMesh(mesh);

    }

    private void disposeAllNatives(ChunkData chunkData)
    {

        // Dispose all tables //
        NativesPool<int3>.ReleaseList(chunkData.frontier);
        NativesPool<byte>.ReleaseArray(chunkData.floodVisited);
        NativesPool<byte>.ReleaseArray(chunkData.linearFloodVisited);
        NativesPool<BlockRender>.ReleaseArray(chunkData.blockRenders);
        NativesPool<SquareFace>.ReleaseList(chunkData.squareList);

        NativesPool<float3>.ReleaseList(chunkData.verticesList);
        NativesPool<int>.ReleaseList(chunkData.trianglesList);
        NativesPool<float2>.ReleaseList(chunkData.uvsList);

    }

}