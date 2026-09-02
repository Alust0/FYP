using UnityEngine;
using UnityEditor;

// Custom Inspector for WebcamBackground — replaces the raw index field with a named dropdown
[CustomEditor(typeof(WebcamBackground))]
public class WebcamBackgroundEditor : Editor
{
    public override void OnInspectorGUI()
    {
        WebcamBackground wb = (WebcamBackground)target;
        serializedObject.Update();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Webcam", EditorStyles.boldLabel);

        WebCamDevice[] devices;
        try
        {
            devices = WebCamTexture.devices;
        }
        catch (System.Exception e)
        {
            // A connected camera (often virtual-camera software like OBS/DroidCam/iVCam)
            // can report a device name with invalid bytes, which crashes this call.
            EditorGUILayout.HelpBox("Failed to enumerate webcam devices: " + e.Message, MessageType.Error);
            devices = new WebCamDevice[0];
        }

        if (devices.Length == 0)
        {
            EditorGUILayout.HelpBox("No webcam devices detected.", MessageType.Warning);
        }
        else
        {
            string[] names = System.Array.ConvertAll(devices, d => d.name);

            // Find which index matches the stored name
            int current = System.Array.IndexOf(names, wb.selectedDeviceName);
            if (current < 0) current = 0;

            int chosen = EditorGUILayout.Popup("Device", current, names);

            if (chosen != current || string.IsNullOrEmpty(wb.selectedDeviceName))
            {
                Undo.RecordObject(wb, "Change Webcam Device");
                wb.selectedDeviceName = names[chosen];
                EditorUtility.SetDirty(wb);
            }

            EditorGUILayout.HelpBox("Selected: " + wb.selectedDeviceName, MessageType.None);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Resolution & FPS", EditorStyles.boldLabel);

        Undo.RecordObject(wb, "Webcam Settings");
        wb.targetWidth = EditorGUILayout.IntField("Target Width", wb.targetWidth);
        wb.targetHeight = EditorGUILayout.IntField("Target Height", wb.targetHeight);
        wb.targetFPS = EditorGUILayout.IntField("Target FPS", wb.targetFPS);

        serializedObject.ApplyModifiedProperties();
    }
}
