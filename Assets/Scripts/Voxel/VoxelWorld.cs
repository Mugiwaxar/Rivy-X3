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
using Unity.Burst;
using System.Threading.Tasks;






#if UNITY_EDITOR
using UnityEditor;
using static UnityEditor.PlayerSettings;
#endif

public class VoxelWorld : MonoBehaviour
{

    public static VoxelWorld _Instance;
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

    [SerializeField] public Material[] Materials;
    [NonSerialized] public Mesh[] Meshs;
    public Texture2D atlasTexture;

    public byte worldSizeInChunks = 4;
    public byte worldHeightInChunks = 8;

    public byte viewDistance = 10;
    public byte yWiewDistance = 3;
    public int worldTotalSizeInChunks
    {
        get { return worldSizeInChunks * worldSizeInChunks * worldHeightInChunks; }
    }
    public int chunkSize = 16;
    public byte chunkInitListSize = 5;

    public bool doFloodFill = true;
    public bool doLinearFloodFill = true;
    public bool doFacesOcclusion = true;
    public bool doGreedyMeshing = true;
    public bool doFaceNormalCheck = true;

    [NonSerialized] public bool requestWorldInit = true;

    [NonSerialized] public ChunkSManager ChunkSManager;

    public sealed class DeferredBootstrap : ICustomBootstrap
    {
        public bool Initialize(string defaultWorldName)
        {
            // Create the world //
            var world = new World("World");
            World.DefaultGameObjectInjectionWorld = world;
            return true;
        }
    }

    public void ResetWorld()
    {
        this.requestWorldInit = true;
    }

    async void Start()
    {

        // Save the instance //
        _Instance = this;

        // Create the chunks manager //
        GameObject cm = new GameObject("ChunkManager");
        cm.transform.SetParent(this.transform, false);
        this.ChunkSManager = cm.AddComponent<ChunkSManager>();

        // Start the world //
        World world = World.DefaultGameObjectInjectionWorld;
        var allSystems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);

        // Init all systems //
        DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, allSystems);

        // Disable the chunks group //
        ChunkPipelineGroup chunkGroup = world.GetExistingSystemManaged<ChunkPipelineGroup>();
        chunkGroup.Enabled = false;

        // Start all systems except the chunks group //
        ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);

        // Wait //
        await Task.Yield();

        // Start the chunks group //
        chunkGroup.Enabled = true;

    }

    private void OnDestroy()
    {
        NativePoolsManager.DisposeAll();
    }

    //void OnEnable()
    //{
    //    RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    //}

    //void OnDisable()
    //{
    //    RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    //}

    //void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    //{
    //    if (this.ChunkSManager.chunksList == null) return;

    //    foreach (KeyValuePair<Vector3Int, VoxelChunk> kvp in this.ChunkSManager.chunksList)
    //    {
    //        VoxelChunk chunk = kvp.Value;
    //        if (chunk.chunkReady && chunk.mesh != null && chunk.material != null)
    //        {
    //            Graphics.DrawMesh(chunk.mesh, chunk.chunkMatrix, chunk.material, 0, camera);
    //        }
    //    }
    //}


}
