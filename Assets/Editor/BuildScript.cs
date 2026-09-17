using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Whiteboard.EditorTools
{
    /// <summary>
    /// requirements.md 27・29章。シーン・UI・オブジェクトはすべてコードで生成し、
    /// Unity Editor の GUI 操作を不要とする。PlayerSettings もここでコードから設定する。
    /// </summary>
    public static class BuildScript
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string WebGLTemplateName = "Whiteboard";
        private const string OutputDir = "Build/WebGL";

        [MenuItem("Whiteboard/Build WebGL")]
        public static void BuildWebGL()
        {
            EnsureBootstrapScene();
            ConfigurePlayerSettings();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log("Build result: " + summary.result + " (errors=" + summary.totalErrors + ", warnings=" + summary.totalWarnings + ", size=" + summary.totalSize + " bytes)");

            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// シーンはコードで生成する。GameObject等の手動配置は行わず、Boot.cs の
        /// RuntimeInitializeOnLoadMethod がプレイ開始時に自己生成する前提の「空シーン」を用意する。
        /// </summary>
        private static void EnsureBootstrapScene()
        {
            var dir = Path.GetDirectoryName(ScenePath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(ScenePath))
            {
                RegisterScene();
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterScene();
        }

        private static void RegisterScene()
        {
            var scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorBuildSettings.scenes = scenes;
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "RictaWorks";
            PlayerSettings.productName = "共同編集ホワイトボード（デモ版）";

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.WebGL, "jp.rictaworks.sharedwhiteboarddemo");

            // requirements.md 30章：WebGL 2.0対応ブラウザのみを対象とする。
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });

            PlayerSettings.WebGL.template = "PROJECT:" + WebGLTemplateName;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

            // requirements.md 16章：解像度はブラウザウィンドウに追随する（固定解像度は使わない）。
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.resizableWindow = true;

            PlayerSettings.colorSpace = ColorSpace.Gamma;
        }
    }
}
