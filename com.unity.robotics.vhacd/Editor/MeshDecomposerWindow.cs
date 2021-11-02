using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MeshProcess
{
    public class MeshDecomposerWindow : EditorWindow
    {
        GameObject m_MeshObject;
        Object m_ObjectField;
        VHACD.Parameters m_Parameters;
        VhacdSettings m_Settings = new VhacdSettings();
        bool m_ShowBar;

        void Awake()
        {
            titleContent = new GUIContent("VHACD Generation Settings");
            m_Parameters = VhacdSettings.DefaultParameters();
        }

        void OnGUI()
        {
            m_Settings.GenerationMode =
                (VhacdSettings.Mode)EditorGUILayout.EnumPopup("Generation Mode", m_Settings.GenerationMode);

            // Asset directory selection
            switch (m_Settings.GenerationMode)
            {
                case VhacdSettings.Mode.SingleMode:
                    if (m_MeshObject != null)
                    {
                        GUILayout.Label(m_MeshObject != null ? m_MeshObject.name : "No mesh imported");
                    }
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Selected file:");
                    m_ObjectField = EditorGUILayout.ObjectField(m_ObjectField, typeof(Object), true);
                    if (m_ObjectField != null)
                    {
                        m_Settings.AssetPath = AssetDatabase.GetAssetPath(m_ObjectField);
                        m_Settings.FileType = Path.GetExtension(m_Settings.AssetPath).Equals(".fbx")
                            ? VhacdSettings.FileExtension.FBX
                            : VhacdSettings.FileExtension.Prefab;
                    }

                    EditorGUILayout.EndHorizontal();

                    break;
                case VhacdSettings.Mode.BatchMode:
                    // File extension selection
                    m_Settings.FileType =
                        (VhacdSettings.FileExtension)EditorGUILayout.EnumPopup("Select Filetype", m_Settings.FileType);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Asset directory:");
                    GUILayout.Label(!string.IsNullOrEmpty(m_Settings.AssetPath)
                        ? m_Settings.AssetPath.Substring(Application.dataPath.Length - "Assets".Length)
                        : "Select a directory");
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
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Mesh save directory
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Mesh save path:");
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

            if (m_Settings.GenerationMode == VhacdSettings.Mode.BatchMode)
            {
                // Bool settings
                m_Settings.OverwriteMeshComponents = GUILayout.Toggle(m_Settings.OverwriteMeshComponents,
                    "Overwrite any existing collider components?");
                if (m_Settings.FileType == VhacdSettings.FileExtension.Prefab)
                    m_Settings.OverwriteAssets =
                        GUILayout.Toggle(m_Settings.OverwriteAssets, "Overwrite existing assets?");

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
            }

            // VHACD decomposition parameters
            GUILayout.Label("VHACD Parameters");
            VhacdGuiLayout();

            if (m_Settings.GenerationMode == VhacdSettings.Mode.SingleMode)
            {
                // Generate
                if (!string.IsNullOrEmpty(m_Settings.AssetPath))
                {
                    var f = m_Settings.AssetPath;
                    if (m_MeshObject == null)
                    {
                        if (GUILayout.Button("Import Mesh"))
                        {
                            ImportMesh(f);
                        }
                    }

                    if (m_MeshObject != null)
                    {
                        if (GUILayout.Button("Generate!"))
                        {
                            GenerateColliders();
                        }

                        if (GUILayout.Button("Save"))
                        {
                            SavePrefab();
                            Debug.Log($"Saved {m_MeshObject.name} with the following parameters:\n{m_Parameters}");
                        }
                    }
                }

                if (m_MeshObject != null)
                {
                    if (GUILayout.Button("Reset Object"))
                    {
                        ClearWindow();
                    }
                }
            }
            else
            {
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
            }

            // Progress bar
            if (m_ShowBar && m_Settings.TotalAssets > 0 && m_Settings.GenerationMode == VhacdSettings.Mode.BatchMode)
            {
                UpdateProgressBar();
            }
        }

        void OnHierarchyChange()
        {
            if (m_MeshObject == null)
            {
                m_ObjectField = null;
                m_Settings.AssetPath = string.Empty;
                Selection.activeObject = null;
                DeleteDirectoryAndContents($"{m_Settings.MeshSavePath}/TEMP");
            }
        }

        void UpdateProgressBar()
        {
            var progress = m_Settings.AssetsConverted / m_Settings.TotalAssets;
            GUILayout.Label(
                $"Converting asset {m_Settings.AssetsConverted} of {m_Settings.TotalAssets} => {m_Settings.CurrentFile}");
            EditorGUI.ProgressBar(new Rect(3, 475, position.width - 6, 25), progress,
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
                    Debug.Log($"Generated {m_Settings.MeshCountTotal} meshes on {Path.GetFileName(f)}");
                }
            }
            else
            {
                Close();
            }
        }

        IEnumerator GenerateConvexMeshes(GameObject go, string prefabPath = "")
        {
            if (m_Settings.GenerationMode == VhacdSettings.Mode.BatchMode)
            {
                m_MeshObject = Instantiate(go);
                Selection.activeObject = m_MeshObject;
                EditorApplication.ExecuteMenuItem("Edit/Frame Selected");
                m_MeshObject.name = m_MeshObject.name.Substring(0, m_MeshObject.name.Length - "(Clone)".Length);
            }

            m_Settings.CurrentFile = m_MeshObject.name;
            var meshFilters = m_MeshObject.GetComponentsInChildren<MeshFilter>();

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
                    var path = $"{m_Settings.MeshSavePath}/{m_MeshObject.name}/{meshIndex}.asset";
                    if (m_Settings.GenerationMode == VhacdSettings.Mode.SingleMode)
                        path = $"{m_Settings.MeshSavePath}/TEMP/{m_MeshObject.name}/{meshIndex}.asset";
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

            if (m_Settings.GenerationMode == VhacdSettings.Mode.BatchMode)
            {
                var localPath = prefabPath;

                if (!m_Settings.OverwriteAssets || m_Settings.FileType != VhacdSettings.FileExtension.Prefab)
                {
                    Directory.CreateDirectory(m_Settings.AssetSavePath);

                    localPath = $"{m_Settings.AssetSavePath}/{m_MeshObject.name}.prefab";

                    // Make sure the file name is unique, in case an existing Prefab has the same name.
                    localPath = AssetDatabase.GenerateUniqueAssetPath(localPath);
                    Debug.Log($"Creating new prefab at: {localPath}");
                }
                else
                {
                    Debug.Log($"Updating prefab {localPath.Substring(Application.dataPath.Length)}");
                }

                // Save the Prefab.
                PrefabUtility.SaveAsPrefabAssetAndConnect(m_MeshObject, localPath, InteractionMode.AutomatedAction);
                DestroyImmediate(m_MeshObject);
                m_Settings.AssetsConverted++;
            }
            else
            {
                Debug.Log($"Generated {m_Settings.MeshCountTotal} meshes on {go.name}");
            }
        }

        void ClearWindow()
        {
            if (m_MeshObject != null)
            {
                DestroyImmediate(m_MeshObject);
            }
            OnHierarchyChange();
        }

        void SavePrefab()
        {
            var localPath = EditorUtility.SaveFilePanel(
                "Save prefab",
                m_Settings.AssetPath,
                m_MeshObject.name,
                "prefab");

            if (!string.IsNullOrEmpty(localPath))
            {
                Debug.Log($"Saving prefab at: {localPath}");

                // Move TEMP files to save location
                Directory.Move($"{m_Settings.MeshSavePath}/TEMP", $"{m_Settings.MeshSavePath}/Meshes");
                File.Delete($"{m_Settings.MeshSavePath}/TEMP.meta");
                // Save the Prefab.
                PrefabUtility.SaveAsPrefabAssetAndConnect(m_MeshObject, localPath, InteractionMode.AutomatedAction);
            }
        }

        void ImportMesh(string file)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(file);
            m_MeshObject = Instantiate(go);
            var templateFileName = m_MeshObject.name.Substring(0, m_MeshObject.name.Length - "(Clone)".Length);
            m_MeshObject.name = templateFileName;
            Selection.activeObject = m_MeshObject;
            EditorApplication.ExecuteMenuItem("Edit/Frame Selected");
        }

        void GenerateColliders()
        {
            DeleteDirectoryAndContents($"{m_Settings.MeshSavePath}/TEMP");
            ClearMeshColliders(m_MeshObject.transform);
            m_Settings.MeshCountChild = 0;
            m_Settings.MeshCountTotal = 0;
            EditorCoroutineUtility.StartCoroutine(GenerateConvexMeshes(m_MeshObject), this);
        }

        static void ClearMeshColliders(Transform t)
        {
            if (t.childCount > 0)
            {
                foreach (Transform child in t)
                {
                    ClearMeshColliders(child);
                }
            }

            var existingColliders = t.GetComponents<MeshCollider>();
            if (existingColliders.Length > 0)
            {
                foreach (var coll in existingColliders)
                {
                    DestroyImmediate(coll);
                }
            }
        }

        static void DeleteDirectoryAndContents(string path)
        {
            var di = new DirectoryInfo(path);

            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (var file in di.GetFiles())
            {
                file.Delete();
            }

            foreach (var dir in di.GetDirectories())
            {
                dir.Delete(true);
            }
            Directory.Delete(path);
            File.Delete($"{path}.meta");
        }

        VHACD ConfigureVhacd(GameObject go)
        {
            var vhacd = go.AddComponent<VHACD>();
            vhacd.m_parameters = m_Parameters;

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
            m_Parameters.m_maxConvexHulls =
                (uint)EditorGUILayout.IntField("Max Convex Hulls", (int)m_Parameters.m_maxConvexHulls);
            m_Parameters.m_projectHullVertices = EditorGUILayout.Toggle(
                new GUIContent("ProjectHullVertices",
                    "This will project the output convex hull vertices onto the original source mesh to increase the floating point accuracy of the results"),
                m_Parameters.m_projectHullVertices);
        }
    }
}
