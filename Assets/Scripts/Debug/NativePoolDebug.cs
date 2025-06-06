using Assets.Scripts.Block;
using System;
using TMPro;
using Unity.Mathematics;
using UnityEngine;

public class NativePoolDebug : MonoBehaviour
{

    public float updateDelay = 1;

    private float lastUpdate;

    void FixedUpdate()
    {
        if (Time.time - this.lastUpdate > this.updateDelay)
        {
            this.lastUpdate = Time.time;
            string text = NativesPool<int3>.GetStats() + Environment.NewLine;
            text += NativesPool<byte>.GetStats() + Environment.NewLine;
            text += NativesPool<BlockRender>.GetStats() + Environment.NewLine;
            text += NativesPool<SquareFace>.GetStats() + Environment.NewLine;
            text += NativesPool<float3>.GetStats() + Environment.NewLine;
            text += NativesPool<int>.GetStats() + Environment.NewLine;
            text += NativesPool<float2>.GetStats() + Environment.NewLine;

            this.GetComponent<TextMeshProUGUI>().text = text;
        }
    }
}
