using Assets.Scripts.Block;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;
using static Atlas;

static public partial class ChunksGenerator
{
    public static bool CreateChunk(ref SystemState state, int3 position, Entity chunk, int chunkSize, bool removeFullAirChunk)
    {

        //// Create the entity //
        //Entity chunk = ecb.CreateEntity();

        //// Add the position components //
        //ecb.AddComponent(chunk, new ChunkPosition { Value = new int3(position.x, position.y, position.z) });

        //// Add the local transform //
        //float3 worldPos = position * chunkSize;
        //ecb.AddComponent(chunk, LocalTransform.FromPosition(worldPos));

        //// Add all enableable Components //
        //ecb.AddComponent<ChunkNeedBlocks>(chunk);
        //ecb.AddComponent<ChunkNeedRender>(chunk);
        //ecb.SetComponentEnabled<ChunkNeedRender>(chunk, false);

        //// Create the buffers //
        //ecb.AddBuffer<BlockData>(chunk);
        //ecb.AddBuffer<ChunkSquareFaces>(chunk);

        // Get the entity manager //
        EntityManager entityManager = state.EntityManager;

        // Get the buffers //
        DynamicBuffer<BlockData> blocks = entityManager.GetBuffer<BlockData>(chunk);
        DynamicBuffer<ChunkSquareFaces> squares = entityManager.GetBuffer<ChunkSquareFaces>(chunk);

        // Check the buffers length //
        int total = chunkSize * chunkSize * chunkSize;
        if (blocks.Length < total) blocks.ResizeUninitialized(total);
        if (squares.Length < total) squares.ResizeUninitialized(total);
        squares.Clear();

        // Full air chunk, don't create //
        bool fullAir = true;

        // Fill the chunk table with all blocks //
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    int yRealPos = position.y * chunkSize + y;
                    if (yRealPos > 20)
                    {
                        blocks[Utils.PosToIndex(chunkSize, x, y, z)] = new BlockData((byte)0, true);
                    }
                    else
                    {
                        blocks[Utils.PosToIndex(chunkSize, x, y, z)] = new BlockData((byte)1);
                        fullAir = false;
                    }
                }
            }
        }

        // Check if full air //
        if (fullAir == true && removeFullAirChunk == true)
            return false;

        // Return //
        return true;

    }

    [BurstCompile]
    public partial struct GenerateChunksGraphics : IJob
    {

        [ReadOnly] public WorldSettings vms;

        [ReadOnly] public int3 pos;
        [ReadOnly] public float3 chunkCenter;
        [ReadOnly] public float3 cameraPosition;
        [ReadOnly] public NativeParallelHashMap<int3, Entity> chunkMap;
        [ReadOnly] public BufferLookup<BlockData> blocksLookup;
        [ReadOnly] public AtlasData atlas;

        int chunkSize;
        int totalBlocks;

        public NativeList<int3> frontier;
        public NativeArray<byte> floodVisited;
        public NativeArray<byte> linearFloodVisited;
        public NativeArray<BlockRender> blockRenders;
        public NativeList<ChunkSquareFaces> squareFaces;

        private DynamicBuffer<BlockData> currentChunk;
        private DynamicBuffer<BlockData> leftNeighbor;
        private DynamicBuffer<BlockData> rightNeighbor;
        private DynamicBuffer<BlockData> bottomNeighbor;
        private DynamicBuffer<BlockData> topNeighbor;
        private DynamicBuffer<BlockData> backNeighbor;
        private DynamicBuffer<BlockData> frontNeighbor;


        public void Execute()
        {

            // Get current chunk and ensure it has a valid buffer //
            Entity chunkEntity = ChunksManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.None);
            if (chunkEntity == Entity.Null || this.blocksLookup.HasBuffer(chunkEntity) == false)
                return;
            this.currentChunk = this.blocksLookup[chunkEntity];

            // Get the settings //
            this.chunkSize = vms.chunkSize;
            this.totalBlocks = vms.chunkBlocksCount;

            // Get all neighbors //
            Entity leftNeighborEntity = ChunksManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Left);
            if (leftNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(leftNeighborEntity)) this.leftNeighbor = this.blocksLookup[leftNeighborEntity];
            Entity rightNeighborEntity = ChunksManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Right);
            if (rightNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(rightNeighborEntity)) this.rightNeighbor = this.blocksLookup[rightNeighborEntity];
            Entity bottomNeighborEntity = ChunksManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Bottom);
            if (bottomNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(bottomNeighborEntity)) this.bottomNeighbor = this.blocksLookup[bottomNeighborEntity];
            Entity topNeighborEntity = ChunksManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Top);
            if (topNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(topNeighborEntity)) this.topNeighbor = this.blocksLookup[topNeighborEntity];
            Entity backNeighborEntity = ChunksManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Back);
            if (backNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(backNeighborEntity)) this.backNeighbor = this.blocksLookup[backNeighborEntity];
            Entity frontNeighborEntity = ChunksManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Front);
            if (frontNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(frontNeighborEntity)) this.frontNeighbor = this.blocksLookup[frontNeighborEntity];


            // Do the flood fill //
            if (vms.doFloodFill == true)
                this.executeFloodFill();

            // Do the linear flood fill //
            if (vms.doLinearFloodFill == true)
                this.executeLinearFloodFill();

            // Generate the render blocks //
            this.generateRenderBlocks();

            // Build the squares list //
            this.buildSquareList();

        }

        private void executeFloodFill()
        {

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
                            this.floodVisited[idx] = 1;
                            if (this.currentChunk[idx].IsRenderable() == false)
                                this.frontier.Add(new int3(x, y, z));
                        }
                    }
                }
            }


            // Itinerate the frontier blocks table //
            for (int i = 0; i < this.frontier.Length; i++)
            {

                // Get the block position //
                int3 pos = this.frontier[i];

                // Check all direction //
                foreach (int3 dir in Directions)
                {

                    int3 np = pos + dir;
                    if (this.InBounds(np) == false) continue;

                    int idx = this.ToIndex(np.x, np.y, np.z);
                    if (this.floodVisited[idx] == 1) continue;
                    if (this.currentChunk[idx].IsRenderable() == true) continue;

                    floodVisited[idx] = 1;
                    frontier.Add(np);

                }

            }

        }

        private void executeLinearFloodFill()
        {

            // Check for all directions //
            for (int dirIndex = 0; dirIndex < 6; dirIndex++)
            {
                int3 dir = Directions[dirIndex];
                int3 orthA, orthB;
                this.GetOrthogonalAxes(dir, out orthA, out orthB);

                int3 faceOrigin = this.GetFaceStart(dir);

                for (int a = 0; a < this.chunkSize; a++)
                {
                    for (int b = 0; b < this.chunkSize; b++)
                    {
                        int3 start = faceOrigin + a * orthA + b * orthB;
                        int3 pos = start;

                        for (int i = 0; i < this.chunkSize; i++)
                        {
                            if (this.InBounds(pos) == false) break;

                            int idx = this.ToIndex(pos.x, pos.y, pos.z);
                            if (this.linearFloodVisited[idx] == 1) break;

                            this.linearFloodVisited[idx] = 1;

                            if (this.currentChunk[idx].IsRenderable())
                                break;

                            pos += dir;
                        }
                    }
                }

            }

        }

        private void generateRenderBlocks()
        {

            // Itinerate all blocks //
            for (int i = 0; i < this.totalBlocks; i++)
            {

                // Get the current block //
                BlockData blockData = this.currentChunk[i];
                BlockRender blockRender = default;

                // Set the block id to the render //
                blockRender.blockID = blockData.id;

                // Store the mask for this block if at least one face is visible //
                byte faceMask = this.checkAllFaces(i, ref blockData, ref blockRender);
                if (faceMask > 0)
                    blockRender.renderMask = faceMask;
                else
                    blockRender.renderMask = 0;

                // Save the block renderer //
                this.blockRenders[i] = blockRender;

            }

        }

        private void buildSquareList()
        {

            for (int i = 0; i < this.blockRenders.Length; i++)
            {

                // Get the current block render //
                BlockRender blockRender = this.blockRenders[i];

                // Get the position //
                int x = i % this.chunkSize;
                int y = (i / this.chunkSize) % this.chunkSize;
                int z = i / (this.chunkSize * this.chunkSize);

                // Generate quads for each visible face oriented toward the camera //
                if ((blockRender.renderMask & (1 << 0)) != 0 &&
                    this.IsFacingCamera(x, y - blockRender.leftHSize, z - blockRender.leftWSize, FaceDirection.Left))
                    this.squareFaces.AddNoResize(new ChunkSquareFaces(x, y - blockRender.leftHSize, z - blockRender.leftWSize, blockRender.leftWSize, blockRender.leftHSize, FaceDirection.Left, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 1)) != 0 &&
                    this.IsFacingCamera(x, y - blockRender.rightHSize, z, FaceDirection.Right))
                    this.squareFaces.AddNoResize(new ChunkSquareFaces(x, y - blockRender.rightHSize, z, blockRender.rightWSize, blockRender.rightHSize, FaceDirection.Right, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 2)) != 0 &&
                    this.IsFacingCamera(x - blockRender.bottomWSize, y, z - blockRender.bottomHSize, FaceDirection.Bottom))
                    this.squareFaces.AddNoResize(new ChunkSquareFaces(x - blockRender.bottomWSize, y, z - blockRender.bottomHSize, blockRender.bottomWSize, blockRender.bottomHSize, FaceDirection.Bottom, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 3)) != 0 &&
                    this.IsFacingCamera(x - blockRender.topWSize, y, z, FaceDirection.Top))
                    this.squareFaces.AddNoResize(new ChunkSquareFaces(x - blockRender.topWSize, y, z, blockRender.topWSize, blockRender.topHSize, FaceDirection.Top, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 4)) != 0 &&
                    this.IsFacingCamera(x, y - blockRender.backHSize, z, FaceDirection.Back))
                    this.squareFaces.AddNoResize(new ChunkSquareFaces(x, y - blockRender.backHSize, z, blockRender.backWSize, blockRender.backHSize, FaceDirection.Back, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 5)) != 0 &&
                    this.IsFacingCamera(x - blockRender.frontWSize, y - blockRender.frontHSize, z, FaceDirection.Front))
                    this.squareFaces.AddNoResize(new ChunkSquareFaces(x - blockRender.frontWSize, y - blockRender.frontHSize, z, blockRender.frontWSize, blockRender.frontHSize, FaceDirection.Front, blockRender.blockID));

            }

        }

        private byte checkAllFaces(int index, ref BlockData blockData, ref BlockRender blockRender)
        {

            // Get the position //
            int x = index % this.chunkSize;
            int y = (index / this.chunkSize) % this.chunkSize;
            int z = index / (this.chunkSize * this.chunkSize);

            // Create the mask //
            byte faceMask = blockRender.renderMask;

            // Get all index //
            int leftBockIndex = this.ToIndex(x - 1, y, z);
            int rightBlockIndex = this.ToIndex(x + 1, y, z);
            int bottomBlockIndex = this.ToIndex(x, y - 1, z);
            int topBlockIndex = this.ToIndex(x, y + 1, z);
            int backBlockIndex = this.ToIndex(x, y, z - 1);
            int frontBlockIndex = this.ToIndex(x, y, z + 1);

            // Get all blocks //
            BlockData leftBlock = this.getBlock(x - 1, y, z);
            BlockData rightBlock = this.getBlock(x + 1, y, z);
            BlockData bottomBlock = this.getBlock(x, y - 1, z);
            BlockData topBlock = this.getBlock(x, y + 1, z);
            BlockData backBlock = this.getBlock(x, y, z - 1);
            BlockData frontBlock = this.getBlock(x, y, z + 1);

            #region Left Face
            // ------------------------------------------ LEFT FACE ------------------------------------------ //

            // Check the previous face //
            if (vms.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x, y - 1, z - 1);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = this.blockRenders[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 0)) != 0 && (neighborBottomRender.renderMask & (1 << 0)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.leftWSize == neighborBottomRender.leftWSize)
                        {
                            neighborFaceRender.leftHSize = (byte)(neighborBottomRender.leftHSize + 1);
                            neighborBottomRender.renderMask &= 0b11111110;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                            this.blockRenders[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (vms.doFacesOcclusion == false || leftBlock.IsRenderable() == false) && isVisitedFace(leftBockIndex) == true)
            {
                faceMask |= 1 << 0;
                if (vms.doGreedyMeshing == true)
                {
                    blockRender.leftWSize = 0;
                    blockRender.leftHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 0)) != 0 && neighborFaceRender.leftHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.leftWSize = (byte)(neighborFaceRender.leftWSize + 1);
                            neighborFaceRender.renderMask &= 0b11111110;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }
            }

            // End the line if the block has reached the end of the chunk //
            if (vms.doGreedyMeshing == true && blockData.IsRenderable() == true && leftBlock.IsRenderable() == false && z >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y - 1, z);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = this.blockRenders[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 0)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.leftWSize == bottomFaceRender.leftWSize)
                    {
                        blockRender.leftHSize = (byte)(bottomFaceRender.leftHSize + 1);
                        bottomFaceRender.renderMask &= 0b11111110;
                        this.blockRenders[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }

            #endregion

            #region Right Face
            // ------------------------------------------ RIGHT FACE ------------------------------------------ //

            // Check the previous face //
            if (vms.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x, y - 1, z - 1);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = this.blockRenders[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 1)) != 0 && (neighborBottomRender.renderMask & (1 << 1)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.rightWSize == neighborBottomRender.rightWSize)
                        {
                            neighborFaceRender.rightHSize = (byte)(neighborBottomRender.rightHSize + 1);
                            neighborBottomRender.renderMask &= 0b11111101;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                            this.blockRenders[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (vms.doFacesOcclusion == false || rightBlock.IsRenderable() == false) && isVisitedFace(rightBlockIndex) == true)
            {
                faceMask |= 1 << 1;

                if (vms.doGreedyMeshing == true)
                {
                    blockRender.rightWSize = 0;
                    blockRender.rightHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 1)) != 0 && neighborFaceRender.rightHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.rightWSize = (byte)(neighborFaceRender.rightWSize + 1);
                            neighborFaceRender.renderMask &= 0b11111101;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }

            }

            // End the line if the block has reached the end of the chunk //
            if (vms.doGreedyMeshing == true && blockData.IsRenderable() == true && rightBlock.IsRenderable() == false && z >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y - 1, z);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = this.blockRenders[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 1)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.rightWSize == bottomFaceRender.rightWSize)
                    {
                        blockRender.rightHSize = (byte)(bottomFaceRender.rightHSize + 1);
                        bottomFaceRender.renderMask &= 0b11111101;
                        this.blockRenders[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }

            #endregion

            #region Bottom Face
            // ------------------------------------------ BOTTOM FACE ------------------------------------------ //

            // Check the previous face //
            if (vms.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x - 1, y, z - 1);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = this.blockRenders[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 2)) != 0 && (neighborBottomRender.renderMask & (1 << 2)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.bottomWSize == neighborBottomRender.bottomWSize)
                        {
                            neighborFaceRender.bottomHSize = (byte)(neighborBottomRender.bottomHSize + 1);
                            neighborBottomRender.renderMask &= 0b11111011;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                            this.blockRenders[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (vms.doFacesOcclusion == false || bottomBlock.IsRenderable() == false) && isVisitedFace(bottomBlockIndex) == true)
            {
                faceMask |= 1 << 2;

                if (vms.doGreedyMeshing == true)
                {
                    blockRender.bottomWSize = 0;
                    blockRender.bottomHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 2)) != 0 && neighborFaceRender.bottomHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.bottomWSize = (byte)(neighborFaceRender.bottomWSize + 1);
                            neighborFaceRender.renderMask &= 0b11111011;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }

            }

            // End the line if the block has reached the end of the chunk //
            if (vms.doGreedyMeshing == true && blockData.IsRenderable() == true && bottomBlock.IsRenderable() == false && x >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = this.blockRenders[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 2)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.bottomWSize == bottomFaceRender.bottomWSize)
                    {
                        blockRender.bottomHSize = (byte)(bottomFaceRender.bottomHSize + 1);
                        bottomFaceRender.renderMask &= 0b11111011;
                        this.blockRenders[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }

            #endregion

            #region Top Face
            // ------------------------------------------ TOP FACE ------------------------------------------ //

            // Check the previous face //
            if (vms.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x - 1, y, z - 1);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = this.blockRenders[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 3)) != 0 && (neighborBottomRender.renderMask & (1 << 3)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.topWSize == neighborBottomRender.topWSize)
                        {
                            neighborFaceRender.topHSize = (byte)(neighborBottomRender.topHSize + 1);
                            neighborBottomRender.renderMask &= 0b11110111;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                            this.blockRenders[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (vms.doFacesOcclusion == false || topBlock.IsRenderable() == false) && isVisitedFace(topBlockIndex) == true)
            {
                faceMask |= 1 << 3;

                if (vms.doGreedyMeshing == true)
                {
                    blockRender.topWSize = 0;
                    blockRender.topHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 3)) != 0 && neighborFaceRender.topHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.topWSize = (byte)(neighborFaceRender.topWSize + 1);
                            neighborFaceRender.renderMask &= 0b11110111;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }

            }

            // End the line if the block has reached the end of the chunk //
            if (vms.doGreedyMeshing == true && blockData.IsRenderable() == true && topBlock.IsRenderable() == false && x >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y, z - 1);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = this.blockRenders[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 3)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.topWSize == bottomFaceRender.topWSize)
                    {
                        blockRender.topHSize = (byte)(bottomFaceRender.topHSize + 1);
                        bottomFaceRender.renderMask &= 0b11110111;
                        this.blockRenders[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }
            #endregion

            #region Back Face
            // ------------------------------------------ BACK FACE ------------------------------------------ //

            // Check the previous face //
            if (vms.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x - 1, y - 1, z);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = this.blockRenders[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 4)) != 0 && (neighborBottomRender.renderMask & (1 << 4)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.backWSize == neighborBottomRender.backWSize)
                        {
                            neighborFaceRender.backHSize = (byte)(neighborBottomRender.backHSize + 1);
                            neighborBottomRender.renderMask &= 0b11101111;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                            this.blockRenders[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (vms.doFacesOcclusion == false || backBlock.IsRenderable() == false) && isVisitedFace(backBlockIndex) == true)
            {
                faceMask |= 1 << 4;

                if (vms.doGreedyMeshing == true)
                {
                    blockRender.backWSize = 0;
                    blockRender.backHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 4)) != 0 && neighborFaceRender.backHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.backWSize = (byte)(neighborFaceRender.backWSize + 1);
                            neighborFaceRender.renderMask &= 0b11101111;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }

            }

            // End the line if the block has reached the end of the chunk //
            if (vms.doGreedyMeshing == true && blockData.IsRenderable() == true && backBlock.IsRenderable() == false && x >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y - 1, z);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = this.blockRenders[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 4)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.backWSize == bottomFaceRender.backWSize)
                    {
                        blockRender.backHSize = (byte)(bottomFaceRender.backHSize + 1);
                        bottomFaceRender.renderMask &= 0b11101111;
                        this.blockRenders[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }

            #endregion

            #region Front Face
            // ------------------------------------------ FRONT FACE ------------------------------------------ //

            // Check the previous face //
            if (vms.doGreedyMeshing == true)
            {
                int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                if (neighborFaceRenderIndex >= 0)
                {
                    BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                    int neighborBottomRenderIndex = this.getBlockRenderIndex(x - 1, y - 1, z);
                    if (neighborBottomRenderIndex >= 0)
                    {
                        BlockRender neighborBottomRender = this.blockRenders[neighborBottomRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 5)) != 0 && (neighborBottomRender.renderMask & (1 << 5)) != 0 && neighborFaceRender.blockID == neighborBottomRender.blockID && neighborFaceRender.frontWSize == neighborBottomRender.frontWSize)
                        {
                            neighborFaceRender.frontHSize = (byte)(neighborBottomRender.frontHSize + 1);
                            neighborBottomRender.renderMask &= 0b11011111;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                            this.blockRenders[neighborBottomRenderIndex] = neighborBottomRender;
                        }
                    }
                }
            }

            // Check if the face must be created //
            if (blockData.IsRenderable() == true && (vms.doFacesOcclusion == false || frontBlock.IsRenderable() == false) && isVisitedFace(frontBlockIndex) == true)
            {
                faceMask |= 1 << 5;

                if (vms.doGreedyMeshing == true)
                {
                    blockRender.frontWSize = 0;
                    blockRender.frontHSize = 0;
                    int neighborFaceRenderIndex = this.getBlockRenderIndex(x - 1, y, z);
                    if (neighborFaceRenderIndex >= 0)
                    {
                        BlockRender neighborFaceRender = this.blockRenders[neighborFaceRenderIndex];
                        if ((neighborFaceRender.renderMask & (1 << 5)) != 0 && neighborFaceRender.frontHSize <= 0 && blockRender.blockID == neighborFaceRender.blockID)
                        {
                            blockRender.frontWSize = (byte)(neighborFaceRender.frontWSize + 1);
                            neighborFaceRender.renderMask &= 0b11011111;
                            this.blockRenders[neighborFaceRenderIndex] = neighborFaceRender;
                        }
                    }
                }

            }

            // End the line if the block has reached the end of the chunk //
            if (vms.doGreedyMeshing == true && blockData.IsRenderable() == true && frontBlock.IsRenderable() == false && x >= this.chunkSize - 1)
            {
                int bottomFaceRenderIndex = this.getBlockRenderIndex(x, y - 1, z);
                if (bottomFaceRenderIndex >= 0)
                {
                    BlockRender bottomFaceRender = this.blockRenders[bottomFaceRenderIndex];
                    if ((bottomFaceRender.renderMask & (1 << 5)) != 0 && blockRender.blockID == bottomFaceRender.blockID && blockRender.frontWSize == bottomFaceRender.frontWSize)
                    {
                        blockRender.frontHSize = (byte)(bottomFaceRender.frontHSize + 1);
                        bottomFaceRender.renderMask &= 0b11011111;
                        this.blockRenders[bottomFaceRenderIndex] = bottomFaceRender;
                    }
                }
            }

            #endregion

            return faceMask;

        }

        private bool InBounds(int3 p)
        {
            return p.x >= 0 && p.y >= 0 && p.z >= 0 && p.x < chunkSize && p.y < chunkSize && p.z < chunkSize;
        }

        private int getBlockRenderIndex(int x, int y, int z)
        {
            if (x < 0 || x >= this.chunkSize || y < 0 || y >= this.chunkSize || z < 0 || z >= this.chunkSize)
                return -1;
            else
                return ToIndex(x, y, z);
        }
        
        private int ToIndex(int x, int y, int z)
        {
            return x + chunkSize * (y + chunkSize * z);
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

        private BlockData getBlock(int x, int y, int z)
        {

            // Check if this is a block from a neighbor chunk //
            if (x < 0)
            {
                if (this.leftNeighbor.IsCreated == false || this.leftNeighbor.Length != this.totalBlocks) return BlockData.Air;
                return this.leftNeighbor[ToIndex(this.chunkSize - 1, y, z)];
            }
            if (x >= this.chunkSize)
            {
                if (this.rightNeighbor.IsCreated == false || this.rightNeighbor.Length != this.totalBlocks) return BlockData.Air;
                return this.rightNeighbor[ToIndex(0, y, z)];
            }
            if (y < 0)
            {
                if (this.bottomNeighbor.IsCreated == false || this.bottomNeighbor.Length != this.totalBlocks) return BlockData.Air;
                return this.bottomNeighbor[ToIndex(x, this.chunkSize - 1, z)];
            }
            if (y >= this.chunkSize)
            {
                if (this.topNeighbor.IsCreated == false || this.topNeighbor.Length != this.totalBlocks) return BlockData.Air;
                return this.topNeighbor[ToIndex(x, 0, z)];
            }
            if (z < 0)
            {
                if (this.backNeighbor.IsCreated == false || this.backNeighbor.Length != this.totalBlocks) return BlockData.Air;
                return this.backNeighbor[ToIndex(x, y, this.chunkSize - 1)];
            }
            if (z >= this.chunkSize)
            {
                if (this.frontNeighbor.IsCreated == false || this.frontNeighbor.Length != this.totalBlocks) return BlockData.Air;
                return this.frontNeighbor[ToIndex(x, y, 0)];
            }

            // Check the current block //
            return this.currentChunk[ToIndex(x, y, z)];
        }

        private bool isVisitedFace(int index)
        {

            if (index < 0 || index >= this.totalBlocks)
                return true;

            if (vms.doFloodFill == true && vms.doLinearFloodFill == true)
                return this.floodVisited[index] == 1 && this.linearFloodVisited[index] == 1;
            else if (vms.doFloodFill == true && vms.doLinearFloodFill == false)
                return this.floodVisited[index] == 1;
            else if (vms.doFloodFill == false && vms.doLinearFloodFill == true)
                return this.linearFloodVisited[index] == 1;

            return true;
        }

        private bool IsFacingCamera(int x, int y, int z, FaceDirection dir)
        {
            if (vms.doFaceNormalCheck == false) return true;
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

}
