using System;
using UnityEditor;
using UnityEngine;

namespace MeshProcess
{
    public class VhacdSettings
    {
        public enum FileExtension
        {
            Prefab,
            FBX
        }

        public enum Mode
        {
            SingleMode,
            BatchMode
        }

        public FileExtension FileType { get; set; } = FileExtension.Prefab;
        Mode m_GenerationMode = Mode.SingleMode;
        public Mode GenerationMode
        {
            get => m_GenerationMode;
            set
            {
                if (m_GenerationMode != value)
                {
                    m_GenerationMode = value;
                    if (OnModeChangeEvent != null)
                    {
                        OnModeChangeEvent();
                    }
                }
            }
        }
        public string AssetPath { get; set; } = string.Empty;
        public bool OverwriteMeshComponents { get; set; } = true;
        public bool OverwriteAssets { get; set; } = true;
        public string AssetSavePath { get; set; } = String.Empty;
        public string MeshSavePath { get; set; } = String.Empty;
        public bool Convex { get; set; } = true;
        public int AssetsConverted { get; set; }
        public int TotalAssets { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public int MeshCountChild { get; set; }
        public int MeshCountTotal { get; set; }
        public bool ChildDefaultOff { get; set; } = true;
        bool m_NewCollidersChild = true;
        public bool NewCollidersChild
        {
            get => m_NewCollidersChild;
            set
            {
                if (m_NewCollidersChild != value)
                {
                    m_NewCollidersChild = value;
                    if (OnHierarchyModeChangeEvent != null)
                    {
                        OnHierarchyModeChangeEvent();
                    }
                }
            }
        }

        // Delegate and Event for Child Object Mode change
        public delegate void OnHierarchyModeChange();
        public event OnHierarchyModeChange OnHierarchyModeChangeEvent;

        // Delegate and Event for GenerationMode change
        public delegate void OnModeChange();
        public event OnModeChange OnModeChangeEvent;

        public static VHACD.Parameters DefaultParameters()
        {
            return new VHACD.Parameters
            {
                m_resolution = 10000,
                m_concavity = 0.001,
                m_planeDownsampling = 4,
                m_convexhullDownsampling = 4,
                m_alpha = 0.05,
                m_beta = 0.05,
                m_pca = 0,
                m_mode = 0,
                m_maxNumVerticesPerCH = 64,
                m_minVolumePerCH = 0.0001,
                m_convexhullApproximation = 1,
                m_oclAcceleration = 0,
                m_maxConvexHulls = 1024,
                m_projectHullVertices = true
            };
        }

        /// <summary>
        /// Convert FileExtension enum to lowercase string.
        /// </summary>
        /// <param name="ext">FileExtension enum to convert</param>
        /// <returns>Extension without prefix, e.g. "fbx"</returns>
        public static string GetFileExtensionString(FileExtension ext)
        {
            return $"{ext.ToString().ToLower()}";
        }
    }
}
