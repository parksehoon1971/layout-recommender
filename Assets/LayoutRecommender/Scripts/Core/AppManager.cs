using LayoutRecommender.Data;
using LayoutRecommender.Screens;
using LayoutRecommender.UI;
using UnityEngine;

namespace LayoutRecommender.Core
{
    /// <summary>
    /// 씬에 아무것도 없어도 Play 시 앱이 뜨도록 자동 생성한다.
    /// 다른 씬에서 끄고 싶으면 씬에 DisableLayoutRecommender 이름의 오브젝트를 두면 된다.
    /// </summary>
    static class AppBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (GameObject.Find("DisableLayoutRecommender") != null) return;
            if (Object.FindFirstObjectByType<AppManager>() != null) return;
            new GameObject("LayoutRecommenderApp").AddComponent<AppManager>();
        }
    }

    /// <summary>앱 전체 상태와 화면 전환을 담당한다.</summary>
    public class AppManager : MonoBehaviour
    {
        public static AppManager Instance { get; private set; }

        /// <summary>현재 편집 중인 프로젝트 입력값</summary>
        public ProjectInput CurrentInput { get; set; } = ProjectInput.CreateSample();

        Canvas _canvas;
        ScreenBase _screen;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureCamera();
            UIFactory.EnsureEventSystem();
            _canvas = UIFactory.CreateCanvas("AppCanvas");
            _canvas.transform.SetParent(transform, false);

            ShowStart();
        }

        void EnsureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                go.tag = "MainCamera";
                cam = go.GetComponent<Camera>();
                go.transform.position = new Vector3(0, 60, -60);
                go.transform.rotation = Quaternion.Euler(45, 0, 0);
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Theme.Background;
        }

        public void ShowStart() => Show<StartScreen>();
        public void ShowInput() => Show<InputWizardScreen>();
        public void ShowLayout() => Show<LayoutScreen>();

        void Show<T>() where T : ScreenBase
        {
            if (_screen != null) Destroy(_screen.gameObject);
            var root = UIFactory.Rect(_canvas.transform, typeof(T).Name);
            UIFactory.Stretch(root);
            _screen = root.gameObject.AddComponent<T>();
            _screen.Build(root);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
