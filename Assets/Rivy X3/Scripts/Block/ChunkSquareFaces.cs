using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static Atlas;

namespace Assets.Scripts.Block
{
    public struct ChunkSquareFaces : IBufferElementData
    {

        public int x;
        public int y;
        public int z;
        public byte sizeW;
        public byte sizeH;
        public FaceDirection direction;
        public byte id;

        public ChunkSquareFaces(int x, int y, int z, byte sizeW, byte sizeH, FaceDirection direction, byte id)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.sizeW = (byte)(sizeW + 1);
            this.sizeH = (byte)(sizeH + 1);
            this.direction = direction;
            this.id = id;
        }

        public void GetSquare(ref NativeList<float3> verticesList, int3 offset)
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
            verticesList.AddNoResize(p0 + offset);
            verticesList.AddNoResize(p1 + offset);
            verticesList.AddNoResize(p2 + offset);
            verticesList.AddNoResize(p3 + offset);
        }


        public void GetTriangles(int index, ref NativeList<int> trianglesList)
        {

            trianglesList.AddNoResize(index + 0);
            trianglesList.AddNoResize(index + 1);
            trianglesList.AddNoResize(index + 2);
            trianglesList.AddNoResize(index + 0);
            trianglesList.AddNoResize(index + 2);
            trianglesList.AddNoResize(index + 3);
        }

        public void GetUVs(ref NativeList<float2> uvsList, ref NativeList<float4> atlasInfoList, AtlasData atlasData)
        {
            float cellWidth = atlasData.CellWidthUV;
            float cellHeight = atlasData.CellHeightUV;

            uint2 atlasIndex = Utils.IDToAtlasIndex((EnumData.BlocksID)id);

            float uMin = atlasIndex.x * cellWidth;
            float vMin = atlasIndex.y * cellHeight;

            // Local UV coordinates, used to repeat the texture in the shader
            uvsList.AddNoResize(new float2(0, 0)); // Bottom-left
            uvsList.AddNoResize(new float2(this.sizeW, 0)); // Bottom-right
            uvsList.AddNoResize(new float2(this.sizeW, this.sizeH)); // Top-right
            uvsList.AddNoResize(new float2(0, this.sizeH)); // Top-left

            // Atlas information : offset (xy) and scale (zw)
            float4 info = new float4(uMin, vMin, cellWidth, cellHeight);
            atlasInfoList.AddNoResize(info);
            atlasInfoList.AddNoResize(info);
            atlasInfoList.AddNoResize(info);
            atlasInfoList.AddNoResize(info);
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

}
