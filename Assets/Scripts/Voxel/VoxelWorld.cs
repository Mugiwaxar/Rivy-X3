using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Collections;
using static Atlas;
using static EnumData;
using System;
using Unity.Entities;
using Unity.Collections;

using Unity.Mathematics;




#if UNITY_EDITOR
using UnityEditor;
using static UnityEditor.PlayerSettings;
#endif

public class VoxelWorld : MonoBehaviour
{

    [NonSerialized] public static VoxelWorld _Instance;
    public static ChunkSManager _ChunkManager
    {
        get { return _Instance.ChunkSManager; }
    }
    public AtlasData _Atlas
    {
        get
        {
            return Atlas.GetAtlasData(atlasTexture, 32);
        }
    }

    public Material material;
    public Texture2D atlasTexture;

    public byte worldSizeInChunks = 4;
    public byte worldHeightInChunks = 8;
    public int worldTotalSizeInChunks
    {
        get { return worldSizeInChunks * worldSizeInChunks * worldHeightInChunks; }
    }
    public byte chunkSize = 16;

    public bool doFloodFill = true;
    public bool doLinearFloodFill = true;
    public bool doFacesOcclusion = true;
    public bool doGreedyMeshing = true;
    public bool doFaceNormalCheck = true;

    [NonSerialized] public ChunkSManager ChunkSManager;



    public void ResetWorld()
    {
        this.ChunkSManager.GenerateAllChunks();
    }

    private void Start()
    {

        // Save the instance //
        _Instance = this;

        // Create the chunks manager //
        GameObject cm = new GameObject("ChunkManager");
        cm.transform.SetParent(this.transform, false);
        this.ChunkSManager = cm.AddComponent<ChunkSManager>();
        this.ChunkSManager.world = this;

        // Create all chunks //
        this.ChunkSManager.GenerateAllChunks();

        // Set the chunks settings //
        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        Entity entity = entityManager.CreateEntity();
        entityManager.AddComponentData(entity, new VoxelSettings
        {
            chunkSize = chunkSize,
            worldSizeInChunks = worldSizeInChunks,
            worldHeightInChunks = worldHeightInChunks,
            doFloodFill = doFloodFill,
            doLinearFloodFill = doLinearFloodFill,
            doFacesOcclusion = doFacesOcclusion,
            doGreedyMeshing = doGreedyMeshing,
            doFaceNormalCheck = doFaceNormalCheck
        });

    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (this.ChunkSManager.chunksList == null) return;

        foreach (KeyValuePair<Vector3Int, VoxelChunk> kvp in this.ChunkSManager.chunksList)
        {
            VoxelChunk chunk = kvp.Value;
            if (chunk.chunkReady && chunk.mesh != null && chunk.material != null)
            {
                Graphics.DrawMesh(chunk.mesh, chunk.chunkMatrix, chunk.material, 0, camera);
            }
        }
    }


}
