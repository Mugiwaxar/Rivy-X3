#if UNITY_EDITOR
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

/// <summary>
/// Dessine tous les RenderBounds DOTS dans la Scene View.
/// À placer dans Assets/Editor/ ou n’importe où sous Editor.
/// </summary>
public class RenderBoundsGizmoDrawer : MonoBehaviour
{
    void OnDrawGizmos()
    {
        var world = World.DefaultGameObjectInjectionWorld;          // monde Editor ou Play
        if (world == null) return;

        var entityManager = world.EntityManager;
        var renderBoundsType = ComponentType.ReadOnly<RenderBounds>();
        var query = entityManager.CreateEntityQuery(renderBoundsType);

        using var boundsArray = query.ToComponentDataArray<RenderBounds>(Allocator.Temp);
        Gizmos.color = Color.yellow;

        foreach (var rb in boundsArray)
        {
            var aabb = rb.Value;
            Gizmos.DrawWireCube(aabb.Center, aabb.Extents * 2f);    // extents → demi‐tailles
        }
    }
}
#endif