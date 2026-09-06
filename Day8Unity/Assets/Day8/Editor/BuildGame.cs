using System.IO;
using System.Linq;
using Day8.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Day8.Editor {

    public static class BuildGame {

        const string MainScenePath = "Assets/Scenes/Main.unity";

        /// 明确重建程序化主场景。该操作会覆盖现有主场景。
        [MenuItem("DAY 8/Rebuild Main Scene")]
        public static void BuildScene() {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera));
            cam.GetComponent<Camera>().backgroundColor = new Color(0.055f, 0.063f, 0.075f, 1f);
            cam.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            cam.tag = "MainCamera";
            cam.AddComponent<AudioListener>();
            new GameObject("GameRoot").AddComponent<GameUI>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, MainScenePath);
            Debug.Log("[Day8] 场景已保存：" + MainScenePath);
        }

        /// Windows 64 位可执行文件；仅在主场景缺失时自动创建，避免覆盖手工调整。
        [MenuItem("DAY 8/Build Windows")]
        public static void BuildAll() {
            ApplyWindowSettings();
            if (!File.Exists(MainScenePath)) BuildScene();
            var scenes = new[] { MainScenePath };
            var player = EditorBuildSettings.scenes.Length > 0
                ? EditorBuildSettings.scenes.Where(s => s.enabled).ToArray()
                : scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();
            if (player.Length == 0) {
                player = scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();
                EditorBuildSettings.scenes = player;
            }
            var output = Path.GetFullPath(Path.Combine("..", "Build", "Day8", "Day8.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(
                player, output,
                BuildTarget.StandaloneWindows64, BuildOptions.None);
            Debug.Log("[Day8] 构建完成：" + output + "，结果：" + report.summary.result);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("DAY 8 Windows 构建失败：" + report.summary.result);
        }

        /// 窗口化启动：带边框、可调整大小、默认 1600×900。
        [MenuItem("DAY 8/Apply Windowed Player Settings")]
        public static void ApplyWindowSettings() {
            PlayerSettings.productName = "DAY 8 / 第八天";
            PlayerSettings.companyName = "Day8 Observation Lab";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.runInBackground = true;
            Debug.Log("[Day8] PlayerSettings 已应用：窗口化 1600×900，可调整大小");
        }
    }
}
