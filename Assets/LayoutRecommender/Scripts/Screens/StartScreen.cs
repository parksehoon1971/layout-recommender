using LayoutRecommender.Data;
using LayoutRecommender.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LayoutRecommender.Screens
{
    /// <summary>시작화면: 새 프로젝트 / 이전 입력 불러오기 / 종료</summary>
    public class StartScreen : ScreenBase
    {
        public override void Build(RectTransform root)
        {
            var bg = UIFactory.Panel(root, "Background", Theme.Background);
            UIFactory.Stretch(bg);

            // 중앙 카드
            var card = UIFactory.Panel(root, "Card", Theme.Panel);
            UIFactory.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-420, -300), new Vector2(420, 300));
            UIFactory.VLayout(card.gameObject, 18, new RectOffset(60, 60, 60, 50), TextAnchor.UpperCenter);

            var title = UIFactory.Label(card, "물류센터 레이아웃 추천 시스템", 46, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Size(title, height: 70);

            var sub = UIFactory.Label(card, "화물 특성과 입출고량을 입력하면 설비 배치안을 만들고\n시뮬레이션으로 비교해 추천합니다.", 22, Theme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Size(sub, height: 70);

            var spacer = UIFactory.Rect(card, "Spacer");
            UIFactory.Size(spacer, height: 20);

            var newBtn = UIFactory.CreateButton(card, "새 프로젝트 시작", () =>
            {
                App.CurrentInput = ProjectInput.CreateSample();
                App.ShowInput();
            }, Theme.Accent, 26);
            UIFactory.Size(newBtn, height: 64);

            var loadBtn = UIFactory.CreateButton(card, "이전 입력 불러오기", () =>
            {
                var loaded = InputStorage.Load();
                if (loaded != null) App.CurrentInput = loaded;
                App.ShowInput();
            }, Theme.PanelLight, 24);
            UIFactory.Size(loadBtn, height: 56);
            loadBtn.interactable = InputStorage.Exists;

            var quitBtn = UIFactory.CreateButton(card, "종료", () => App.Quit(), Theme.PanelLight, 24);
            UIFactory.Size(quitBtn, height: 56);

            var version = UIFactory.Label(root, "Prototype v0.1", 18, Theme.TextDim, TextAnchor.LowerRight);
            UIFactory.Anchor(version.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-320, 16), new Vector2(-20, 50));
        }
    }
}
