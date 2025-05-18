using Assets.Scripts.Block;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public static class NativesPool<T> where T : unmanaged
{

    private static int MaxArrayInStack = 1000;
    private static int MaxListInStack = 100;

    private static readonly Dictionary<int, Stack<NativeArray<T>>> ArrayPool = new();
    private static readonly Dictionary<int, Stack<NativeList<T>>> ListPool = new();
    private static readonly object Locker = new();

    public static NativeArray<T> GetArray(int length)
    {
        lock (Locker)
        {
            if (ArrayPool.TryGetValue(length, out var stack) && stack.Count > 0)
            {
                NativeArray<T> array = stack.Pop();
                if (array.IsCreated) return array;
            }
        }
        return new NativeArray<T>(length, Allocator.Persistent);
    }

    public static NativeList<T> GetList(int length)
    {
        lock (Locker)
        {
            if (ListPool.TryGetValue(length, out var stack) && stack.Count > 0)
            {
                NativeList<T> list = stack.Pop();
                if (list.IsCreated) return list;
            }
        }
        return new NativeList<T>(length, Allocator.Persistent);
    }

    public static void ReleaseArray(NativeArray<T> array)
    {
        if (array.IsCreated == false) return;

        if (array.Length < 128)
        {
            for (int i = 0; i < array.Length; i++)
                array[i] = default;
        }
        else
        {
            ClearArray job = new() { array = array };
            job.ScheduleParallelByRef(array.Length, array.Length / 16, default).Complete();
        }

        lock (Locker)
        {
            if (ArrayPool.TryGetValue(array.Length, out var stack) == false)
            {
                stack = new Stack<NativeArray<T>>();
                ArrayPool[array.Length] = stack;
            }
            if (stack.Count < MaxArrayInStack)
                stack.Push(array);
            else
                array.Dispose();
        }
    }

    public static void ReleaseList(NativeList<T> list)
    {
        if (list.IsCreated == false) return;

        list.Clear();

        lock (Locker)
        {
            if (ListPool.TryGetValue(list.Capacity, out var stack) == false)
            {
                stack = new Stack<NativeList<T>>();
                ListPool[list.Capacity] = stack;
            }
            if (stack.Count < MaxListInStack)
                stack.Push(list);
            else
                list.Dispose();
        }
    }

    public static void DisposeAll()
    {
        foreach (var stack in ArrayPool.Values)
            foreach (var array in stack)
                if (array.IsCreated) array.Dispose();

        foreach (var stack in ListPool.Values)
            foreach (var list in stack)
                if (list.IsCreated) list.Dispose();

        ArrayPool.Clear();
        ListPool.Clear();
    }

    [BurstCompile]
    private struct ClearArray : IJobFor
    {

        [WriteOnly] public NativeArray<T> array;

        public void Execute(int index)
        {
            array[index] = default;
        }
    }

}
