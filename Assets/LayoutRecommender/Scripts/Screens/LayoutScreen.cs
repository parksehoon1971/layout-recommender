using System;
using System.Collections.Generic;
using System.Text;
using LayoutRecommender.Data;
using LayoutRecommender.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LayoutRecommender.Screens
{
    /// <summary>
    /// 레이아웃 생성 화면 (HUD 틀).
    /// 좌측: 카테고리별 설비 팔레트 / 우측: 선택 개체 속성 / 상단: 시간, 플레이 컨트롤 / 하단: 진행 상황.
    /// 1단계에서는 틀과 시간 진행만 동작하고, 자동 배치와 시뮬레이션은 2~3단계에서 붙인다.
    /// </summary>
    public class LayoutScreen : ScreenBase
    {
        const float TopH = 72f, BottomH = 140f, LeftW = 320f, RightW = 380f;
        static readonly float[] Speeds = { 1f, 2f, 4f, 8f, 16f };

        ProjectInput In => App.CurrentInput;

        // 시간/플레이
        float _simTime;     // 초
        bool _playing;
        int _speedIndex;
        Text _timeText;
        Button _speedBtn;

        // 속성창
        Text _propTitle;
        Text _propBody;

        // 하단
        Text _progressText;
        RectTransform _progressFill;
        Text _logText;

        // 3D 바닥
        GameObject _floor;

        public override void Build(RectTransform root)
        {
            BuildFloor();
            BuildTopBar(root);
            BuildPalette(root);
            BuildProperties(root);
            BuildBottomBar(root);
            ShowInputProperties();
            SetProgress(0f, "레이아웃 생성 대기 중  ·  자동 배치는 다음 단계에서 구현됩니다.");
            Log($"프로젝트 '{In.projectName}' 로드됨  ·  {ProjectInput.UnitName(In.cargo.unitType)}, 피크 시간당 {In.throughput.PeakTotalPerHour:0}건, 보관 {In.throughput.inventoryUnits:N0} 단위");
        }

        void Update()
        {
            if (!_playing) return;
            _simTime += Time.deltaTime * Speeds[_speedIndex];
            UpdateTimeText();
        }

        void OnDestroy()
        {
            if (_floor != null) Destroy(_floor);
        }

        // ---------- 3D 바닥 (건물 외곽) ----------

        void BuildFloor()
        {
            float w = Mathf.Max(10f, In.building.widthM);
            float d = Mathf.Max(10f, In.building.depthM);
            _floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _floor.name = "BuildingFloor";
            _floor.transform.localScale = new Vector3(w / 10f, 1f, d / 10f); // Plane 기본 크기 10m
            var r = _floor.GetComponent<Renderer>();
            if (r != null) r.material.color = new Color(0.30f, 0.33f, 0.36f);

            var cam = Camera.main;
            if (cam != null)
            {
                // HUD가 좌우를 가리므로 약간 여유 있게 잡는다
                float size = Mathf.Max(w, d);
                cam.transform.position = new Vector3(0f, size * 0.95f, -size * 0.75f);
                cam.transform.LookAt(Vector3.zero);
                cam.farClipPlane = Mathf.Max(1000f, size * 5f);
            }
        }

        // ---------- 상단: 시간, 플레이 컨트롤 ----------

        void BuildTopBar(RectTransform root)
        {
            var bar = UIFactory.Panel(root, "TopBar", Theme.Panel);
            UIFactory.DockTop(bar, TopH);
            UIFactory.HLayout(bar.gameObject, 10, new RectOffset(20, 20, 10, 10));

            var name = UIFactory.Label(bar, In.projectName, 24, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(name, width: 380);

            var spacerL = UIFactory.Rect(bar, "Spacer");
            UIFactory.Size(spacerL, flexWidth: 1);

            var timeBox = UIFactory.Panel(bar, "Time", Theme.Field);
            UIFactory.Size(timeBox, width: 200);
            _timeText = UIFactory.Label(timeBox, "", 26, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(_timeText.rectTransform);
            UpdateTimeText();

            UIFactory.Size(UIFactory.CreateButton(bar, "▶", () => _playing = true, Theme.AccentDim, 24), width: 60);
            UIFactory.Size(UIFactory.CreateButton(bar, "∥", () => _playing = false, Theme.PanelLight, 20), width: 60);
            UIFactory.Size(UIFactory.CreateButton(bar, "■", () => { _playing = false; _simTime = 0f; UpdateTimeText(); }, Theme.PanelLight, 22), width: 60);
            _speedBtn = UIFactory.CreateButton(bar, "×1", () =>
            {
                _speedIndex = (_speedIndex + 1) % Speeds.Length;
                UIFactory.SetButtonLabel(_speedBtn, "×" + Speeds[_speedIndex]);
            }, Theme.PanelLight, 22);
            UIFactory.Size(_speedBtn, width: 70);

            var spacerR = UIFactory.Rect(bar, "Spacer");
            UIFactory.Size(spacerR, flexWidth: 1);

            UIFactory.Size(UIFactory.CreateButton(bar, "입력 수정", () => App.ShowInput(), Theme.PanelLight, 20), width: 130);
            UIFactory.Size(UIFactory.CreateButton(bar, "처음으로", () => App.ShowStart(), Theme.PanelLight, 20), width: 130);
        }

        void UpdateTimeText()
        {
            if (_timeText == null) return;
            var t = TimeSpan.FromSeconds(_simTime);
            _timeText.text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        }

        // ---------- 좌측: 설비 팔레트 ----------

        void BuildPalette(RectTransform root)
        {
            var panel = UIFactory.Panel(root, "Palette", Theme.Panel);
            UIFactory.DockLeft(panel, LeftW, TopH, BottomH);
            UIFactory.VLayout(panel.gameObject, 6, new RectOffset(14, 14, 14, 14));

            var title = UIFactory.Label(panel, "설비", 24, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(title, height: 36);

            foreach (EquipmentCategory cat in Enum.GetValues(typeof(EquipmentCategory)))
            {
                var catLabel = UIFactory.Label(panel, EquipmentCatalog.CategoryName(cat), 18, Theme.TextDim, TextAnchor.LowerLeft);
                UIFactory.Size(catLabel, height: 26);

                foreach (var def in EquipmentCatalog.InCategory(cat))
                    PaletteItem(panel, def, In.IsAllowed(def));
            }
        }

        void PaletteItem(Transform parent, EquipmentDef def, bool allowed)
        {
            var btn = UIFactory.CreateButton(parent, "", () => ShowEquipmentProperties(def), Theme.PanelLight);
            UIFactory.Size(btn, height: 54);
            btn.interactable = allowed;
            UIFactory.ClearChildren(btn.transform); // 기본 라벨 대신 아이콘 + 이름

            var row = UIFactory.Rect(btn.transform, "Row");
            UIFactory.Stretch(row, 6, 6, 5, 5);
            UIFactory.HLayout(row.gameObject, 10);

            var icon = UIFactory.Panel(row, "Icon", allowed ? def.Color : new Color(0.3f, 0.3f, 0.3f));
            UIFactory.Size(icon, width: 56);
            icon.GetComponent<Image>().raycastTarget = false;
            var code = UIFactory.Label(icon, def.Icon, 18, Color.black, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(code.rectTransform);

            var name = UIFactory.Label(row, allowed ? def.NameKo : def.NameKo + " (제외)", 19, allowed ? Theme.Text : Theme.TextDim);
            UIFactory.Size(name, flexWidth: 1);
        }

        // ---------- 우측: 속성창 ----------

        void BuildProperties(RectTransform root)
        {
            var panel = UIFactory.Panel(root, "Properties", Theme.Panel);
            UIFactory.DockRight(panel, RightW, TopH, BottomH);
            UIFactory.VLayout(panel.gameObject, 10, new RectOffset(18, 18, 14, 14));

            var title = UIFactory.Label(panel, "속성", 24, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(title, height: 36);

            _propTitle = UIFactory.Label(panel, "", 22, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_propTitle, height: 34);

            _propBody = UIFactory.Label(panel, "", 19, Theme.Text, TextAnchor.UpperLeft);
            _propBody.lineSpacing = 1.25f;
            UIFactory.Size(_propBody, flexHeight: 1);

            var btn = UIFactory.CreateButton(panel, "프로젝트 입력값 보기", ShowInputProperties, Theme.PanelLight, 19);
            UIFactory.Size(btn, height: 44);
        }

        void ShowInputProperties()
        {
            _propTitle.text = "프로젝트 입력값";
            _propBody.text = In.ToSummary().Replace(",  ", "\n    ").Replace("[", "\n[").TrimStart('\n');
        }

        void ShowEquipmentProperties(EquipmentDef def)
        {
            var units = new List<string>();
            if ((def.Supports & LoadUnitMask.Pallet) != 0) units.Add("파렛트");
            if ((def.Supports & LoadUnitMask.Tote) != 0) units.Add("토트");
            if ((def.Supports & LoadUnitMask.Carton) != 0) units.Add("카톤");

            var sb = new StringBuilder();
            sb.AppendLine($"카테고리:  {EquipmentCatalog.CategoryName(def.Category)}");
            sb.AppendLine($"적용 단위:  {string.Join(", ", units)}");
            sb.AppendLine($"자동화:  {(def.Automated ? "예" : "아니오")}");
            sb.AppendLine($"대표 점유:  {def.FootprintWidthM:0.#} × {def.FootprintDepthM:0.#} m");
            if (def.SpeedMps > 0) sb.AppendLine($"이동 속도:  {def.SpeedMps:0.#} m/s");
            if (def.CyclesPerHour > 0) sb.AppendLine($"처리능력:  {def.CyclesPerHour:0} 회/시간");
            sb.AppendLine();
            sb.Append(def.Description);
            _propTitle.text = def.NameKo;
            _propBody.text = sb.ToString();
            Log($"선택: {def.NameKo}");
        }

        // ---------- 하단: 진행 상황 ----------

        void BuildBottomBar(RectTransform root)
        {
            var bar = UIFactory.Panel(root, "BottomBar", Theme.Panel);
            UIFactory.DockBottom(bar, BottomH);
            UIFactory.VLayout(bar.gameObject, 8, new RectOffset(20, 20, 14, 14));

            _progressText = UIFactory.Label(bar, "", 20, Theme.Text);
            UIFactory.Size(_progressText, height: 28);

            var track = UIFactory.Panel(bar, "ProgressTrack", Theme.Field);
            UIFactory.Size(track, height: 14);
            _progressFill = UIFactory.Panel(track, "Fill", Theme.Accent);
            _progressFill.anchorMin = Vector2.zero;
            _progressFill.anchorMax = new Vector2(0f, 1f);
            _progressFill.offsetMin = Vector2.zero;
            _progressFill.offsetMax = Vector2.zero;

            _logText = UIFactory.Label(bar, "", 18, Theme.TextDim, TextAnchor.UpperLeft);
            UIFactory.Size(_logText, flexHeight: 1);
        }

        void SetProgress(float ratio, string text)
        {
            ratio = Mathf.Clamp01(ratio);
            _progressFill.anchorMax = new Vector2(ratio, 1f);
            _progressText.text = $"진행 {ratio * 100f:0}%  ·  {text}";
        }

        readonly Queue<string> _logLines = new Queue<string>();

        void Log(string line)
        {
            _logLines.Enqueue(line);
            while (_logLines.Count > 2) _logLines.Dequeue();
            _logText.text = string.Join("\n", _logLines);
        }
    }
}
