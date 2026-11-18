using System.IO;
using LayoutRecommender.RL;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LayoutRecommender.EditorTools
{
    /// <summary>
    /// 강화학습용 씬을 메뉴 한 번으로 만든다.
    /// - 학습 씬: 에이전트(배치 영역) 여러 개를 격자로 늘어놓아 동시에 경험을 모은다.
    /// - 시연 씬: 앱에서 저장한 입력값으로 한 개 영역을 천천히 배치해 보여준다. 학습된 .onnx를 여기에 넣는다.
    /// </summary>
    static class LayoutRLSceneBuilder
    {
        const string SceneDir = "Assets/LayoutRecommender/RL/Scenes";
        const float Gap = 4f;

        [MenuItem("Tools/Layout Recommender/강화학습/학습 씬 만들기")]
        static void BuildTrainingScene()
        {
            Build("LayoutRL_Training", columns: 4, rows: 3, demo: false);
        }

        [MenuItem("Tools/Layout Recommender/강화학습/시연 씬 만들기")]
        static void BuildDemoScene()
        {
            Build("LayoutRL_Demo", columns: 1, rows: 1, demo: true);
        }

        static void Build(string sceneName, int columns, int rows, bool demo)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 앱 UI(AppManager) 자동 실행 끄기
            new GameObject("DisableLayoutRecommender");

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(60f, -30f, 0f);

            float areaW = LayoutProblem.GridW + Gap, areaD = LayoutProblem.GridH + Gap;
            float totalW = columns * areaW, totalD = rows * areaD;

            var root = new GameObject(demo ? "DemoArea" : "TrainingAreas").transform;
            int n = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                {
                    var go = new GameObject($"LayoutAgent_{n++}");
                    go.transform.SetParent(root, false);
                    go.transform.position = new Vector3((c + 0.5f) * areaW - totalW * 0.5f, 0f, totalD * 0.5f - (r + 0.5f) * areaD);

                    var agent = go.AddComponent<LayoutPlacementAgent>(); // BehaviorParameters·LayoutView·센서는 자동 추가
                    LayoutPlacementAgent.ConfigureBehavior(go.GetComponent<BehaviorParameters>());
                    if (demo)
                    {
                        agent.Source = LayoutPlacementAgent.ProblemSource.ProjectInput;
                        agent.StepInterval = 0.4f;
                        agent.EndPause = 3f;
                        agent.CompareWithGreedyEvery = 1;
                    }
                }

            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera";
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(totalD * 0.5f, totalW * 0.5f / (16f / 9f)) + 1f;
            cam.transform.position = new Vector3(0f, 50f, 0f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.12f, 0.15f);
            cam.farClipPlane = 200f;

            Directory.CreateDirectory(SceneDir);
            string path = $"{SceneDir}/{sceneName}.unity";
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();
            Debug.Log($"[LayoutRL] 씬을 만들었습니다: {path}" + (demo
                ? "  ·  학습된 .onnx를 LayoutAgent_0의 Behavior Parameters > Model에 넣으면 RL 정책으로, 비워 두면 탐욕 기준선으로 배치합니다."
                : "  ·  터미널에서 mlagents-learn을 실행한 뒤 Play를 누르세요."));
        }
    }
}
