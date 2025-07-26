using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

public static class VoxelRaycast
{
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

    [BurstCompile]
    public struct RaycastJobParallel : IJobParallelFor
    {

        [ReadOnly] public NativeArray<RayCast> rayCasts;
        public NativeList<ChunkHit>.ParallelWriter chunksHit;

        [ReadOnly] public NativeParallelHashMap<int3, Entity> ChunkMap;
        [ReadOnly] public BufferLookup<BlockData> BlocksLookup;
        [ReadOnly] public int ChunkSize;

        public void Execute(int index)
        {
            float3 origin = rayCasts[index].origin;
            float3 direction = rayCasts[index].direction;
            float maxDistance = rayCasts[index].distance;

            if (math.all(direction == float3.zero))
                return;

            int3 pos = (int3)math.floor(origin);
            int3 step = math.select(new int3(-1), new int3(1), direction >= 0f);

            float3 tMax, tDelta;

            tDelta.x = direction.x == 0f ? float.PositiveInfinity : math.abs(1f / direction.x);
            tDelta.y = direction.y == 0f ? float.PositiveInfinity : math.abs(1f / direction.y);
            tDelta.z = direction.z == 0f ? float.PositiveInfinity : math.abs(1f / direction.z);

            tMax.x = direction.x == 0f ? float.PositiveInfinity : IntBound(origin.x, direction.x);
            tMax.y = direction.y == 0f ? float.PositiveInfinity : IntBound(origin.y, direction.y);
            tMax.z = direction.z == 0f ? float.PositiveInfinity : IntBound(origin.z, direction.z);

            float travelled = 0f;
            int3 lastNormal = int3.zero;
            int3 lastChunkPos = int3.zero;
            Entity lastChunk = Entity.Null;
            int stepCount = 0;
            int maxStep = 10000;

            while (travelled <= maxDistance && stepCount < maxStep)
            {

                stepCount++;

                int3 chunkPos = new int3(
                    (int)math.floor((float)pos.x / ChunkSize),
                    (int)math.floor((float)pos.y / ChunkSize),
                    (int)math.floor((float)pos.z / ChunkSize)
                );

                Entity chunk = Entity.Null;

                if (math.all(chunkPos == lastChunkPos) && math.all(lastChunkPos != int3.zero))
                    chunk = lastChunk;
                else
                    ChunkMap.TryGetValue(chunkPos, out chunk);

                lastChunkPos = chunkPos;
                lastChunk = chunk;

                if (chunk != Entity.Null && IsSolid(pos, chunk))
                {
                    ChunkHit chunkHit = new ChunkHit();
                    chunkHit.chunk = chunk;
                    chunkHit.position = pos;
                    chunkHit.hitNormal = lastNormal;
                    this.chunksHit.AddNoResize(chunkHit);
                    return;
                }

                if (tMax.x < tMax.y)
                {
                    if (tMax.x < tMax.z)
                    {
                        pos.x += step.x;
                        travelled = tMax.x;
                        tMax.x += tDelta.x;
                        lastNormal = new int3(-step.x, 0, 0);
                    }
                    else
                    {
                        pos.z += step.z;
                        travelled = tMax.z;
                        tMax.z += tDelta.z;
                        lastNormal = new int3(0, 0, -step.z);
                    }
                }
                else
                {
                    if (tMax.y < tMax.z)
                    {
                        pos.y += step.y;
                        travelled = tMax.y;
                        tMax.y += tDelta.y;
                        lastNormal = new int3(0, -step.y, 0);
                    }
                    else
                    {
                        pos.z += step.z;
                        travelled = tMax.z;
                        tMax.z += tDelta.z;
                        lastNormal = new int3(0, 0, -step.z);
                    }
                }
            }

        }

        private bool IsSolid(int3 position, Entity chunk)
        {
            int3 localPos = ((position % ChunkSize) + ChunkSize) % ChunkSize;

            if (!BlocksLookup.HasBuffer(chunk))
                return false;

            DynamicBuffer<BlockData> buffer = BlocksLookup[chunk];
            int idx = Utils.PosToIndex(ChunkSize, localPos.x, localPos.y, localPos.z);
            if ((uint)idx >= (uint)buffer.Length)
                return false;

            return buffer[idx].IsRenderable();
        }

        private float IntBound(float s, float ds)
        {
            if (ds > 0f) return (math.ceil(s) - s) / ds;
            if (ds < 0f) return (s - math.floor(s)) / -ds;
            return float.PositiveInfinity;
        }
    }

}