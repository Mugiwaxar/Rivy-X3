using UnityEngine;
using UnityEngine.Rendering;
using static Atlas;
using System;
using Unity.Entities;
using System.Threading.Tasks;
using Unity.Rendering;

#if UNITY_EDITOR
using UnityEditor;
using static UnityEditor.PlayerSettings;
#endif

public class VoxelWorld : MonoBehaviour
{

    public static VoxelWorld _Instance;
    public static ChunksManager _ChunkManager
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
    public Texture2D atlasTexture;

    public byte worldSizeInChunks = 4;
    public byte worldHeightInChunks = 8;
    public int worldTotalSizeInChunk { get { return worldSizeInChunks * worldSizeInChunks * worldHeightInChunks; } }

    public int maxRegionDistance = 30;
    public int nearRegionDistance = 20;
    public int playerContactRegionDistance = 2;
    public int yViewDistance = 3;

    public int regionSize = 16;
    public int chunkSize = 16;
    public int chunkBlocksCount { get { return this.chunkSize * this.chunkSize * this.chunkSize; } }
    public int regionBlocksCount { get { return this.chunkSize * this.chunkSize * this.chunkSize * this.regionSize * this.regionSize * this.regionSize; } }
    public byte chunkInitListSize = 5;

    public bool doFloodFill = true;
    public bool doLinearFloodFill = true;
    public bool doFacesOcclusion = true;
    public bool doGreedyMeshing = true;
    public bool doFaceNormalCheck = true;
    public bool removeFullAirChunk = true;

    [NonSerialized] public bool requestWorldInit = true;

    [NonSerialized] public bool MustUpdateSingleton = false;

    [NonSerialized] public ChunksManager ChunkSManager;

    [NonSerialized] public BatchMaterialID MaterialID;

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
        this.InitWorld();
    }

    void Start()
    {
        this.InitWorld();
    }

    async void InitWorld()
    {

        // Save the instance //
        _Instance = this;

        // Create the chunks manager //
        if (this.ChunkSManager != null)
            GameObject.DestroyImmediate(this.ChunkSManager.gameObject);
        GameObject cm = new GameObject("ChunkManager");
        cm.transform.SetParent(this.transform, false);
        this.ChunkSManager = cm.AddComponent<ChunksManager>();

        // Destroy the old world if exist //
        World.DefaultGameObjectInjectionWorld.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Create the new world //
        World world = new World("VoxelWorld");
        World.DefaultGameObjectInjectionWorld = world;

        // Start the world //
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
        this.requestWorldInit = true;

        // Get the material ID //
        EntitiesGraphicsSystem gfxSys = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<EntitiesGraphicsSystem>();
        this.MaterialID = gfxSys.RegisterMaterial(this.Materials[0]);

    }

    private void OnDestroy()
    {
        NativePoolManager.DisposeAll();
        MeshPoolManager.DisposeAll();
    }

    private void OnValidate()
    {
        // Update the settings singleton //
        this.MustUpdateSingleton = true;
    }

}
