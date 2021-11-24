using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeshProcess
{
    public static class MeshDecomposerExtensions
    {
        /// <summary>
        ///     Recursively searches a transform for a child by name
        /// </summary>
        /// <param name="parent">Parent object to search through</param>
        /// <param name="childName">String name of child to be searched</param>
        /// <returns>Transform of the found child, or null if not found</returns>
        public static Transform RecursiveFind(this Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name.Equals(childName)) return child;

                var found = RecursiveFind(child, childName);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        ///     Deletes all the content inside and the folder at that path, including the metafile
        /// </summary>
        /// <param name="path">Path of directory to delete</param>
        public static void DeleteDirectoryAndContents(string path)
        {
            var di = new DirectoryInfo(path);

            if (!Directory.Exists(path)) return;

            foreach (var file in di.GetFiles()) file.Delete();

            foreach (var dir in di.GetDirectories()) dir.Delete(true);

            Directory.Delete(path);
            File.Delete($"{path}.meta");

            AssetDatabase.Refresh();
        }
    }
}
