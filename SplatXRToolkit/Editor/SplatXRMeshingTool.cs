using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Process = System.Diagnostics.Process;

namespace SplatXR
{
    public static class SplatXRMeshingTool
    {
        public const string DownloadUrl = "https://splatxr.github.io/docs/installation.html";

        public const string PackageFolder = "Runtime/SplatXRBuildingBlocks/Physics";

        public static string FileName
        {
            get
            {
    #if UNITY_EDITOR_OSX
                return "splatxr_meshing_mac";
    #elif UNITY_EDITOR_WIN
                return "splatxr_meshing.exe";
    #else
                return null;
    #endif
            }
        }

        public static string FindExecutable()
        {
            string fileName = FileName;
            if (fileName == null) return null;

            string packagePath = SplatXRPackagePaths.GetFullPath(Path.Combine(PackageFolder, fileName));
            if (File.Exists(packagePath)) return packagePath;

            string assetsPath = Path.Combine(Application.dataPath, fileName);
            if (File.Exists(assetsPath)) return assetsPath;

            return null;
        }

        public static string MissingMessage
        {
            get
            {
                if (FileName == null)
                {
                    return "The SplatXR meshing tool is only available for Windows and macOS.";
                }
                return $"The SplatXR meshing tool ({FileName}) wasn't found.\n\n" +
                       $"It isn't included in the package, so download it from {DownloadUrl} " +
                       $"and place it in the package's {PackageFolder} folder or in your project's Assets folder.";
            }
        }

        public static void DrawNotice()
        {
            if (FindExecutable() != null)
            {
                GUILayout.Label("✓ Meshing tool found in this project", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.HelpBox(MissingMessage, MessageType.Warning);

            if (GUILayout.Button("Open Meshing Tool Download Page"))
            {
                Application.OpenURL(DownloadUrl);
            }
        }

        private static Process runningProcess;
        private static int progressId = -1;
        private static bool cancelRequested;
        private static System.Action<bool> onFinished;
        private static readonly StringBuilder standardOutput = new StringBuilder();
        private static readonly StringBuilder standardError = new StringBuilder();
        private static string latestStatus;
        private static string shownStatus;

        public static bool IsRunning => runningProcess != null;

        public const string RunningNotice = "Generating physics mesh. This usually takes 10-15 seconds.";

        public static bool Run(string exePath, string arguments, string description, System.Action<bool> finished)
        {
            if (IsRunning)
            {
                Debug.LogError("[SplatXR] A physics mesh is already being generated. Wait for it to finish, or cancel it from the progress bar in the bottom-right of the editor.");
                return false;
            }

            var process = new Process();
            process.StartInfo.FileName = exePath;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            lock (standardOutput)
            {
                standardOutput.Clear();
                standardError.Clear();
                latestStatus = "Starting meshing tool";
            }
            shownStatus = null;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (standardOutput)
                {
                    standardOutput.AppendLine(e.Data);
                    if (!string.IsNullOrWhiteSpace(e.Data)) latestStatus = e.Data;
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (standardOutput) standardError.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            runningProcess = process;
            onFinished = finished;
            cancelRequested = false;

            progressId = Progress.Start("Splat Physics", description, Progress.Options.Indefinite);
            Progress.RegisterCancelCallback(progressId, () =>
            {
                cancelRequested = true;
                KillProcess();
                return true;
            });

            EditorApplication.LockReloadAssemblies();
            EditorApplication.playModeStateChanged += BlockPlayMode;
            EditorApplication.quitting += KillProcess;
            EditorApplication.update += Poll;

            Debug.Log($"[SplatXR] {description}. This usually takes 10-15 seconds.");
            return true;
        }

        private static void Poll()
        {
            if (runningProcess == null) return;

            string status;
            lock (standardOutput) status = latestStatus;
            if (status != shownStatus)
            {
                shownStatus = status;
                Progress.SetDescription(progressId, status);
            }

            if (!runningProcess.HasExited) return;

            runningProcess.WaitForExit();
            int exitCode = runningProcess.ExitCode;
            runningProcess.Dispose();
            runningProcess = null;

            string output, error;
            lock (standardOutput)
            {
                output = standardOutput.ToString().Trim();
                error = standardError.ToString().Trim();
            }

            EditorApplication.update -= Poll;
            EditorApplication.playModeStateChanged -= BlockPlayMode;
            EditorApplication.quitting -= KillProcess;
            EditorApplication.UnlockReloadAssemblies();

            bool success = !cancelRequested && exitCode == 0;
            Progress.Finish(progressId, cancelRequested ? Progress.Status.Canceled : success ? Progress.Status.Succeeded : Progress.Status.Failed);
            progressId = -1;

            if (cancelRequested)
            {
                Debug.LogWarning("[SplatXR] Physics mesh generation was canceled.");
            }
            else if (exitCode == 137)
            {
                Debug.LogError("Splat PLY meshing failed: Process ran out of memory (exit code 137).\n" +
                    "Try reducing the complexity:\n" +
                    "- Increase 'Mesh Threshold' (e.g., 0.7 or higher for Object type)\n" +
                    "- Decrease 'Gaussian Mesh Scale'\n" +
                    "- Use a smaller PLY file with fewer Gaussian splats\n" +
                    "- Close other applications to free up RAM");
            }
            else if (!success)
            {
                Debug.LogError($"Splat PLY meshing failed with exit code: {exitCode}\n{error}\n\n{output}");
            }

            var callback = onFinished;
            onFinished = null;
            try
            {
                callback?.Invoke(success);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void BlockPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;

            EditorApplication.isPlaying = false;
            Debug.LogWarning("[SplatXR] Can't enter Play mode while a physics mesh is being generated. Wait for it to finish, or cancel it from the progress bar in the bottom-right of the editor.");
        }

        private static void KillProcess()
        {
            try
            {
                if (runningProcess != null && !runningProcess.HasExited)
                {
                    runningProcess.Kill();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SplatXR] Could not stop the meshing tool: {e.Message}");
            }
        }
    }
}
