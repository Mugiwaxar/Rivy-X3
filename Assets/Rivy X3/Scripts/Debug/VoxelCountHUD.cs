using Assets.Scripts.Block;
using System;
using TMPro;
using Unity.Mathematics;
using UnityEngine;

public class VoxelCountHUD : MonoBehaviour
{

    public float updateDelay = 1;

    private float lastUpdate;

    void FixedUpdate()
    {
        if (Time.time - this.lastUpdate > this.updateDelay)
        {

            this.lastUpdate = Time.time;

            VoxelWorld world = VoxelWorld._Instance;


            if (Utils.TryGetSingletonECS<DataSingleton>(out DataSingleton DS) == false)
                return;

            int regionCount = DS.regionsMap.Count();
            int chunkCount = DS.chunksMap.Count();
            int chunkJob = DS.chunkToBuildQueue.Count;
            int blockCount = chunkCount * world.chunkBlocksCount;

            string text = $"Regions: {regionCount},  Chunks: {chunkCount},  Blocks: {blockCount}" + Environment.NewLine;
            text += $"Chunks Job: {chunkJob}";

            this.GetComponent<TextMeshProUGUI>().text = text;

        }
    }
}
