using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.PlayerLoop;

namespace MeshProcess
{
    public class MeshDecomposerWindow : EditorWindow
    {
        VhacdSettings m_Settings = new VhacdSettings();
        bool m_ShowBar;

        void Awake()
        {
            titleContent = new GUIContent("VHACD Generation Settings");
        }

        void OnGUI()
        {
            // Asset directory selection
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(!string.IsNullOrEmpty(m_Settings.AssetPath) ? m_Settings.AssetPath : "Select a directory");
            if (GUILayout.Button("Select Directory"))
            {
                m_Settings.AssetPath = EditorUtility.OpenFolderPanel("Select Asset Directory", "Assets", "");
                m_Settings.MeshSavePath = $"{m_Settings.AssetPath.Substring(Application.dataPath.Length - "Assets".Length)}/Meshes";
            }
            EditorGUILayout.EndHorizontal();

            // File extension selection
            m_Settings.FileType =
                (VhacdSettings.FileExtension)EditorGUILayout.EnumPopup("Select Filetype", m_Settings.FileType);

            // Mesh save directory
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(!string.IsNullOrEmpty(m_Settings.MeshSavePath) ? m_Settings.MeshSavePath : "Select a directory");
            if (GUILayout.Button("Select Directory"))
            {
                var tmpMeshSavePath = EditorUtility.OpenFolderPanel("Select Mesh Save Directory", "Assets", "");
                m_Settings.MeshSavePath = tmpMeshSavePath.Substring(Application.dataPath.Length - "Assets".Length);
            }
            EditorGUILayout.EndHorizontal();

            // Bool settings
            m_Settings.OverwriteMeshComponents = GUILayout.Toggle(m_Settings.OverwriteMeshComponents, "Overwrite any existing collider components?");

            if (m_Settings.FileType == VhacdSettings.FileExtension.Prefab)
            {
                m_Settings.OverwriteAssets = GUILayout.Toggle(m_Settings.OverwriteAssets, "Overwrite existing assets?");
            }
            if ((m_Settings.FileType == VhacdSettings.FileExtension.Prefab && !m_Settings.OverwriteAssets) || m_Settings.FileType != VhacdSettings.FileExtension.Prefab)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(!string.IsNullOrEmpty(m_Settings.AssetSavePath) ? m_Settings.AssetSavePath : "Select a directory");
                if (GUILayout.Button("Select Directory"))
                {
                    var tmpAssetSavePath = EditorUtility.OpenFolderPanel("Select Save Directory", "Assets", "");
                    m_Settings.AssetSavePath = tmpAssetSavePath.Substring(Application.dataPath.Length - "Assets".Length);
                }
                EditorGUILayout.EndHorizontal();
            }


            // Generate
            if (!string.IsNullOrEmpty(m_Settings.AssetPath) && Directory.Exists(m_Settings.AssetPath))
            {
                var fileEnumerable = Directory.EnumerateFiles(m_Settings.AssetPath,
                    $"*{VhacdSettings.GetFileExtensionString(m_Settings.FileType)}", SearchOption.AllDirectories);
                m_Settings.TotalAssets = fileEnumerable.Count();
                GUILayout.Label($"Assets found in directory: {m_Settings.TotalAssets}");

                if (GUILayout.Button("Generate!"))
                {
                    EditorCoroutineUtility.StartCoroutine(OpenFilePanel(fileEnumerable), this);
                }
            }
            else
            {
                GUILayout.Label("Please select a directory!");
                m_ShowBar = false;
            }

            // Progress bar
            if (m_ShowBar && m_Settings.TotalAssets > 0)
            {
                UpdateProgressBar();
            }
        }

        void UpdateProgressBar()
        {
            var progress = m_Settings.AssetsConverted / m_Settings.TotalAssets;
            GUILayout.Label(
                $"Converting asset {m_Settings.AssetsConverted} of {m_Settings.TotalAssets} => {m_Settings.CurrentFile}");
            EditorGUI.ProgressBar(new Rect(3, 100, position.width - 6, 25), progress,
                $"{m_Settings.AssetsConverted}/{m_Settings.TotalAssets} Assets Converted");
            if (Math.Abs(progress - 1) < 0.01f)
            {
                Close();
                m_Settings.TotalAssets = 0;
                m_ShowBar = false;
            }
        }

        IEnumerator OpenFilePanel(IEnumerable<string> fileEnumerable)
        {
            m_Settings.AssetsConverted = 0;

            if (m_Settings.TotalAssets > 0)
            {
                m_ShowBar = true;

                foreach (var filePath in fileEnumerable)
                {
                    var f = filePath.Substring(Application.dataPath.Length - "Assets".Length);
                    var obj = AssetDatabase.LoadAssetAtPath<GameObject>(f);

                    m_Settings.MeshCountChild = 0;
                    m_Settings.MeshCountTotal = 0;
                    yield return EditorCoroutineUtility.StartCoroutine(GenerateConvexMeshes(obj, filePath), this);
                    Debug.Log($"Generated {m_Settings.MeshCountTotal} meshes on {f}");
                }
            }
            else
            {
                Close();
            }
        }

        IEnumerator GenerateConvexMeshes(GameObject go, string prefabPath)
        {
            var obj = Instantiate(go);
            Selection.activeObject = obj;
            EditorApplication.ExecuteMenuItem("Edit/Frame Selected");
            var templateFileName = obj.name.Substring(0, obj.name.Length - "(Clone)".Length);
            m_Settings.CurrentFile = templateFileName;
            var meshFilters = obj.GetComponentsInChildren<MeshFilter>();

            var meshIndex = 0;
            foreach (var meshFilter in meshFilters)
            {
                var child = meshFilter.gameObject;
                var decomposer = ConfigureVhacd(child);
                yield return new WaitForEndOfFrame();
                var colliderMeshes = decomposer.GenerateConvexMeshes(meshFilter.sharedMesh);
                yield return new WaitForEndOfFrame();
                foreach (var collider in colliderMeshes)
                {
                    meshIndex++;
                    var path = $"{m_Settings.MeshSavePath}/{templateFileName}/{meshIndex}.asset";
                    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException());

                    // Only create new asset if one doesn't exist or should overwrite
                    AssetDatabase.CreateAsset(collider, path);
                    AssetDatabase.SaveAssets();

                    if (m_Settings.OverwriteMeshComponents)
                    {
                        var existingColliders = child.GetComponents<MeshCollider>();
                        if (existingColliders.Length > 0)
                        {
                            Debug.Log($"{child.name} had existing colliders; overwriting!");
                            foreach (var coll in existingColliders) DestroyImmediate(coll);
                        }
                    }

                    var current = child.AddComponent<MeshCollider>();
                    current.sharedMesh = collider;
                    current.convex = true;
                    m_Settings.MeshCountChild++;
                    yield return new WaitForEndOfFrame();
                }

                DestroyImmediate(child.GetComponent<VHACD>());

                m_Settings.MeshCountTotal += m_Settings.MeshCountChild;
            }

            var localPath = prefabPath;

            if (!m_Settings.OverwriteAssets || m_Settings.FileType != VhacdSettings.FileExtension.Prefab)
            {
                Directory.CreateDirectory(m_Settings.AssetSavePath);

                localPath = $"{m_Settings.AssetSavePath}/{templateFileName}.prefab";

                // Make sure the file name is unique, in case an existing Prefab has the same name.
                localPath = AssetDatabase.GenerateUniqueAssetPath(localPath);
                Debug.Log($"Creating new prefab at: {localPath}");
            }
            else
            {
                Debug.Log($"Updating prefab {localPath.Substring(Application.dataPath.Length)}");
            }

            // Save the Prefab.
            PrefabUtility.SaveAsPrefabAssetAndConnect(obj, localPath, InteractionMode.AutomatedAction);
            DestroyImmediate(obj);
            m_Settings.AssetsConverted++;
        }

        static VHACD ConfigureVhacd(GameObject go)
        {
            var vhacd = go.AddComponent<VHACD>();
            vhacd.m_parameters.m_resolution = 10000;
            vhacd.m_parameters.m_concavity = 0.001;
            vhacd.m_parameters.m_planeDownsampling = 4;
            vhacd.m_parameters.m_convexhullDownsampling = 4;
            vhacd.m_parameters.m_alpha = 0.05;
            vhacd.m_parameters.m_beta = 0.05;
            vhacd.m_parameters.m_pca = 0;
            vhacd.m_parameters.m_mode = 0;
            vhacd.m_parameters.m_maxNumVerticesPerCH = 64;
            vhacd.m_parameters.m_minVolumePerCH = 0.0001;

            vhacd.m_parameters.m_convexhullApproximation = 1;
            vhacd.m_parameters.m_oclAcceleration = 0;
            vhacd.m_parameters.m_maxConvexHulls = 1024;
            vhacd.m_parameters.m_projectHullVertices = true;

            return vhacd;
        }
    }
}
