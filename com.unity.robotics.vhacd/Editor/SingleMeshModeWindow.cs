using System;
using System.Collections;
using System.IO;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MeshProcess
{
    public class SingleMeshModeWindow : EditorWindow
    {
        GameObject m_MeshObject;
        Object m_ObjectField;
        VHACD.Parameters m_Parameters;
        VhacdSettings m_Settings = new VhacdSettings();

        void Awake()
        {
            titleContent = new GUIContent("VHACD Single Mesh Settings");

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

        void OnDestroy()
        {
            ClearWindow();
        }

        void OnGUI()
        {
            if (m_MeshObject != null) GUILayout.Label(m_MeshObject != null ? m_MeshObject.name : "No mesh imported");

            // Asset directory selection
            EditorGUILayout.BeginHorizontal();
            m_ObjectField = EditorGUILayout.ObjectField(m_ObjectField, typeof(Object), true);
            if (m_ObjectField != null)
            {
                m_Settings.AssetPath = AssetDatabase.GetAssetPath(m_ObjectField);
                m_Settings.FromObjectField = true;
            }

            EditorGUILayout.EndHorizontal();

            // VHACD decomposition parameters
            GUILayout.Label("VHACD Parameters");
            VhacdGuiLayout();

            // Generate
            if (!string.IsNullOrEmpty(m_Settings.AssetPath))
            {
                var f = m_Settings.AssetPath;
                if (!m_Settings.FromObjectField)
                    f = m_Settings.AssetPath.Substring(Application.dataPath.Length - "Assets".Length);
                if (m_MeshObject == null)
                    if (GUILayout.Button("Import Mesh"))
                        ImportMesh(f);

                if (m_MeshObject != null)
                {
                    if (GUILayout.Button("Generate!")) GenerateColliders();

                    if (GUILayout.Button("Save"))
                    {
                        SavePrefab();
                        Debug.Log($"Saved {m_MeshObject.name} with the following parameters:\n{m_Parameters}");
                    }
                }
            }

            if (m_MeshObject != null)
                if (GUILayout.Button("Reset"))
                    ClearWindow();
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

        void ClearWindow()
        {
            if (m_MeshObject != null) DestroyImmediate(m_MeshObject);
            OnHierarchyChange();
        }

        void SavePrefab()
        {
            var localPath = m_Settings.AssetPath;
            if (!m_Settings.FromObjectField)
                localPath = EditorUtility.SaveFilePanel(
                    "Save prefab",
                    m_Settings.AssetPath.Substring(Application.dataPath.Length - "Assets".Length),
                    m_MeshObject.name,
                    "prefab");
            var templateFileName = m_MeshObject.name.Substring(0, m_MeshObject.name.Length - "(Clone)".Length);
            m_MeshObject.name = templateFileName;
            m_Settings.CurrentFile = templateFileName;
            Directory.CreateDirectory(m_Settings.AssetSavePath);
            Debug.Log($"Saving prefab at: {localPath}");

            // Save the Prefab.
            PrefabUtility.SaveAsPrefabAssetAndConnect(m_MeshObject, localPath, InteractionMode.AutomatedAction);
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
                foreach (Transform child in t)
                    ClearMeshColliders(child);

            var existingColliders = t.GetComponents<MeshCollider>();
            if (existingColliders.Length > 0)
                foreach (var coll in existingColliders)
                    DestroyImmediate(coll);
        }

        static void DeleteDirectoryAndContents(string path)
        {
            var di = new DirectoryInfo(path);

            if (!Directory.Exists(path)) return;
            foreach (var file in di.GetFiles()) file.Delete();
            foreach (var dir in di.GetDirectories()) dir.Delete(true);
            Directory.Delete(path);
            File.Delete($"{path}.meta");
        }

        IEnumerator GenerateConvexMeshes(GameObject go)
        {
            m_Settings.CurrentFile = m_MeshObject.name;
            var meshFilters = m_MeshObject.GetComponentsInChildren<MeshFilter>();

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
                    var path = $"{m_Settings.MeshSavePath}/TEMP/{m_MeshObject.name}/{meshIndex}.asset";
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

            Debug.Log($"Generated {m_Settings.MeshCountTotal} meshes on {go.name}");
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
                new GUIContent("PCA", "enable/disable normalizing the mesh before applying the convex decomposition"),
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
