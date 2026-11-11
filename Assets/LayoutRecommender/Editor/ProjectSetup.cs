using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Rendering;
#if LR_HAS_URP
using UnityEngine.Rendering.Universal;
#endif

namespace LayoutRecommender.EditorTools
{
    /// <summary>
    /// 프로젝트를 처음 열 때 한 번 실행되는 자동 설정.
    /// 1) URP와 Input System 패키지가 없으면 에디터 버전에 맞는 버전으로 설치
    /// 2) URP 에셋을 만들어 Graphics/Quality 설정에 지정 (Built-in RP 지원 중단 경고 제거)
    /// 3) Active Input Handling을 새 Input System으로 전환 (Input Manager 지원 중단 경고 제거)
    /// 이미 설정된 항목은 건너뛴다.
    /// </summary>
    [InitializeOnLoad]
    static class ProjectSetup
    {
        const string UrpPackage = "com.unity.render-pipelines.universal";
        const string InputPackage = "com.unity.inputsystem";
        const string SettingsDir = "Assets/LayoutRecommender/Settings";
        const string Tag = "[LayoutRecommender 설정]";

        static AddAndRemoveRequest _request;

        static ProjectSetup()
        {
            EditorApplication.delayCall += Run;
        }

        [MenuItem("Tools/Layout Recommender/프로젝트 설정 다시 실행")]
        static void RunFromMenu()
        {
            SessionState.EraseBool("LR_PackagesRequested");
            SessionState.EraseBool("LR_InputAsked");
            Run();
        }

        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;

            bool hasUrp = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime") != null;
            bool hasInput = Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem") != null;

            if (!hasUrp || !hasInput)
            {
                InstallPackages(hasUrp, hasInput);
                return; // 설치 후 스크립트가 다시 컴파일되면서 이 함수가 다시 호출된다
            }

            SetupUrp();
            SetupInputHandler();
        }

        // ---------- 1) 패키지 설치 ----------

        static void InstallPackages(bool hasUrp, bool hasInput)
        {
            if (SessionState.GetBool("LR_PackagesRequested", false)) return;
            SessionState.SetBool("LR_PackagesRequested", true);

            var toAdd = new System.Collections.Generic.List<string>();
            if (!hasUrp) toAdd.Add(UrpPackage);
            if (!hasInput) toAdd.Add(InputPackage);

            Debug.Log($"{Tag} 패키지 설치 중: {string.Join(", ", toAdd)}");
            _request = Client.AddAndRemove(toAdd.ToArray(), null);
            EditorApplication.update += WaitForInstall;
        }

        static void WaitForInstall()
        {
            if (_request == null || !_request.IsCompleted) return;
            EditorApplication.update -= WaitForInstall;
            if (_request.Status == StatusCode.Success)
                Debug.Log($"{Tag} 패키지 설치 완료");
            else
                Debug.LogWarning($"{Tag} 패키지 설치 실패: {_request.Error?.message}. Window > Package Manager에서 직접 설치해주세요.");
            _request = null;
        }

        // ---------- 2) URP 지정 ----------

        static void SetupUrp()
        {
#if LR_HAS_URP
            if (GraphicsSettings.defaultRenderPipeline != null) return;

            Directory.CreateDirectory(SettingsDir);
            string assetPath = SettingsDir + "/URP_Asset.asset";
            string rendererPath = SettingsDir + "/URP_Renderer.asset";

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
                if (rendererData == null)
                {
                    rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rendererData, rendererPath);
                }
                asset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);

            AssetDatabase.SaveAssets();
            Debug.Log($"{Tag} URP 에셋을 만들어 기본 렌더 파이프라인으로 지정했습니다.");
#endif
        }

        // ---------- 3) Active Input Handling ----------

        static void SetupInputHandler()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (SessionState.GetBool("LR_InputAsked", false)) return;
            SessionState.SetBool("LR_InputAsked", true);

            var playerSettings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            if (playerSettings == null) return;
            var so = new SerializedObject(playerSettings);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null) return;

            prop.intValue = 1; // 0: Input Manager(구), 1: Input System(신), 2: 둘 다
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            if (EditorUtility.DisplayDialog("입력 시스템 전환",
                    "Active Input Handling을 새 Input System으로 바꿨습니다.\n적용하려면 에디터를 다시 시작해야 합니다.",
                    "지금 재시작", "나중에"))
            {
                EditorApplication.OpenProject(Directory.GetCurrentDirectory());
            }
#endif
        }
    }
}
