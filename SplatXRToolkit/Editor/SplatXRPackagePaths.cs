using System.IO;
using UnityEditor.Compilation;

namespace SplatXR
{
    public static class SplatXRPackagePaths
    {
        private static string root;
        private static string rootFullPath;

        public static string Root
        {
            get
            {
                if (root == null) FindRoot();
                return root;
            }
        }

        public static string RootFullPath
        {
            get
            {
                if (rootFullPath == null) FindRoot();
                return rootFullPath;
            }
        }

        public static string GetAssetPath(string relativePath)
        {
            return $"{Root}/{relativePath}";
        }

        public static string GetFullPath(string relativePath)
        {
            return Path.Combine(RootFullPath, relativePath);
        }

        private static void FindRoot()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SplatXRPackagePaths).Assembly);
            if (packageInfo != null)
            {
                root = packageInfo.assetPath;
                rootFullPath = packageInfo.resolvedPath;
                return;
            }

            string asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("SplatXRToolkit.Editor");
            root = !string.IsNullOrEmpty(asmdefPath)
                ? Path.GetDirectoryName(Path.GetDirectoryName(asmdefPath)).Replace('\\', '/')
                : "Assets/SplatXRToolkit";
            rootFullPath = Path.GetFullPath(root);
        }
    }
}
