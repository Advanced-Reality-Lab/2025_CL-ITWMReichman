using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CityBlock))]
public class CityBlockEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var block = (CityBlock)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Draw the block on the inner curb, in order. Generate lines buildings along that edge, facing the street, and leaves the middle empty.",
            MessageType.Info);

        if (GUILayout.Button("Generate Perimeter"))
        {
            Undo.RegisterCompleteObjectUndo(block, "Generate City Block");
            block.Generate();
        }

        if (GUILayout.Button("Clear Buildings"))
        {
            Undo.RegisterCompleteObjectUndo(block, "Clear City Block");
            block.ClearBuildings();
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Create 40m Rectangle"))
        {
            Undo.RecordObject(block, "Create City Block Rectangle");
            block.boundary.Clear();
            block.boundary.Add(new Vector3(-20f, 0f, -20f));
            block.boundary.Add(new Vector3(20f, 0f, -20f));
            block.boundary.Add(new Vector3(20f, 0f, 20f));
            block.boundary.Add(new Vector3(-20f, 0f, 20f));
            EditorUtility.SetDirty(block);
        }

        if (GUILayout.Button("Copy Polygon Collider 2D"))
        {
            Undo.RecordObject(block, "Copy City Block Polygon");
            block.CopyFromPolygonCollider2D();
            EditorUtility.SetDirty(block);
        }
    }

    void OnSceneGUI()
    {
        var block = (CityBlock)target;
        if (block.boundary == null) return;

        Handles.color = new Color(0.95f, 0.35f, 0.2f, 1f);
        for (int i = 0; i < block.boundary.Count; i++)
        {
            Vector3 world = block.transform.TransformPoint(block.boundary[i]);
            Vector3 next = block.transform.TransformPoint(block.boundary[(i + 1) % block.boundary.Count]);
            if (block.boundary.Count > 1)
                Handles.DrawAAPolyLine(3f, world, next);

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(block, "Move City Block Corner");
                block.boundary[i] = block.transform.InverseTransformPoint(moved);
                EditorUtility.SetDirty(block);
            }
        }
    }
}
