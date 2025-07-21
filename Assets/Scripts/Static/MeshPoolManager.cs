using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public static class MeshPoolManager
{
    private static readonly Stack<Mesh> Pool = new Stack<Mesh>();
    public static Dictionary<int3, Mesh> UsedMesh = new Dictionary<int3, Mesh>();
    private static readonly object Locker = new();
    private const int MaxMeshInStack = 100;

    public static Mesh GetMesh()
    {
        lock (Locker)
        {
            if (Pool.Count > 0)
            {
                Mesh mesh = Pool.Pop();
                if (mesh != null)
                {
                    mesh.Clear();
                    return mesh;
                }
            }
        }
        Mesh newMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        newMesh.MarkDynamic();
        return newMesh;
    }

    public static void ReleaseMesh(Mesh mesh)
    {
        if (mesh == null) return;
        mesh.Clear();
        lock (Locker)
        {
            if (Pool.Count < MaxMeshInStack)
                Pool.Push(mesh);
            else
                Object.Destroy(mesh);
        }
    }

    public static void SaveMesh(int3 coor, Mesh mesh)
    {
        UsedMesh.TryAdd(coor, mesh);
    }

    public static Mesh GetSavedMesh(int3 coord)
    {
        if (UsedMesh.TryGetValue(coord, out Mesh mesh) == true)
            return mesh;
        else
            return GetMesh();
    }

    public static void ReleaseSavedMesh(int3 coord)
    {
        if (UsedMesh.TryGetValue(coord, out Mesh mesh) == true)
            ReleaseMesh(mesh);
    }

    public static void DisposeAll()
    {
        foreach (var mesh in Pool)
            Object.Destroy(mesh);
        Pool.Clear();
    }

    public static string GetStats()
    {
        return $"Mesh released: {Pool.Count},  Mesh used: {UsedMesh.Count}";
    }
}