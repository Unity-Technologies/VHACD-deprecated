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
        EditorCoroutine m_ColliderCoroutine;
        bool m_ShowBar;
        bool m_RunningGenerator;

        void Awake()
        {
            titleContent = new GUIContent("VHACD Generation Settings");
            m_Parameters = VhacdSettings.DefaultParameters();
            m_Settings.OnModeChangeEvent += ClearWindow;
        }

        void OnDestroy()
        {
            ClearWindow();
            m_Settings.OnModeChangeEvent -= ClearWindow;
        }

        void OnGUI()
        {
            m_Settings.GenerationMode =
                (VhacdSettings.Mode)EditorGUILayout.EnumPopup("Generation Mode", m_Settings.GenerationMode);

            // Asset directory selection
            switch (m_Settings.GenerationMode)
            {
                case VhacdSettings.Mode.SingleMode:
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Selected file:");
                    if (m_ObjectField == null)
                    {
                        m_ObjectField = EditorGUILayout.ObjectField(m_ObjectField, typeof(Object), true);
                    }
                    else
                    {
                        GUILayout.Label(m_ObjectField.name);
                        m_Settings.AssetPath = AssetDatabase.GetAssetPath(m_ObjectField);
                        m_Settings.FileType = Path.GetExtension(m_Settings.AssetPath).Equals(".fbx")
                            ? VhacdSettings.FileExtension.FBX
                            : VhacdSettings.FileExtension.Prefab;

                        if (!string.IsNullOrEmpty(m_Settings.AssetPath) &&
                            string.IsNullOrEmpty(m_Settings.MeshSavePath))
                        {
                            m_Settings.MeshSavePath =
                                $"{Path.GetDirectoryName(m_Settings.AssetPath)}/VHACD/Collision Meshes/{Path.GetFileNameWithoutExtension(m_Settings.AssetPath)}";
                            m_Settings.AssetSavePath = $"{Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length - 1)}/{Path.GetDirectoryName(m_Settings.AssetPath)}";
                        }
                    }

                    EditorGUILayout.EndHorizontal();

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
                            m_Settings.MeshSavePath =
                                $"{tmpMeshSavePath.Substring(Application.dataPath.Length - "Assets".Length)}/VHACD/Collision Meshes";
                        }
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
                        m_Settings.AssetSavePath = m_Settings.AssetPath.Substring(Application.dataPath.Length - "Assets".Length);
                    }

                    EditorGUILayout.EndHorizontal();

                    // Bool settings
                    m_Settings.OverwriteMeshComponents = GUILayout.Toggle(m_Settings.OverwriteMeshComponents,
                        "Overwrite any existing collider components?");
                    if (m_Settings.FileType == VhacdSettings.FileExtension.Prefab)
                    {
                        m_Settings.OverwriteAssets =
                            GUILayout.Toggle(m_Settings.OverwriteAssets, "Overwrite existing assets?");
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

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
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
                    GUI.enabled = m_MeshObject == null && !m_RunningGenerator;
                    if (GUILayout.Button("Import Mesh"))
                    {
                        ImportMesh(f);
                    }

                    GUI.enabled = m_MeshObject != null && !m_RunningGenerator;
                    if (GUILayout.Button("Generate!"))
                    {
                        GenerateColliders();
                    }

                    GUI.enabled = m_MeshObject != null && !m_RunningGenerator;
                    if (GUILayout.Button("Save"))
                    {
                        if (SavePrefab())
                        {
                            Debug.Log($"Saved {m_MeshObject.name} with the following parameters:\n{m_Parameters}");
                        }
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

                    GUI.enabled = true;
                    if (GUILayout.Button("Generate!"))
                    {
                        m_ColliderCoroutine = EditorCoroutineUtility.StartCoroutine(OpenFiles(fileEnumerable), this);
                        m_RunningGenerator = true;
                    }
                }
                else
                {
                    GUILayout.Label("Please select a directory!");
                    m_ShowBar = false;
                }
            }

            // Clear out object button
            GUI.enabled = (m_ObjectField != null || m_MeshObject != null) && !m_RunningGenerator;
            if (GUILayout.Button("Clear Object"))
            {
                ClearWindow();
            }

            // Reset button
            GUI.enabled = !m_RunningGenerator;
            if (GUILayout.Button("Reset VHACD Parameters"))
            {
                m_Parameters = VhacdSettings.DefaultParameters();
            }

            // Cancel button
            GUI.enabled = m_RunningGenerator;
            if (GUILayout.Button("Cancel Generation"))
            {
                m_RunningGenerator = false;
                EditorCoroutineUtility.StopCoroutine(m_ColliderCoroutine);
                ClearWindow();
            }

            // Progress bar
            GUI.enabled = true;
            if (m_ShowBar && m_Settings.TotalAssets > 0 && m_Settings.GenerationMode == VhacdSettings.Mode.BatchMode)
            {
                UpdateProgressBar();
            }
        }

        /// <summary>
        ///     Resets paths and fields for this window when the scene is modified manually.
        /// </summary>
        void OnHierarchyChange()
        {
            if (m_Settings.GenerationMode == VhacdSettings.Mode.SingleMode) ResetGenerator();
        }

        /// <summary>
        ///     Resets paths and fields for this window. Also deletes and temp directories and contents.
        /// </summary>
        void ResetGenerator()
        {
            if (m_MeshObject == null)
            {
                DeleteDirectoryAndContents($"{m_Settings.MeshSavePath}/TEMP");
                m_ObjectField = null;
                m_Settings.AssetPath = string.Empty;
                m_Settings.MeshSavePath = string.Empty;
            }
        }

        /// <summary>
        ///     In Batch Mode; Helper function to update Progress bar in this window
        /// </summary>
        void UpdateProgressBar()
        {
            var progress = m_Settings.AssetsConverted / m_Settings.TotalAssets;
            GUILayout.Label(
                $"Converting asset {m_Settings.AssetsConverted} of {m_Settings.TotalAssets} => {m_Settings.CurrentFile}");
            EditorGUI.ProgressBar(new Rect(3, 500, position.width - 6, 25), progress,
                $"{m_Settings.AssetsConverted}/{m_Settings.TotalAssets} Assets Converted");
            if (Math.Abs(progress - 1) < 0.01f)
            {
                m_Settings.TotalAssets = 0;
                m_ShowBar = false;
            }
        }

        /// <summary>
        ///     In Batch Mode; enumerate over files and generate mesh colliders.
        /// </summary>
        /// <param name="fileEnumerable">File enumerable to iterate through</param>
        IEnumerator OpenFiles(IEnumerable<string> fileEnumerable)
        {
            m_Settings.AssetsConverted = 0;

            if (m_Settings.TotalAssets > 0)
            {
                m_ShowBar = true;

                foreach (var filePath in fileEnumerable)
                {
                    var f = filePath.Substring(Application.dataPath.Length - "Assets".Length);
                    m_Settings.MeshSavePath =
                        $"{Path.GetDirectoryName(f)}/VHACD/Collision Meshes/{Path.GetFileNameWithoutExtension(f)}";
                    m_Settings.AssetSavePath = Path.GetDirectoryName(f);
                    var obj = AssetDatabase.LoadAssetAtPath<GameObject>(f);

                    m_Settings.MeshCountChild = 0;
                    m_Settings.MeshCountTotal = 0;
                    m_RunningGenerator = true;
                    m_ColliderCoroutine =
                        EditorCoroutineUtility.StartCoroutine(GenerateConvexMeshes(obj, filePath), this);
                    yield return m_ColliderCoroutine;
                    Debug.Log($"Generated {m_Settings.MeshCountTotal} meshes on {Path.GetFileName(f)}");
                }

                m_RunningGenerator = false;
            }
            else
            {
                Close();
            }
        }

        /// <summary>
        ///     The core function to generate and assign mesh colliders.
        /// </summary>
        /// <param name="go">GameObject to convert meshes</param>
        /// <param name="prefabPath">In Batch Mode; path of file to overwrite</param>
        IEnumerator GenerateConvexMeshes(GameObject go, string prefabPath = "")
        {
            m_Settings.MeshCountChild = 0;

            // Instantiate and focus on object in Batch Mode
            if (m_Settings.GenerationMode == VhacdSettings.Mode.BatchMode)
            {
                m_MeshObject = Instantiate(go);
                Selection.activeObject = m_MeshObject;
                EditorApplication.ExecuteMenuItem("Edit/Frame Selected");
                m_MeshObject.name = m_MeshObject.name.Substring(0, m_MeshObject.name.Length - "(Clone)".Length);
            }

            m_Settings.CurrentFile = m_MeshObject.name;
            var meshFilters = m_MeshObject.GetComponentsInChildren<MeshFilter>();

            // Generate and assign colliders
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
                        foreach (var coll in existingColliders)
                        {
                            DestroyImmediate(coll);
                        }
                    }
                }

                var decomposer = ConfigureVhacd(child);
                yield return new WaitForEndOfFrame();
                var colliderMeshes = decomposer.GenerateConvexMeshes(meshFilter.sharedMesh);
                yield return new WaitForEndOfFrame();
                foreach (var collider in colliderMeshes)
                {
                    // Assign and save generated mesh
                    meshIndex++;
                    var path = $"{m_Settings.MeshSavePath}/{m_MeshObject.name}_{meshIndex}.asset";
                    if (m_Settings.GenerationMode == VhacdSettings.Mode.SingleMode)
                        path = $"{m_Settings.MeshSavePath}/TEMP/{m_MeshObject.name}_{meshIndex}.asset";
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
            }

            m_Settings.MeshCountTotal += m_Settings.MeshCountChild;

            // In Batch Mode; save prefab
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
                    Debug.Log($"Updating prefab {localPath.Substring(Application.dataPath.Length + 1)}");
                }

                // Save the Prefab.
                PrefabUtility.SaveAsPrefabAssetAndConnect(m_MeshObject, localPath, InteractionMode.AutomatedAction);
                DestroyImmediate(m_MeshObject);
                m_Settings.AssetsConverted++;
            }
            else
            {
                Debug.Log($"Generated {m_Settings.MeshCountTotal} meshes on {go.name}");
                m_RunningGenerator = false;

                // TODO: refresh scene view without reactivating object
                go.SetActive(false);
                yield return new WaitForEndOfFrame();
                go.SetActive(true);
            }
        }

        /// <summary>
        ///     Removes instantiated mesh and resets values for this window.
        /// </summary>
        void ClearWindow()
        {
            if (m_MeshObject != null)
            {
                DestroyImmediate(m_MeshObject);
            }

            ResetGenerator();
        }

        /// <summary>
        ///     For Single Mode; Saves the prefab to the chosen location. Also changes the TEMP files to a non-temp location.
        /// </summary>
        bool SavePrefab()
        {
            var localPath = EditorUtility.SaveFilePanel(
                "Save prefab",
                m_Settings.FileType == VhacdSettings.FileExtension.Prefab ? m_Settings.AssetPath : Path.GetDirectoryName(m_Settings.AssetPath),
                m_MeshObject.name,
                "prefab");

            if (!string.IsNullOrEmpty(localPath))
            {
                Debug.Log($"Saving prefab at: {localPath.Substring(Application.dataPath.Length - "Assets".Length)}");

                // Move TEMP files to save location
                if (Directory.Exists(m_Settings.MeshSavePath))
                {
                    var files = Directory.EnumerateFiles($"{m_Settings.MeshSavePath}/TEMP",
                        "*", SearchOption.AllDirectories);

                    foreach (var s in files)
                    {
                        var fileName = Path.GetFileName(s);
                        var destFile = Path.Combine(m_Settings.MeshSavePath, fileName);
                        if (File.Exists(destFile))
                        {
                            File.Delete(destFile);
                        }

                        File.Move(s, destFile);
                    }
                }
                else
                {
                    Directory.Move($"{m_Settings.MeshSavePath}/TEMP", $"{m_Settings.MeshSavePath}");
                }

                File.Delete($"{m_Settings.MeshSavePath}/TEMP.meta");

                // Save the Prefab.
                PrefabUtility.SaveAsPrefabAssetAndConnect(m_MeshObject, localPath, InteractionMode.AutomatedAction);
                AssetDatabase.Refresh();
                return true;
            }

            return false;
        }

        /// <summary>
        ///     For Single Mode; Loads GameObject from file and instantiates it into the scene
        /// </summary>
        /// <param name="file">Path of the file to load in project</param>
        void ImportMesh(string file)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(file);
            m_MeshObject = Instantiate(go);
            var templateFileName = m_MeshObject.name.Substring(0, m_MeshObject.name.Length - "(Clone)".Length);
            m_MeshObject.name = templateFileName;
            Selection.activeObject = m_MeshObject;
            EditorApplication.ExecuteMenuItem("Edit/Frame Selected");
        }

        /// <summary>
        ///     Clears any existing colliders and generated meshes and restarts the generation process
        /// </summary>
        void GenerateColliders()
        {
            DeleteDirectoryAndContents($"{m_Settings.MeshSavePath}/TEMP");
            ClearMeshColliders(m_MeshObject.transform);
            m_Settings.MeshCountChild = 0;
            m_Settings.MeshCountTotal = 0;
            m_ColliderCoroutine = EditorCoroutineUtility.StartCoroutine(GenerateConvexMeshes(m_MeshObject), this);
            m_RunningGenerator = true;
        }

        /// <summary>
        ///     Recursively remove all existing Mesh Colliders on the transform
        /// </summary>
        /// <param name="t">Transform to recurse on</param>
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

        /// <summary>
        ///     Deletes all the content inside and the folder at that path, including the metafile
        /// </summary>
        /// <param name="path">Path of directory to delete</param>
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

            AssetDatabase.Refresh();
        }

        /// <summary>
        ///     Creates and assigns the VHACD component to a GameObject
        /// </summary>
        /// <param name="go">The GameObject to add the configured VHACD component</param>
        /// <returns>The added VHACD component</returns>
        VHACD ConfigureVhacd(GameObject go)
        {
            var vhacd = go.AddComponent<VHACD>();
            vhacd.m_parameters = m_Parameters;

            return vhacd;
        }

        /// <summary>
        ///     Create editor sliders for VHACD parameters with appropriate tooltips
        /// </summary>
        void VhacdGuiLayout()
        {
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
            // TODO: Investigate this parameter
            // m_Parameters.m_oclAcceleration = (uint)EditorGUILayout.IntSlider(new GUIContent("OclAcceleration", ""),
            //     (int)m_Parameters.m_oclAcceleration, 0, 1);
            m_Parameters.m_maxConvexHulls =
                (uint)EditorGUILayout.IntField("Max Convex Hulls per MeshRenderer", (int)m_Parameters.m_maxConvexHulls);
            m_Parameters.m_projectHullVertices = EditorGUILayout.Toggle(
                new GUIContent("ProjectHullVertices",
                    "This will project the output convex hull vertices onto the original source mesh to increase the floating point accuracy of the results"),
                m_Parameters.m_projectHullVertices);
        }
    }
}
