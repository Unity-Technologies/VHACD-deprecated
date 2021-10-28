using System;
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

        public FileExtension FileType { get; set; } = FileExtension.Prefab;
        public string AssetPath { get; set; } = string.Empty;
        public bool OverwriteAssets { get; set; } = true;
        public int AssetsConverted { get; set; }
        public int TotalAssets { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public int MeshCountChild { get; set; }
        public int MeshCountTotal { get; set; }

        public static string GetFileExtensionString(FileExtension ext)
        {
            return $".{ext.ToString().ToLower()}";
        }
    }
}
