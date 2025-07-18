using Assets.Scripts.Block;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static BuildMesh;
using static EnumData;

public static class Utils
{
    static public int PosToIndex(int chunkSize, int x, int y, int z)
    {
        return x + (chunkSize * (y + (chunkSize * z)));
    }

    static public uint2 IDToAtlasIndex(BlocksID id)
    {
        switch (id)
        {
            case BlocksID.Grass: return new uint2(1, 0);
            case BlocksID.Dirt: return new uint2(2, 0);
            case BlocksID.Stone: return new uint2(3, 0);
            default: return new uint2(0, 0);
        }
    }

    static public void DestroyVoxelChunkSingleton(VoxelChunkSingleton vcs)
    {
        // Destroy the voxel chunk singleton //
        if (vcs.chunkToBuildQueue.IsCreated)
            vcs.chunkToBuildQueue.Dispose();
        if (vcs.chunkJobList.IsCreated)
        {
            for (int i = vcs.chunkJobList.Length - 1; i >= 0; i--)
            {
                ChunkData chunkData = vcs.chunkJobList[i];
                chunkData.job.Complete();
                DisposeVCSAllNatives(chunkData);
            }
            vcs.chunkJobList.Dispose();
        }
    }

    static public void DisposeVCSAllNatives(ChunkData chunkData)
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
