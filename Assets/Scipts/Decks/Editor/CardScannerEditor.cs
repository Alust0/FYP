using UnityEditor;
using UnityEngine;

// CardScanner's inspector: a tick box per Cloud Reco database in the scene
[CustomEditor(typeof(CardScanner))]
public class CardScannerEditor : Editor
{
    void OnEnable() => ((CardScanner)target).FindDatabases();

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "databases");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(new GUIContent("Cloud Reco Databases", "Ticked databases are searched while scanning; the rest stay off."), EditorStyles.boldLabel);
        SerializedProperty databases = serializedObject.FindProperty("databases");
        if (databases.arraySize == 0)
            EditorGUILayout.HelpBox("No CloudRecoBehaviour in this scene, so scanning can't start.", MessageType.Info);
        for (int i = 0; i < databases.arraySize; i++)
        {
            SerializedProperty database = databases.GetArrayElementAtIndex(i);
            Object cloudReco = database.FindPropertyRelative("cloudReco").objectReferenceValue;
            EditorGUILayout.PropertyField(database.FindPropertyRelative("use"), new GUIContent(cloudReco != null ? cloudReco.name : "(missing)"));
        }

        serializedObject.ApplyModifiedProperties();
    }
}
