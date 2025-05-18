using Assets.Scripts.Block;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Atlas;

static public partial class ChunksGenerator
{

    public static Entity CreateChunk(ref SystemState state, int3 position, int chunkSize)
    {

        // Create the entity //
        EntityManager entityManager = state.EntityManager;
        Entity chunk = entityManager.CreateEntity();

        // Calculate the matrix //
        float3 worldPos = position * chunkSize;
        Matrix4x4 matrix = Matrix4x4.TRS(new float3(worldPos.x, worldPos.y, worldPos.z), quaternion.identity, new float3(1f, 1f, 1f));

        // Add all components //
        entityManager.AddComponentData(chunk, new ChunkMatrix { Matrix = matrix });
        entityManager.AddComponentData(chunk, new ChunkPosition { Value = new int3(position.x, position.y, position.z) });

        // Add all enableable Components //
        entityManager.AddComponent<JustCreated>(chunk);
        entityManager.AddComponent<CheckFloodFill>(chunk);
        entityManager.SetComponentEnabled<JustCreated>(chunk, true);
        entityManager.SetComponentEnabled<CheckFloodFill>(chunk, false);
        entityManager.SetComponentEnabled<CheckLinearFloodFil>(chunk, false);
        entityManager.SetComponentEnabled<NeedGenerateRenderBlocks>(chunk, false);
        entityManager.SetComponentEnabled<NeedCreateMesh>(chunk, false);

        // Create the buffers //
        entityManager.AddBuffer<BlockData>(chunk);
        entityManager.AddBuffer<BlockRender>(chunk);
        entityManager.AddBuffer<Frontier>(chunk);
        entityManager.AddBuffer<VisitFlags>(chunk);
        entityManager.AddBuffer<LinearVisitFlags>(chunk);
        entityManager.AddBuffer<SquaresList>(chunk);

        return chunk;

    }

    [BurstCompile]
    [WithAll(typeof(JustCreated))]
    public partial struct PreGenerateChunk : IJobEntity
    {

        [ReadOnly] public int chunkSize;

        public void Execute(DynamicBuffer<BlockData> blocks, EnabledRefRW<JustCreated> justCreated, EnabledRefRW<CheckFloodFill> checkFloodFill)
        {

            // Check the buffer //
            int total = chunkSize * chunkSize * chunkSize;
            if (blocks.Length < total)
                blocks.ResizeUninitialized(total);

            // Fill the chunk table with all blocks //
            for (int x = 0; x < this.chunkSize; x++)
            {
                for (int y = 0; y < this.chunkSize; y++)
                {
                    for (int z = 0; z < this.chunkSize; z++)
                    {
                        if ((x == 0 || y == 0 || z == 0))
                            blocks [Utils.PosToIndex(this.chunkSize, x, y, z)] = new BlockData((byte)1);
                    }
                }
            }

            // Set the chunk as generated and ask for flood fill //
            justCreated.ValueRW = false;
            checkFloodFill.ValueRW = true;

        }

    }

    [BurstCompile]
    [WithAll(typeof(CheckFloodFill))]
    public partial struct FloodFillJob : IJobEntity
    {

        [ReadOnly] public int chunkSize;

        public void Execute(DynamicBuffer<BlockData> blocks, DynamicBuffer<Frontier> frontier, DynamicBuffer<VisitFlags> floodVisited, EnabledRefRW<CheckFloodFill> checkFloodFill, EnabledRefRW<CheckLinearFloodFil> checkLinearFloodFil)
        {

            // Check the visited buffer //
            int total = chunkSize * chunkSize * chunkSize;
            if (floodVisited.Length < total)
                floodVisited.ResizeUninitialized(total);

            // Fill frontier and render tables //
            for (int x = 0; x < this.chunkSize; x++)
            {
                for (int y = 0; y < this.chunkSize; y++)
                {
                    for (int z = 0; z < this.chunkSize; z++)
                    {
                        if ((x == 0 || x == this.chunkSize - 1 || y == 0 || y == this.chunkSize - 1 || z == 0 || z == this.chunkSize - 1))
                        {
                            int idx = this.ToIndex(x, y, z);
                            floodVisited[idx] = new VisitFlags { value = 1 };
                            if (blocks[idx].IsRenderable() == false)
                                frontier.Add(new Frontier { pos = new int3(x, y, z) });
                        }
                    }
                }
            }


            // Itinerate the frontier blocks table //
            for (int i = 0; i < frontier.Length; i++)
            {

                // Get the block position //
                int3 pos = frontier[i].pos;

                // Check all direction //
                foreach (int3 dir in Directions)
                {

                    int3 np = pos + dir;
                    if (this.InBounds(np) == false) continue;

                    int idx = ToIndex(np.x, np.y, np.z);
                    if (floodVisited[idx].value == 1) continue;
                    if (blocks[idx].IsRenderable() == true) continue;

                    floodVisited[idx] = new VisitFlags { value = 1 };
                    frontier.Add(new Frontier { pos = np });

                }
            }

            // Clear the frontier buffer //
            frontier.Clear();

            // Launch the linear flood fill //
            checkFloodFill.ValueRW = false;
            checkLinearFloodFil.ValueRW = true;

        }

        private bool InBounds(int3 p)
        {
            return p.x >= 0 && p.y >= 0 && p.z >= 0 && p.x < chunkSize && p.y < chunkSize && p.z < chunkSize;
        }
            

        private int ToIndex(int x, int y, int z)
        {
            return x + chunkSize * (y + chunkSize * z);
        }

        static readonly int3[] Directions = new int3[]
        {
        new int3(1, 0, 0), new int3(-1, 0, 0),
        new int3(0, 1, 0), new int3(0, -1, 0),
        new int3(0, 0, 1), new int3(0, 0, -1)
        };
    }

    [BurstCompile]
    [WithAll(typeof(CheckLinearFloodFil))]
    public partial struct LinearFloodFillJob : IJobEntity
    {
        [ReadOnly] public int chunkSize;

        public void Execute(DynamicBuffer<BlockData> blocks, DynamicBuffer<LinearVisitFlags> floodVisited, EnabledRefRW<CheckLinearFloodFil> checkLinearFloodFil, EnabledRefRW<NeedGenerateRenderBlocks> needGenerateRenderBlocks)
        {

            // Check the linear visited buffer //
            int total = chunkSize * chunkSize * chunkSize;
            if (floodVisited.Length < total)
                floodVisited.ResizeUninitialized(total);

            // Check for all dirrections //
            for (int dirIndex = 0; dirIndex < 6; dirIndex++)
            {
                int3 dir = Directions[dirIndex];
                int3 orthA, orthB;
                GetOrthogonalAxes(dir, out orthA, out orthB);

                int3 faceOrigin = GetFaceStart(dir);

                for (int a = 0; a < chunkSize; a++)
                {
                    for (int b = 0; b < chunkSize; b++)
                    {
                        int3 start = faceOrigin + a * orthA + b * orthB;
                        int3 pos = start;

                        for (int i = 0; i < chunkSize; i++)
                        {
                            if (InBounds(pos) == false) break;

                            int idx = ToIndex(pos);
                            if (floodVisited[idx].value == 1) break;

                            floodVisited[idx] = new LinearVisitFlags { value = 1 };

                            if (blocks[idx].IsRenderable())
                                break;

                            pos += dir;
                        }
                    }
                }
            }

            // Launch the block renders generation //
            checkLinearFloodFil.ValueRW = false;
            needGenerateRenderBlocks.ValueRW = true;

        }

        private bool InBounds(int3 p)
        {
            return p.x >= 0 && p.y >= 0 && p.z >= 0 && p.x < chunkSize && p.y < chunkSize && p.z < chunkSize;
        }

        private int ToIndex(int3 p)
        {
            return p.x + chunkSize * (p.y + chunkSize * p.z);
        }

        private int3 GetFaceStart(int3 dir)
        {
            return new int3(
                dir.x < 0 ? chunkSize - 1 : 0,
                dir.y < 0 ? chunkSize - 1 : 0,
                dir.z < 0 ? chunkSize - 1 : 0
            );
        }

        private void GetOrthogonalAxes(int3 dir, out int3 axisA, out int3 axisB)
        {
            if (math.abs(dir.x) == 1)
            {
                axisA = new int3(0, 1, 0);
                axisB = new int3(0, 0, 1);
            }
            else if (math.abs(dir.y) == 1)
            {
                axisA = new int3(1, 0, 0);
                axisB = new int3(0, 0, 1);
            }
            else
            {
                axisA = new int3(1, 0, 0);
                axisB = new int3(0, 1, 0);
            }
        }

        private static readonly int3[] Directions = new int3[]
        {
        new int3(1, 0, 0),  // +X
        new int3(-1, 0, 0), // -X
        new int3(0, 1, 0),  // +Y
        new int3(0, -1, 0), // -Y
        new int3(0, 0, 1),  // +Z
        new int3(0, 0, -1)  // -Z
        };
    }

    [BurstCompile]
    [WithAll(typeof(NeedGenerateRenderBlocks))]
    public partial struct GenerateRenderBlocks : IJobEntity
    {

        [ReadOnly] public byte chunkSize;
        [ReadOnly] public int totalBlock;        
        [ReadOnly] public bool doFloodFill;
        [ReadOnly] public bool doLinearFloodFill;
        [ReadOnly] public bool doFacesOcclusion;
        [ReadOnly] public bool doGreedyMeshing;

        [ReadOnly] public NativeParallelHashMap<int3, Entity> chunkMap;
        [ReadOnly] public BufferLookup<BlockData> blocksLookup;

        public struct Data
        {

            public DynamicBuffer<BlockData> currentChunk;
            public DynamicBuffer<BlockData> leftNeighbor;
            public DynamicBuffer<BlockData> rightNeighbor;
            public DynamicBuffer<BlockData> bottomNeighbor;
            public DynamicBuffer<BlockData> topNeighbor;
            public DynamicBuffer<BlockData> backNeighbor;
            public DynamicBuffer<BlockData> frontNeighbor;

            public DynamicBuffer<VisitFlags> floodVisited;
            public DynamicBuffer<LinearVisitFlags> linearFloodVisited;

            public DynamicBuffer<BlockRender> blocksRender;

        }

        public void Execute(in ChunkPosition pos, DynamicBuffer<BlockData> blocks, DynamicBuffer<BlockRender> blockRenders, DynamicBuffer<VisitFlags> floodVisited, DynamicBuffer<LinearVisitFlags> linearFloodVisited, EnabledRefRW<NeedGenerateRenderBlocks> needGenerateRenderBlocks, EnabledRefRW<NeedCreateMesh> needCreateMesh)
        {

            // Create the Data //
            Data data = new Data();
            data.currentChunk = blocks;
            data.blocksRender = blockRenders;
            data.floodVisited = floodVisited;
            data.linearFloodVisited = linearFloodVisited;

            // Get all neighbors //
            Entity leftNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, pos.Value.x, pos.Value.y, pos.Value.z, EnumData.Direction.Left);
            if (leftNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(leftNeighborEntity)) data.leftNeighbor = blocksLookup[leftNeighborEntity];
            Entity rightNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, pos.Value.x, pos.Value.y, pos.Value.z, EnumData.Direction.Right);
            if (rightNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(rightNeighborEntity)) data.rightNeighbor = blocksLookup[rightNeighborEntity];
            Entity bottomNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, pos.Value.x, pos.Value.y, pos.Value.z, EnumData.Direction.Bottom);
            if (bottomNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(bottomNeighborEntity)) data.bottomNeighbor = blocksLookup[bottomNeighborEntity];
            Entity topNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, pos.Value.x, pos.Value.y, pos.Value.z, EnumData.Direction.Top);
            if (topNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(topNeighborEntity)) data.topNeighbor = blocksLookup[topNeighborEntity];
            Entity backNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, pos.Value.x, pos.Value.y, pos.Value.z, EnumData.Direction.Back);
            if (backNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(backNeighborEntity)) data.backNeighbor = blocksLookup[backNeighborEntity];
            Entity frontNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, pos.Value.x, pos.Value.y, pos.Value.z, EnumData.Direction.Front);
            if (frontNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(frontNeighborEntity)) data.frontNeighbor = blocksLookup[frontNeighborEntity];

            // Itinerate all blocks //
            for (int i = 0;  i < this.totalBlock; i++)
            {

                // Get the current block //
                BlockData blockData = data.currentChunk[i];
                BlockRender blockRender = data.blocksRender[i];

                // Set the block id to the render //
                blockRender.blockID = blockData.id;

                // Store the mask for this block if at least one face is visible //
                byte faceMask = this.checkAllFaces(i, data, ref blockData, ref blockRender);
                if (faceMask > 0)
                    blockRender.renderMask = faceMask;
                else
                    blockRender.renderMask = 0;

                // Save the block renderer //
                data.blocksRender[i] = blockRender;

            }

            // Launch the mesh generation //
            needGenerateRenderBlocks.ValueRW = false;
            needCreateMesh.ValueRW = true;

        }

        private byte checkAllFaces(int index, Data data, ref BlockData blockData, ref BlockRender blockRender)
        {

            // Get the position //
            int x = index % this.chunkSize;
            int y = (index / this.chunkSize) % this.chunkSize;
            int z = index / (this.chunkSize * this.chunkSize);

            // Create the mask //
            byte faceMask = blockRender.renderMask;

            // Get all index //
            int leftBockIndex = this.posToIndex(this.chunkSize, x-1, y, z);
            int rightBlockIndex = this.posToIndex(this.chunkSize, x+1, y, z);
            int bottomBlockIndex = this.posToIndex(this.chunkSize, x, y-1, z);
            int topBlockIndex = this.posToIndex(this.chunkSize, x, y+1, z);
            int backBlockIndex = this.posToIndex(this.chunkSize, x, y, z-1);
            int frontBlockIndex = this.posToIndex(this.chunkSize, x, y, z+1);

            // Get all blocks //
            BlockData leftBlock = this.getBlock(data, x - 1, y, z);
            BlockData rightBlock = this.getBlock(data, x + 1, y, z);
            BlockData bottomBlock = this.getBlock(data, x, y-1, z);
            BlockData topBlock = this.getBlock(data, x, y+1, z);
            BlockData backBlock = this.getBlock(data, x, y, z-1);
            BlockData frontBlock = this.getBlock(data, x, y, z+1);

            #region Left Face
            // ------------------------------------------ LEFT FACE ------------------------------------------ //

            // Check the previous face //
            if (this.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x, y - 1, z - 1);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = data.blocksRender[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 0)) != 0 && (neighborBottomRender.renderMask & (1 << 0)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.leftWSize == neighborBottomRender.leftWSize)
                        {
                            neighborFaceRender.leftHSize = (byte)(neighborBottomRender.leftHSize + 1);
                            neighborBottomRender.renderMask &= 0b11111110;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                            data.blocksRender[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || leftBlock.IsRenderable() == false) && isVisitedFace(data, leftBockIndex) == true)
            {
                faceMask |= 1 << 0;
                if (this.doGreedyMeshing == true)
                {
                    blockRender.leftWSize = 0;
                    blockRender.leftHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 0)) != 0 && neighborFaceRender.leftHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.leftWSize = (byte)(neighborFaceRender.leftWSize + 1);
                            neighborFaceRender.renderMask &= 0b11111110;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }
            }

            // End the line if the block has reached the end of the chunk //
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && leftBlock.IsRenderable() == false && z >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y - 1, z);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = data.blocksRender[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 0)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.leftWSize == bottomFaceRender.leftWSize)
                    {
                        blockRender.leftHSize = (byte)(bottomFaceRender.leftHSize + 1);
                        bottomFaceRender.renderMask &= 0b11111110;
                        data.blocksRender[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }

            #endregion

            #region Right Face
            // ------------------------------------------ RIGHT FACE ------------------------------------------ //

            // Check the previous face //
            if (this.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x, y - 1, z - 1);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = data.blocksRender[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 1)) != 0 && (neighborBottomRender.renderMask & (1 << 1)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.rightWSize == neighborBottomRender.rightWSize)
                        {
                            neighborFaceRender.rightHSize = (byte)(neighborBottomRender.rightHSize + 1);
                            neighborBottomRender.renderMask &= 0b11111101;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                            data.blocksRender[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || rightBlock.IsRenderable() == false) && isVisitedFace(data, rightBlockIndex) == true)
            {
                faceMask |= 1 << 1;

                if (this.doGreedyMeshing == true)
                {
                    blockRender.rightWSize = 0;
                    blockRender.rightHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 1)) != 0 && neighborFaceRender.rightHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.rightWSize = (byte)(neighborFaceRender.rightWSize + 1);
                            neighborFaceRender.renderMask &= 0b11111101;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }
                    
            }

            // End the line if the block has reached the end of the chunk //
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && rightBlock.IsRenderable() == false && z >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y - 1, z);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = data.blocksRender[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 1)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.rightWSize == bottomFaceRender.rightWSize)
                    {
                        blockRender.rightHSize = (byte)(bottomFaceRender.rightHSize + 1);
                        bottomFaceRender.renderMask &= 0b11111101;
                        data.blocksRender[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }

            #endregion

            #region Bottom Face
            // ------------------------------------------ BOTTOM FACE ------------------------------------------ //

            // Check the previous face //
            if (this.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x - 1, y, z - 1);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = data.blocksRender[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 2)) != 0 && (neighborBottomRender.renderMask & (1 << 2)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.bottomWSize == neighborBottomRender.bottomWSize)
                        {
                            neighborFaceRender.bottomHSize = (byte)(neighborBottomRender.bottomHSize + 1);
                            neighborBottomRender.renderMask &= 0b11111011;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                            data.blocksRender[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || bottomBlock.IsRenderable() == false) && isVisitedFace(data, bottomBlockIndex) == true)
            {
                faceMask |= 1 << 2;

                if (this.doGreedyMeshing == true)
                {
                    blockRender.bottomWSize = 0;
                    blockRender.bottomHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 2)) != 0 && neighborFaceRender.bottomHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.bottomWSize = (byte)(neighborFaceRender.bottomWSize + 1);
                            neighborFaceRender.renderMask &= 0b11111011;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }
                    
            }

            // End the line if the block has reached the end of the chunk //
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && bottomBlock.IsRenderable() == false && x >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = data.blocksRender[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 2)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.bottomWSize == bottomFaceRender.bottomWSize)
                    {
                        blockRender.bottomHSize = (byte)(bottomFaceRender.bottomHSize + 1);
                        bottomFaceRender.renderMask &= 0b11111011;
                        data.blocksRender[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }

            #endregion

            #region Top Face
            // ------------------------------------------ TOP FACE ------------------------------------------ //

            // Check the previous face //
            if (this.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x - 1, y, z - 1);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = data.blocksRender[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 3)) != 0 && (neighborBottomRender.renderMask & (1 << 3)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.topWSize == neighborBottomRender.topWSize)
                        {
                            neighborFaceRender.topHSize = (byte)(neighborBottomRender.topHSize + 1);
                            neighborBottomRender.renderMask &= 0b11110111;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                            data.blocksRender[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || topBlock.IsRenderable() == false) && isVisitedFace(data, topBlockIndex) == true)
            {
                faceMask |= 1 << 3;

                if (this.doGreedyMeshing == true)
                {
                    blockRender.topWSize = 0;
                    blockRender.topHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 3)) != 0 && neighborFaceRender.topHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.topWSize = (byte)(neighborFaceRender.topWSize + 1);
                            neighborFaceRender.renderMask &= 0b11110111;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }
                    
            }

            // End the line if the block has reached the end of the chunk //
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && topBlock.IsRenderable() == false && x >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = data.blocksRender[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 3)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.topWSize == bottomFaceRender.topWSize)
                    {
                        blockRender.topHSize = (byte)(bottomFaceRender.topHSize + 1);
                        bottomFaceRender.renderMask &= 0b11110111;
                        data.blocksRender[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }
            #endregion

            #region Back Face
            // ------------------------------------------ BACK FACE ------------------------------------------ //

            // Check the previous face //
            if (this.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x - 1, y - 1, z);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = data.blocksRender[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 4)) != 0 && (neighborBottomRender.renderMask & (1 << 4)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.backWSize == neighborBottomRender.backWSize)
                        {
                            neighborFaceRender.backHSize = (byte)(neighborBottomRender.backHSize + 1);
                            neighborBottomRender.renderMask &= 0b11101111;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                            data.blocksRender[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || backBlock.IsRenderable() == false) && isVisitedFace(data, backBlockIndex) == true)
            {
                faceMask |= 1 << 4;

                if (this.doGreedyMeshing == true)
                {
                    blockRender.backWSize = 0;
                    blockRender.backHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 4)) != 0 && neighborFaceRender.backHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.backWSize = (byte)(neighborFaceRender.backWSize + 1);
                            neighborFaceRender.renderMask &= 0b11101111;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }

            }

            // End the line if the block has reached the end of the chunk //
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && backBlock.IsRenderable() == false && x >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y - 1, z);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = data.blocksRender[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 4)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.backWSize == bottomFaceRender.backWSize)
                    {
                        blockRender.backHSize = (byte)(bottomFaceRender.backHSize + 1);
                        bottomFaceRender.renderMask &= 0b11101111;
                        data.blocksRender[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }

            #endregion

            #region Front Face
            // ------------------------------------------ FRONT FACE ------------------------------------------ //

            // Check the previous face //
            if (this.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x - 1, y - 1, z);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = data.blocksRender[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 5)) != 0 && (neighborBottomRender.renderMask & (1 << 5)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.frontWSize == neighborBottomRender.frontWSize)
                        {
                            neighborFaceRender.frontHSize = (byte)(neighborBottomRender.frontHSize + 1);
                            neighborBottomRender.renderMask &= 0b11011111;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                            data.blocksRender[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || frontBlock.IsRenderable() == false) && isVisitedFace(data, frontBlockIndex) == true)
            {
                faceMask |= 1 << 5;

                if (this.doGreedyMeshing == true)
                {
                    blockRender.frontWSize = 0;
                    blockRender.frontHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = data.blocksRender[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 5)) != 0 && neighborFaceRender.frontHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.frontWSize = (byte)(neighborFaceRender.frontWSize + 1);
                            neighborFaceRender.renderMask &= 0b11011111;
                            data.blocksRender[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }

            }

            // End the line if the block has reached the end of the chunk //
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && frontBlock.IsRenderable() == false && x >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y - 1, z);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = data.blocksRender[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 5)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.frontWSize == bottomFaceRender.frontWSize)
                    {
                        blockRender.frontHSize = (byte)(bottomFaceRender.frontHSize + 1);
                        bottomFaceRender.renderMask &= 0b11011111;
                        data.blocksRender[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }

            #endregion

            return faceMask;

        }

        private BlockData getBlock(Data data, int x, int y, int z)
        {

            // Check if this is a block from a neighbor chunk //
            if (x < 0)
            {
                if (data.leftNeighbor.Length != this.totalBlock) return BlockData.Air;
                return data.leftNeighbor[posToIndex(this.chunkSize, this.chunkSize - 1, y, z)];
            }
            if (x >= this.chunkSize)
            {
                if (data.rightNeighbor.Length != this.totalBlock) return BlockData.Air;
                return data.rightNeighbor[posToIndex(this.chunkSize, 0, y, z)];
            }
            if (y < 0)
            {
                if (data.bottomNeighbor.Length != this.totalBlock) return BlockData.Air;
                return data.bottomNeighbor[posToIndex(this.chunkSize, x, this.chunkSize - 1, z)];
            }
            if (y >= this.chunkSize)
            {
                if (data.topNeighbor.Length != this.totalBlock) return BlockData.Air;
                return data.topNeighbor[posToIndex(this.chunkSize, x, 0, z)];
            }
            if (z < 0)
            {
                if (data.backNeighbor.Length != this.totalBlock) return BlockData.Air;
                return data.backNeighbor[posToIndex(this.chunkSize, x, y, this.chunkSize - 1)];
            }
            if (z >= this.chunkSize)
            {
                if (data.frontNeighbor.Length != this.totalBlock) return BlockData.Air;
                return data.frontNeighbor[posToIndex(this.chunkSize, x, y, 0)];
            }

            // Check the current block //
            return data.currentChunk[posToIndex(this.chunkSize, x, y, z)];
        }

        private int getBlockRenderIndex(int x, int y, int z)
        {
            if (x < 0 || x >= this.chunkSize || y < 0 || y >= this.chunkSize || z < 0 || z >= this.chunkSize)
                return -1;
            else
                return posToIndex(this.chunkSize, x, y, z);
        }

        private int posToIndex(byte chunkSize, int x, int y, int z)
        {
            return x + chunkSize * (y + chunkSize * z);
        }

        private bool isVisitedFace(Data data, int index)
        {

            if (index < 0 || index >= this.totalBlock)
                return true;

            if (this.doFloodFill == true && this.doLinearFloodFill == true)
                return data.floodVisited[index].value == 1 && data.linearFloodVisited[index].value == 1;
            else if (this.doFloodFill == true && this.doLinearFloodFill == false)
                return data.floodVisited[index].value == 1;
            else if (this.doFloodFill == false && this.doLinearFloodFill == true)
                return data.linearFloodVisited[index].value == 1;

            return true;
        }

    }

    [BurstCompile]
    [WithAll(typeof(NeedCreateMesh))]
    public partial struct BuildSquareList : IJobEntity
    {

        [ReadOnly] public int chunkSize;
        [ReadOnly] public float3 chunkCenter;
        [ReadOnly] public float3 cameraPosition;
        [ReadOnly] public bool doFaceNormalCheck;

        public void Execute(in ChunkPosition pos, DynamicBuffer<BlockData> blocks, DynamicBuffer<BlockRender> blockRenders, DynamicBuffer<SquareFace> squareList, EnabledRefRW<NeedGenerateRenderBlocks> needGenerateRenderBlocks, EnabledRefRW<NeedCreateMesh> needCreateMesh)
        {

            for (int i = 0;  i < blockRenders.Length; i++)
            {

                // Get the current block render //
                BlockRender blockRender = blockRenders[i];

                // Get the position //
                int x = i % chunkSize;
                int y = (i / chunkSize) % chunkSize;
                int z = i / (chunkSize * chunkSize);

                // Generate quads for each visible face oriented toward the camera //
                if ((blockRender.renderMask & (1 << 0)) != 0 &&
                    this.IsFacingCamera(x, y - blockRender.leftHSize, z - blockRender.leftWSize, FaceDirection.Left))
                    squareList.Add(new SquareFace(x, y - blockRender.leftHSize, z - blockRender.leftWSize, blockRender.leftWSize, blockRender.leftHSize, FaceDirection.Left, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 1)) != 0 &&
                    this.IsFacingCamera(x, y - blockRender.rightHSize, z, FaceDirection.Right))
                    squareList.Add(new SquareFace(x, y - blockRender.rightHSize, z, blockRender.rightWSize, blockRender.rightHSize, FaceDirection.Right, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 2)) != 0 &&
                    this.IsFacingCamera(x - blockRender.bottomWSize, y, z - blockRender.bottomHSize, FaceDirection.Bottom))
                    squareList.Add(new SquareFace(x - blockRender.bottomWSize, y, z - blockRender.bottomHSize, blockRender.bottomWSize, blockRender.bottomHSize, FaceDirection.Bottom, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 3)) != 0 &&
                    this.IsFacingCamera(x - blockRender.topWSize, y, z, FaceDirection.Top))
                    squareList.Add(new SquareFace(x - blockRender.topWSize, y, z, blockRender.topWSize, blockRender.topHSize, FaceDirection.Top, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 4)) != 0 &&
                    this.IsFacingCamera(x, y - blockRender.backHSize, z, FaceDirection.Back))
                    squareList.Add(new SquareFace(x, y - blockRender.backHSize, z, blockRender.backWSize, blockRender.backHSize, FaceDirection.Back, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 5)) != 0 &&
                    this.IsFacingCamera(x - blockRender.frontWSize, y - blockRender.frontHSize, z, FaceDirection.Front))
                    squareList.Add(new SquareFace(x - blockRender.frontWSize, y - blockRender.frontHSize, z, blockRender.frontWSize, blockRender.frontHSize, FaceDirection.Front, blockRender.blockID));



            }

        }

        private bool IsFacingCamera(int x, int y, int z, FaceDirection dir)
        {
            if (this.doFaceNormalCheck == false) return true;
            float3 center = new float3(x + 0.5f, y + 0.5f, z + 0.5f);
            float3 toCam = math.normalize(this.cameraPosition - center);
            float3 normal = GetFaceNormal(dir);
            return math.dot(normal, toCam) > 0f;
        }

        private float3 GetFaceNormal(FaceDirection dir)
        {
            switch (dir)
            {
                case FaceDirection.Left: return new float3(-1, 0, 0);
                case FaceDirection.Right: return new float3(1, 0, 0);
                case FaceDirection.Bottom: return new float3(0, -1, 0);
                case FaceDirection.Top: return new float3(0, 1, 0);
                case FaceDirection.Back: return new float3(0, 0, -1);
                case FaceDirection.Front: return new float3(0, 0, 1);
                default: return float3.zero;
            }
        }

    }

    public enum FaceDirection
    {
        Left,
        Right,
        Top,
        Bottom,
        Front,
        Back
    }

    public struct SquareFace
    {

        public int x;
        public int y;
        public int z;
        public byte sizeW;
        public byte sizeH;
        public FaceDirection direction;
        public byte id;

        public SquareFace(int x, int y, int z, byte sizeW, byte sizeH, FaceDirection direction, byte id)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.sizeW = (byte)(sizeW + 1);
            this.sizeH = (byte)(sizeH + 1);
            this.direction = direction;
            this.id = id;
        }

        public void getSquare(ref NativeList<float3> squareList)
        {
            float3 p0, p1, p2, p3;
            switch (this.direction)
            {
                case FaceDirection.Left:
                    p0 = new float3(x, y, z);
                    p1 = new float3(x, y, z + this.sizeW);
                    p2 = new float3(x, y + this.sizeH, z + this.sizeW);
                    p3 = new float3(x, y + this.sizeH, z);
                    break;
                case FaceDirection.Right:
                    p0 = new float3(x + 1, y, z + 1);
                    p1 = new float3(x + 1, y, z + 1 - this.sizeW);
                    p2 = new float3(x + 1, y + this.sizeH, z + 1 - this.sizeW);
                    p3 = new float3(x + 1, y + this.sizeH, z + 1);
                    break;
                case FaceDirection.Bottom:
                    p0 = new float3(x, y, z);
                    p1 = new float3(x + this.sizeW, y, z);
                    p2 = new float3(x + this.sizeW, y, z + this.sizeH);
                    p3 = new float3(x, y, z + this.sizeH);
                    break;
                case FaceDirection.Top:
                    p0 = new float3(x, y + 1, z + 1);
                    p1 = new float3(x + this.sizeW, y + 1, z + 1);
                    p2 = new float3(x + this.sizeW, y + 1, z + 1 - this.sizeH);
                    p3 = new float3(x, y + 1, z + 1 - this.sizeH);
                    break;
                case FaceDirection.Back:
                    p0 = new float3(x + 1, y, z);
                    p1 = new float3(x - this.sizeW + 1, y, z);
                    p2 = new float3(x - this.sizeW + 1, y + this.sizeH, z);
                    p3 = new float3(x + 1, y + this.sizeH, z);
                    break;
                case FaceDirection.Front:
                    p0 = new float3(x, y, z + 1);
                    p1 = new float3(x + this.sizeW, y, z + 1);
                    p2 = new float3(x + this.sizeW, y + this.sizeH, z + 1);
                    p3 = new float3(x, y + this.sizeH, z + 1);
                    break;
                default:
                    return;
            }
            squareList.AddNoResize(p0);
            squareList.AddNoResize(p1);
            squareList.AddNoResize(p2);
            squareList.AddNoResize(p3);
        }


        public void getTriangles(int index, ref NativeList<int> trianglesNativeList)
        {

            trianglesNativeList.AddNoResize(index + 0);
            trianglesNativeList.AddNoResize(index + 1);
            trianglesNativeList.AddNoResize(index + 2);
            trianglesNativeList.AddNoResize(index + 0);
            trianglesNativeList.AddNoResize(index + 2);
            trianglesNativeList.AddNoResize(index + 3);
        }

        public void GetUVs(ref NativeList<float2> uvs, AtlasData atlasData)
        {
            float cellWidth = atlasData.CellWidthUV;
            float cellHeight = atlasData.CellHeightUV;

            uint2 atlasIndex = Utils.IDToAtlasIndex((EnumData.BlocksID)id);

            float uMin = atlasIndex.x * cellWidth; 
            float uMax = uMin + cellWidth;
            float vMin = atlasIndex.y * cellHeight;
            float vMax = vMin + cellHeight;

            uvs.AddNoResize(new float2(uMin, vMin)); // Bottom-left
            uvs.AddNoResize(new float2(uMax, vMin)); // Bottom-right
            uvs.AddNoResize(new float2(uMax, vMax)); // Top-right
            uvs.AddNoResize(new float2(uMin, vMax)); // Top-left
        }

    }

}
