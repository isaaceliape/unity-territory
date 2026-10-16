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
    }
}
