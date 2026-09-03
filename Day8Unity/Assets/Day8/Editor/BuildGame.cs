using System.IO;
using System.Linq;
using Day8.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Day8.Editor {

    public static class BuildGame {

        /// 创建主场景并保存
        public static void BuildScene() {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera));
            cam.GetComponent<Camera>().backgroundColor = new Color(0.055f, 0.063f, 0.075f, 1f);
            cam.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            cam.tag = "MainCamera";
            cam.AddComponent<AudioListener>();
            new GameObject("GameRoot").AddComponent<GameUI>();
            var path = "Assets/Scenes/Main.unity";
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[Day8] 场景已保存：" + path);
        }

        /// 场景 + Windows 64 位可执行文件，一键构建
        public static void BuildAll() {
            BuildScene();
            var scenes = new[] { "Assets/Scenes/Main.unity" };
            var player = EditorBuildSettings.scenes.Length > 0
                ? EditorBuildSettings.scenes
                : scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();
            if (EditorBuildSettings.scenes.Length == 0)
                EditorBuildSettings.scenes = player;
            var output = Path.GetFullPath(Path.Combine("..", "Build", "Day8", "Day8.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(
                EditorBuildSettings.scenes, output,
                BuildTarget.StandaloneWindows64, BuildOptions.None);
            Debug.Log("[Day8] 构建完成：" + output + "，结果：" + report.summary.result);
        }
    }
}
