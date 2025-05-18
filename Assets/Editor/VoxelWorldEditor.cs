using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VoxelWorld))]
public class VoxelWorldEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw default inspector
        DrawDefaultInspector();

        VoxelWorld voxelWorld = (VoxelWorld)target;

        EditorGUILayout.Space(10);

        // Create a Reset World button
        if (GUILayout.Button("Reset World"))
        {
            voxelWorld.ResetWorld();
        }
    }
}
