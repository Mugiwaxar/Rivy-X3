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
    public static Entity CreateChunk(ref SystemState state, int3 position, int chunkSize)
    {

        // Create the entity //
        EntityManager entityManager = state.EntityManager;
        Entity chunk = entityManager.CreateEntity();

        // Add all components //
        entityManager.AddComponentData(chunk, new ChunkPosition { Value = new int3(position.x, position.y, position.z) });

        // Calcul the chunk transform and bounds //
        float3 worldPos = position * chunkSize;
        float3 center = new float3(chunkSize * 0.5f, chunkSize * 0.5f, chunkSize * 0.5f);
        float3 extents = new float3(chunkSize * 0.5f, chunkSize * 0.5f, chunkSize * 0.5f);
        AABB bounds = new AABB {Center = center,Extents = extents};

        // Add the render components //
        EntitiesGraphicsSystem gfx = state.World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
        BatchMaterialID batchMatID = gfx.RegisterMaterial(VoxelWorld._Instance.Materials[0]);
        BatchMeshID batchMeshID = gfx.RegisterMesh(VoxelWorld._Instance.ChunkSManager.DummyCube);
        RenderMeshDescription desc = new RenderMeshDescription(shadowCastingMode: UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows: true);
        MaterialMeshInfo mmi = new MaterialMeshInfo { MaterialID = batchMatID, MeshID = batchMeshID };
        RenderMeshUtility.AddComponents(chunk, entityManager, desc, mmi);

        // Add the local transform //
        entityManager.AddComponentData(chunk, LocalTransform.FromPosition(worldPos));

        // Set the bounds //
        entityManager.SetComponentData(chunk, new Unity.Rendering.RenderBounds {Value = bounds});

        // Add all enableable Components //
        entityManager.AddComponent<JustCreated>(chunk);
        entityManager.SetComponentEnabled<JustCreated>(chunk, true);

        // Check the blocks buffer //
        DynamicBuffer<BlockData> blocks = entityManager.AddBuffer<BlockData>(chunk);

        // Check the blocks buffer length //
        int total = chunkSize * chunkSize * chunkSize;
        if (blocks.Length < total)
            blocks.ResizeUninitialized(total);

        // Fill the chunk table with all blocks //
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    blocks[Utils.PosToIndex(chunkSize, x, y, z)] = new BlockData((byte)1);
                }
            }
        }

        return chunk;

    }

    [BurstCompile]
    public partial struct GenerateChunksGraphics : IJob
    {

        [ReadOnly] public int chunkSize;
        [ReadOnly] public int totalBlocks;
        [ReadOnly] public bool doFloodFill;
        [ReadOnly] public bool doLinearFloodFill;
        [ReadOnly] public bool doFacesOcclusion;
        [ReadOnly] public bool doGreedyMeshing;
        [ReadOnly] public bool doFaceNormalCheck;

        [ReadOnly] public int3 pos;
        [ReadOnly] public float3 chunkCenter;
        [ReadOnly] public float3 cameraPosition;
        [ReadOnly] public NativeParallelHashMap<int3, Entity> chunkMap;
        [ReadOnly] public BufferLookup<BlockData> blocksLookup;
        [ReadOnly] public AtlasData atlas;

        public NativeList<int3> frontier;
        public NativeArray<byte> floodVisited;
        public NativeArray<byte> linearFloodVisited;
        public NativeArray<BlockRender> blockRenders;
        public NativeList<SquareFace> squareList;

        public NativeList<float3> verticesList;
        public NativeList<int> trianglesList;
        public NativeList<float2> uvsList;

        public DynamicBuffer<BlockData> currentChunk;
        public DynamicBuffer<BlockData> leftNeighbor;
        public DynamicBuffer<BlockData> rightNeighbor;
        public DynamicBuffer<BlockData> bottomNeighbor;
        public DynamicBuffer<BlockData> topNeighbor;
        public DynamicBuffer<BlockData> backNeighbor;
        public DynamicBuffer<BlockData> frontNeighbor;

        public void Execute()
        {

            // Get current chunks //
            Entity chunkBlocks = ChunkSManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.None);
            this.currentChunk = this.blocksLookup[chunkBlocks];

            // Get all neighbors //
            Entity leftNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Left);
            if (leftNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(leftNeighborEntity)) this.leftNeighbor = this.blocksLookup[leftNeighborEntity];
            Entity rightNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Right);
            if (rightNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(rightNeighborEntity)) this.rightNeighbor = this.blocksLookup[rightNeighborEntity];
            Entity bottomNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Bottom);
            if (bottomNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(bottomNeighborEntity)) this.bottomNeighbor = this.blocksLookup[bottomNeighborEntity];
            Entity topNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Top);
            if (topNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(topNeighborEntity)) this.topNeighbor = this.blocksLookup[topNeighborEntity];
            Entity backNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Back);
            if (backNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(backNeighborEntity)) this.backNeighbor = this.blocksLookup[backNeighborEntity];
            Entity frontNeighborEntity = ChunkSManager.GetChunk(this.chunkMap, this.pos.x, this.pos.y, this.pos.z, EnumData.Direction.Front);
            if (frontNeighborEntity != Entity.Null && this.blocksLookup.HasBuffer(frontNeighborEntity)) this.frontNeighbor = this.blocksLookup[frontNeighborEntity];


            // Do the flood fill //
            if (this.doFloodFill == true)
                this.executeFloodFill();

            // Do the linear flood fill //
            if (this.doLinearFloodFill == true)
                this.executeLinearFloodFill();

            // Generate the render blocks //
            this.generateRenderBlocks();

            // Build the squares list //
            this.buildSquareList();

            // Build the mesh //
            this.buildMesh();

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

            // Check for all dirrections //
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
                    this.squareList.Add(new SquareFace(x, y - blockRender.leftHSize, z - blockRender.leftWSize, blockRender.leftWSize, blockRender.leftHSize, FaceDirection.Left, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 1)) != 0 &&
                    this.IsFacingCamera(x, y - blockRender.rightHSize, z, FaceDirection.Right))
                    this.squareList.Add(new SquareFace(x, y - blockRender.rightHSize, z, blockRender.rightWSize, blockRender.rightHSize, FaceDirection.Right, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 2)) != 0 &&
                    this.IsFacingCamera(x - blockRender.bottomWSize, y, z - blockRender.bottomHSize, FaceDirection.Bottom))
                    this.squareList.Add(new SquareFace(x - blockRender.bottomWSize, y, z - blockRender.bottomHSize, blockRender.bottomWSize, blockRender.bottomHSize, FaceDirection.Bottom, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 3)) != 0 &&
                    this.IsFacingCamera(x - blockRender.topWSize, y, z, FaceDirection.Top))
                    this.squareList.Add(new SquareFace(x - blockRender.topWSize, y, z, blockRender.topWSize, blockRender.topHSize, FaceDirection.Top, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 4)) != 0 &&
                    this.IsFacingCamera(x, y - blockRender.backHSize, z, FaceDirection.Back))
                    this.squareList.Add(new SquareFace(x, y - blockRender.backHSize, z, blockRender.backWSize, blockRender.backHSize, FaceDirection.Back, blockRender.blockID));

                if ((blockRender.renderMask & (1 << 5)) != 0 &&
                    this.IsFacingCamera(x - blockRender.frontWSize, y - blockRender.frontHSize, z, FaceDirection.Front))
                    this.squareList.Add(new SquareFace(x - blockRender.frontWSize, y - blockRender.frontHSize, z, blockRender.frontWSize, blockRender.frontHSize, FaceDirection.Front, blockRender.blockID));

            }

        }

        private void buildMesh()
        {

            // Transform the struct to lists //
            for (int i = 0; i < this.squareList.Length; i++)
            {
                SquareFace square = this.squareList[i];
                square.GetSquare(ref this.verticesList);
                square.GetTriangles(i * 4, ref this.trianglesList);
                square.GetUVs(ref this.uvsList, this.atlas);
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
            if (this.doGreedyMeshing == true)
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
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || leftBlock.IsRenderable() == false) && isVisitedFace(leftBockIndex) == true)
            {
                faceMask |= 1 << 0;
                if (this.doGreedyMeshing == true)
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
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && leftBlock.IsRenderable() == false && z >= this.chunkSize - 1)
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
            if (this.doGreedyMeshing == true)
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
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || rightBlock.IsRenderable() == false) && isVisitedFace(rightBlockIndex) == true)
            {
                faceMask |= 1 << 1;

                if (this.doGreedyMeshing == true)
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
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && rightBlock.IsRenderable() == false && z >= this.chunkSize - 1)
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
            if (this.doGreedyMeshing == true)
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
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || bottomBlock.IsRenderable() == false) && isVisitedFace(bottomBlockIndex) == true)
            {
                faceMask |= 1 << 2;

                if (this.doGreedyMeshing == true)
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
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && bottomBlock.IsRenderable() == false && x >= this.chunkSize - 1)
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
            if (this.doGreedyMeshing == true)
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
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || topBlock.IsRenderable() == false) && isVisitedFace(topBlockIndex) == true)
            {
                faceMask |= 1 << 3;

                if (this.doGreedyMeshing == true)
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
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && topBlock.IsRenderable() == false && x >= this.chunkSize - 1)
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
            if (this.doGreedyMeshing == true)
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
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || backBlock.IsRenderable() == false) && isVisitedFace(backBlockIndex) == true)
            {
                faceMask |= 1 << 4;

                if (this.doGreedyMeshing == true)
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
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && backBlock.IsRenderable() == false && x >= this.chunkSize - 1)
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
            if (this.doGreedyMeshing == true)
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
            if (blockData.IsRenderable() == true && (this.doFacesOcclusion == false || frontBlock.IsRenderable() == false) && isVisitedFace(frontBlockIndex) == true)
            {
                faceMask |= 1 << 5;

                if (this.doGreedyMeshing == true)
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
            if (this.doGreedyMeshing == true && blockData.IsRenderable() == true && frontBlock.IsRenderable() == false && x >= this.chunkSize - 1)
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

            if (this.doFloodFill == true && this.doLinearFloodFill == true)
                return this.floodVisited[index] == 1 && this.linearFloodVisited[index] == 1;
            else if (this.doFloodFill == true && this.doLinearFloodFill == false)
                return this.floodVisited[index] == 1;
            else if (this.doFloodFill == false && this.doLinearFloodFill == true)
                return this.linearFloodVisited[index] == 1;

            return true;
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
