using Assets.Scripts.Block;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.LightTransport;
using UnityEngine.UIElements;
using static ChunksGenerator;

public struct ChunkPosition : IComponentData {public int3 Value;}
public struct ChunkMatrix : IComponentData {public Matrix4x4 Matrix;}
public struct Frontier : IBufferElementData {public int3 pos;}
public struct VisitFlags : IBufferElementData {public byte value;}
public struct LinearVisitFlags : IBufferElementData {public byte value;}
public struct SquaresList : IBufferElementData {SquareFace value;}

public struct JustCreated : IComponentData, IEnableableComponent { }
public struct CheckFloodFill : IComponentData, IEnableableComponent { }
public struct CheckLinearFloodFil : IComponentData, IEnableableComponent { }
public struct NeedGenerateRenderBlocks : IComponentData, IEnableableComponent { }
public struct NeedCreateMesh : IComponentData, IEnableableComponent { }

public readonly partial struct ChunkAspect : IAspect
{
    public readonly Entity Self;
    public readonly RefRW<ChunkPosition> Position;
    public readonly RefRW<ChunkMatrix> Matrix;
    public readonly DynamicBuffer<BlockData> Blocks;
    public readonly DynamicBuffer<BlockRender> BlockRenders;
}


[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct GenerateChunks : ISystem
{
    public void OnUpdate(ref SystemState state)
    {

        // Get the chunk size //
        byte chunkSize = SystemAPI.GetSingleton<VoxelSettings>().chunkSize;

        // Start the job //
        state.Dependency = new PreGenerateChunk
        {
            chunkSize = chunkSize
        }.ScheduleParallel(state.Dependency);

    }

}

[UpdateAfter(typeof(GenerateChunks))]
public partial struct FloodFillCheck : ISystem
{
    public void OnUpdate(ref SystemState state)
    {

        // Get the settings //
        VoxelSettings settings = SystemAPI.GetSingleton<VoxelSettings>();

        // Check if the flood fill must be done //
        if (settings.doFloodFill == true)
        {
            // Start the job //
            state.Dependency = new PreGenerateChunk
            {
                chunkSize = settings.chunkSize
            }.ScheduleParallel(state.Dependency);
        }

        // Check if the linear flood fill must be done //
        if(settings.doLinearFloodFill == true)
        {
            // Start the job //
            state.Dependency = new LinearFloodFillJob
            {
                chunkSize = settings.chunkSize
            }.ScheduleParallel(state.Dependency);
        }

    }
}

[UpdateAfter(typeof(FloodFillCheck))]
public partial struct GenerateSquares : ISystem
{

    public void OnUpdate(ref SystemState state)
    {

        // Get the settings //
        VoxelSettings settings = SystemAPI.GetSingleton<VoxelSettings>();

        // Get the world //
        VoxelWorld world = VoxelWorld._Instance;

        // Get the buffer lookup //
        var blocksLookup = SystemAPI.GetBufferLookup<BlockData>(true);

        // Start the square generation //
        state.Dependency = new GenerateRenderBlocks
        {
            chunkSize = settings.chunkSize,
            totalBlock = settings.chunkSize * settings.chunkSize * settings.chunkSize,
            doFloodFill = settings.doFloodFill,
            doLinearFloodFill = settings.doLinearFloodFill,
            doFacesOcclusion = settings.doFacesOcclusion,
            doGreedyMeshing = settings.doGreedyMeshing,
            chunkMap = world.ChunkSManager.chunksMap,
            blocksLookup = blocksLookup

        }.ScheduleParallel(state.Dependency);


    }

}

[UpdateAfter(typeof(GenerateSquares))]
public partial struct BuildMesh : ISystem
{

    public void OnUpdate(ref SystemState state)
    {

        // Get the settings //
        VoxelSettings settings = SystemAPI.GetSingleton<VoxelSettings>();

        // Build the mesh //
        state.Dependency = new BuildSquareList
        {
            chunkSize = settings.chunkSize,
            chunkCenter = new float3(settings.chunkSize * 0.5f, settings.chunkSize * 0.5f, settings.chunkSize * 0.5f),
            cameraPosition = Camera.main.transform.position,
            doFaceNormalCheck = settings.doFaceNormalCheck,
        }.ScheduleParallel(state.Dependency);

    }

}