// Headless WebGL build: Unity -batchmode -projectPath . -executeMethod Territory.Editor.BuildScript.BuildWebGL
// Output dir comes from UNITY_WEBGL_OUT (default WebGLBuild/).
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Territory.Editor
{
    public static class BuildScript
    {
        public static void BuildWebGL()
        {
            string outDir = Environment.GetEnvironmentVariable("UNITY_WEBGL_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = "WebGLBuild";
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.runInBackground = true;
            PlayerSettings.companyName = "Territory";
            PlayerSettings.productName = "Territory";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"WebGL build result: {report.summary.result}");
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        // Fast dev-preview target: Linux standalone with the Mono backend
        // (no IL2CPP/emscripten), built in minutes and run natively.
        // Output dir comes from UNITY_LINUX_OUT (default LinuxPreview/).
        public static void BuildLinux()
        {
            string outDir = Environment.GetEnvironmentVariable("UNITY_LINUX_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = "LinuxPreview";
            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.companyName = "Territory";
            PlayerSettings.productName = "Territory";
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = System.IO.Path.Combine(outDir, "Territory.x86_64"),
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.Development,
            };
            BuildReport linuxReport = BuildPipeline.BuildPlayer(options);
            Debug.Log($"Linux build result: {linuxReport.summary.result}");
            if (linuxReport.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
