using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;

namespace MeshProcess
{
    public class MeshDecomposerWindow : EditorWindow
    {
        VHACD.Parameters m_Parameters;
        VhacdSettings m_Settings = new VhacdSettings();
        bool m_ShowBar;

        void Awake()
        {
            titleContent = new GUIContent("VHACD Generation Settings");

            // TODO: cleanup default value assignment
            m_Parameters.m_resolution = 10000;
            m_Parameters.m_concavity = 0.001;
            m_Parameters.m_planeDownsampling = 4;
            m_Parameters.m_convexhullDownsampling = 4;
            m_Parameters.m_alpha = 0.05;
            m_Parameters.m_beta = 0.05;
            m_Parameters.m_pca = 0;
            m_Parameters.m_mode = 0;
            m_Parameters.m_maxNumVerticesPerCH = 64;
            m_Parameters.m_minVolumePerCH = 0.0001;
            m_Parameters.m_convexhullApproximation = 1;
            m_Parameters.m_oclAcceleration = 0;
            m_Parameters.m_maxConvexHulls = 1024;
            m_Parameters.m_projectHullVertices = true;
        }

        void OnGUI()
        {
            // Asset directory selection
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(!string.IsNullOrEmpty(m_Settings.AssetPath) ? m_Settings.AssetPath : "Select a directory");
            if (GUILayout.Button("Select Directory"))
            {
                m_Settings.AssetPath = EditorUtility.OpenFolderPanel("Select Asset Directory", "Assets", "");
                if (!string.IsNullOrEmpty(m_Settings.AssetPath))
                {
                    m_Settings.MeshSavePath =
                        $"{m_Settings.AssetPath.Substring(Application.dataPath.Length - "Assets".Length)}/Meshes";
                }
            }
            EditorGUILayout.EndHorizontal();

            // File extension selection
            m_Settings.FileType =
                (VhacdSettings.FileExtension)EditorGUILayout.EnumPopup("Select Filetype", m_Settings.FileType);

            // Mesh save directory
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(!string.IsNullOrEmpty(m_Settings.MeshSavePath)
                ? m_Settings.MeshSavePath
                : "Select a directory");
            if (GUILayout.Button("Select Directory"))
            {
                var tmpMeshSavePath = EditorUtility.OpenFolderPanel("Select Mesh Save Directory", "Assets", "");
                if (!string.IsNullOrEmpty(tmpMeshSavePath))
                {
                    m_Settings.MeshSavePath = tmpMeshSavePath.Substring(Application.dataPath.Length - "Assets".Length);
                }
            }

            EditorGUILayout.EndHorizontal();

            // Bool settings
            m_Settings.OverwriteMeshComponents = GUILayout.Toggle(m_Settings.OverwriteMeshComponents,
                "Overwrite any existing collider components?");
            if (m_Settings.FileType == VhacdSettings.FileExtension.Prefab)
            {
                m_Settings.OverwriteAssets = GUILayout.Toggle(m_Settings.OverwriteAssets, "Overwrite existing assets?");
            }

            if (m_Settings.FileType == VhacdSettings.FileExtension.Prefab && !m_Settings.OverwriteAssets ||
                m_Settings.FileType != VhacdSettings.FileExtension.Prefab)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(!string.IsNullOrEmpty(m_Settings.AssetSavePath)
                    ? m_Settings.AssetSavePath
                    : "Select a directory");
                if (GUILayout.Button("Select Directory"))
                {
                    var tmpAssetSavePath = EditorUtility.OpenFolderPanel("Select Save Directory", "Assets", "");
                    if (!string.IsNullOrEmpty(tmpAssetSavePath))
                    {
                        m_Settings.AssetSavePath =
                            tmpAssetSavePath.Substring(Application.dataPath.Length - "Assets".Length);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            // VHACD decomposition parameters
            GUILayout.Label("VHACD Parameters");
            VhacdGuiLayout();

            // Generate
            if (!string.IsNullOrEmpty(m_Settings.AssetPath) && Directory.Exists(m_Settings.AssetPath))
            {
                var fileEnumerable = Directory.EnumerateFiles(m_Settings.AssetPath,
                    $"*.{VhacdSettings.GetFileExtensionString(m_Settings.FileType)}", SearchOption.AllDirectories);
                m_Settings.TotalAssets = fileEnumerable.Count();
                GUILayout.Label($"Assets found in directory: {m_Settings.TotalAssets}");

                if (GUILayout.Button("Generate!"))
                {
                    EditorCoroutineUtility.StartCoroutine(OpenFiles(fileEnumerable), this);
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
            EditorGUI.ProgressBar(new Rect(3, 450, position.width - 6, 25), progress,
                $"{m_Settings.AssetsConverted}/{m_Settings.TotalAssets} Assets Converted");
            if (Math.Abs(progress - 1) < 0.01f)
            {
                Close();
                m_Settings.TotalAssets = 0;
                m_ShowBar = false;
            }
        }

        IEnumerator OpenFiles(IEnumerable<string> fileEnumerable)
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
                if (m_Settings.OverwriteMeshComponents)
                {
                    var existingColliders = child.GetComponents<MeshCollider>();
                    if (existingColliders.Length > 0)
                    {
                        Debug.Log($"{child.name} had existing colliders; overwriting!");
                        foreach (var coll in existingColliders) DestroyImmediate(coll);
                    }
                }
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

        VHACD ConfigureVhacd(GameObject go)
        {
            var vhacd = go.AddComponent<VHACD>();

            vhacd.m_parameters.m_resolution = m_Parameters.m_resolution;
            vhacd.m_parameters.m_concavity = m_Parameters.m_concavity;
            vhacd.m_parameters.m_planeDownsampling = m_Parameters.m_planeDownsampling;
            vhacd.m_parameters.m_convexhullDownsampling = m_Parameters.m_convexhullDownsampling;
            vhacd.m_parameters.m_alpha = m_Parameters.m_alpha;
            vhacd.m_parameters.m_beta = m_Parameters.m_beta;
            vhacd.m_parameters.m_pca = m_Parameters.m_pca;
            vhacd.m_parameters.m_mode = m_Parameters.m_mode;
            vhacd.m_parameters.m_maxNumVerticesPerCH = m_Parameters.m_maxNumVerticesPerCH;
            vhacd.m_parameters.m_minVolumePerCH = m_Parameters.m_minVolumePerCH;
            vhacd.m_parameters.m_convexhullApproximation = m_Parameters.m_convexhullApproximation;
            vhacd.m_parameters.m_oclAcceleration = m_Parameters.m_oclAcceleration;
            vhacd.m_parameters.m_maxConvexHulls = m_Parameters.m_maxConvexHulls;
            vhacd.m_parameters.m_projectHullVertices = m_Parameters.m_projectHullVertices;

            return vhacd;
        }

        void VhacdGuiLayout()
        {
            // TODO: cleanup parameter assignment
            m_Parameters.m_concavity = EditorGUILayout.Slider(new GUIContent("Concavity", "Maximum Concavity"),
                (float)m_Parameters.m_concavity, 0, 1);
            m_Parameters.m_alpha =
                EditorGUILayout.Slider(new GUIContent("Alpha", "Bias toward clipping along symmetry planes"),
                    (float)m_Parameters.m_alpha, 0, 1);
            m_Parameters.m_beta =
                EditorGUILayout.Slider(new GUIContent("Beta", "Bias toward clipping along revolution axes"),
                    (float)m_Parameters.m_beta, 0, 1);
            m_Parameters.m_minVolumePerCH = EditorGUILayout.Slider(
                new GUIContent("MinVolumePerCH", "Adaptive sampling of the generated convex-hulls"),
                (float)m_Parameters.m_minVolumePerCH, 0, 0.01f);
            m_Parameters.m_resolution = (uint)EditorGUILayout.IntSlider(
                new GUIContent("Resolution", "Maximum voxels generated"), (int)m_Parameters.m_resolution, 10000,
                64000000);
            m_Parameters.m_maxNumVerticesPerCH = (uint)EditorGUILayout.IntSlider(
                new GUIContent("MaxNumVerticesPerCH", "Maximum triangles per convex-hull"),
                (int)m_Parameters.m_maxNumVerticesPerCH, 4, 1024);
            m_Parameters.m_planeDownsampling = (uint)EditorGUILayout.IntSlider(
                new GUIContent("PlaneDownsampling", "Granularity of the search for the \"best\" clipping plane"),
                (int)m_Parameters.m_planeDownsampling, 1, 16);
            m_Parameters.m_convexhullDownsampling = (uint)EditorGUILayout.IntSlider(
                new GUIContent("ConvexhullDownsampling",
                    "Precision of the convex-hull generation process during the clipping plane selection stage"),
                (int)m_Parameters.m_convexhullDownsampling, 1, 16);
            m_Parameters.m_pca = (uint)EditorGUILayout.IntSlider(
                new GUIContent("PCA", "Enable/disable normalizing the mesh before applying the convex decomposition"),
                (int)m_Parameters.m_pca, 0, 1);
            m_Parameters.m_mode = (uint)EditorGUILayout.IntSlider(
                new GUIContent("Mode", "0: voxel-based (recommended), 1: tetrahedron-based"), (int)m_Parameters.m_mode,
                0, 1);
            m_Parameters.m_convexhullApproximation = (uint)EditorGUILayout.IntSlider(
                new GUIContent("ConvexhullApproximation", ""), (int)m_Parameters.m_convexhullApproximation, 0, 1);
            m_Parameters.m_oclAcceleration = (uint)EditorGUILayout.IntSlider(new GUIContent("OclAcceleration", ""),
                (int)m_Parameters.m_oclAcceleration, 0, 1);
            m_Parameters.m_maxConvexHulls = (uint)EditorGUILayout.IntField("Max Convex Hulls", (int)m_Parameters.m_maxConvexHulls);
            m_Parameters.m_projectHullVertices = EditorGUILayout.Toggle(
                new GUIContent("ProjectHullVertices",
                    "This will project the output convex hull vertices onto the original source mesh to increase the floating point accuracy of the results"),
                m_Parameters.m_projectHullVertices);
        }
    }
}
