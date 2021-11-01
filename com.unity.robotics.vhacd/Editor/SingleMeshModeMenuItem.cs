using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MeshProcess
{
    public static class SingleMeshModeMenuItem
    {
        [MenuItem("VHACD/Single Mesh Mode")]
        public static void OpenGenerateWindow()
        {
            // Get existing open window or if none, make a new one:
            var window = (SingleMeshModeWindow)EditorWindow.GetWindow(typeof(SingleMeshModeWindow));
            window.minSize = new Vector2(500, 500);
            window.Show();
        }
    }
}
