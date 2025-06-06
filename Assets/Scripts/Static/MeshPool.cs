using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class MeshPool
{
    private static readonly Stack<Mesh> Pool = new();
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

    public static void DisposeAll()
    {
        foreach (var mesh in Pool)
            Object.Destroy(mesh);
        Pool.Clear();
    }

    public static string GetStats()
    {
        int total = Pool.Count;
        return $"Mesh count: {total}";
    }
}