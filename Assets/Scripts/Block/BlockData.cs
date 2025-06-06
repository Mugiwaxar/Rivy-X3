using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

[Flags]
public enum VoxelFlags : byte
{
    None = 0,
    Transparent = 1 << 0,
    NonSolide = 2 << 0
}

public struct BlockData : IBufferElementData
{

    public byte id;
    public byte flags;

    public static readonly BlockData Air = new BlockData(0, false);

    public BlockData(byte id, bool isTransparent = false)
    {
        this.id = id;
        this.flags = 0;
        if (isTransparent == true)
            this.SetFlag(VoxelFlags.Transparent, true);
    }

    public bool HasFlag(VoxelFlags flag)
    {
        return ((VoxelFlags)flags & flag) != 0;
    }

    public void SetFlag(VoxelFlags flag, bool value)
    {
        if (value)
            flags |= (byte)flag;
        else
            flags &= (byte)~flag;
    }

    public bool IsAir()
    {
        if (this.id == (byte)EnumData.BlocksID.Air)
            return true;
        else
            return false;
    }

    public bool IsRenderable()
    {
        if (this.IsAir() == true || this.HasFlag(VoxelFlags.Transparent) == true)
            return false;
        else
            return true;
    }

}