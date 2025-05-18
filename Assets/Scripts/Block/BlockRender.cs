using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;

namespace Assets.Scripts.Block
{
    public struct BlockRender : IBufferElementData
    {

        public byte blockID;
        public byte renderMask;

        public byte leftWSize;
        public byte rightWSize;
        public byte bottomWSize;
        public byte topWSize;
        public byte backWSize;
        public byte frontWSize;

        public byte leftHSize;
        public byte rightHSize;
        public byte bottomHSize;
        public byte topHSize;
        public byte backHSize;
        public byte frontHSize;

        public BlockRender(byte blockID)
        {
            this.blockID = blockID;
            this.renderMask = 0b01000000;

            this.leftWSize = 0;
            this.rightWSize = 0;
            this.bottomWSize = 0;
            this.topWSize = 0;
            this.backWSize = 0;
            this.frontWSize = 0;

            this.leftHSize = 0;
            this.rightHSize = 0;
            this.bottomHSize = 0;
            this.topHSize = 0;
            this.backHSize = 0;
            this.frontHSize = 0;

        }

    }
}
