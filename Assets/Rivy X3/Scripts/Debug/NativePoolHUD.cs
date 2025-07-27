using Assets.Scripts.Block;
using System;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class NativePoolHUD : MonoBehaviour
{

    public float updateDelay = 1;

    private float lastUpdate;

    void FixedUpdate()
    {
        if (Time.time - this.lastUpdate > this.updateDelay)
        {
            this.lastUpdate = Time.time;
            string text = NativesPoolManager<int3>.GetStats() + Environment.NewLine;
            text += NativesPoolManager<byte>.GetStats() + Environment.NewLine;
            text += NativesPoolManager<BlockRender>.GetStats() + Environment.NewLine;
            text += NativesPoolManager<ChunkSquareFaces>.GetStats() + Environment.NewLine;
            text += NativesPoolManager<float3>.GetStats() + Environment.NewLine;
            text += NativesPoolManager<int>.GetStats() + Environment.NewLine;
            text += NativesPoolManager<float2>.GetStats() + Environment.NewLine;
            text += NativesPoolManager<Entity>.GetStats() + Environment.NewLine;
            text += NativesPoolManager<BlockData>.GetStats() + Environment.NewLine;
            text += MeshesPoolManager.GetStats() + Environment.NewLine;
            text += ChunksPoolManager.GetStats();

            this.GetComponent<TextMeshProUGUI>().text = text;
        }
    }
}
