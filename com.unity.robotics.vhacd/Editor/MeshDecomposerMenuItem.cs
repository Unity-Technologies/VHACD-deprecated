using System;
using UnityEditor;
using UnityEngine;

namespace MeshProcess
{
    public static class MeshDecomposerMenuItem
    {
        [MenuItem("VHACD/Generate Collider Meshes")]
        public static void OpenGenerateWindow()
        {
            // Get existing open window or if none, make a new one:
            var window = (MeshDecomposerWindow)EditorWindow.GetWindow(typeof(MeshDecomposerWindow));
            window.minSize = new Vector2(400, 525);
            window.Show();
        }

        [MenuItem("VHACD/Generate Collider Meshes", true)]
        static bool ValidateGenerateWindow()
        {
            return !Application.isPlaying;
        }
    }
}
