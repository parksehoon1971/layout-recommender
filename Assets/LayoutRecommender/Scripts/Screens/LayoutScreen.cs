using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LayoutRecommender.Core;
using LayoutRecommender.Data;
using LayoutRecommender.Planning;
using LayoutRecommender.Simulation;
using LayoutRecommender.UI;
using LayoutRecommender.World;
using UnityEngine;
using UnityEngine.UI;

namespace LayoutRecommender.Screens
{
    /// <summary>
    /// 레이아웃 생성 화면.
    /// 좌측: 카테고리별 설비 팔레트 / 우측: 배치안 비교와 선택 개체 속성 / 상단: 시간, 플레이 컨트롤 / 하단: 진행 상황.
    /// 들어오면 배치안을 만들고, 추천안을 팔레트에서 하나씩 끌어다 놓는 애니메이션으로 배치한 뒤 시뮬레이션 시간을 시작한다.
    /// </summary>
    public class LayoutScreen : ScreenBase
    {
        const float TopH = 72f, BottomH = 140f, LeftW = 320f, RightW = 400f;
        static readonly float[] Speeds = { 1f, 2f, 4f, 8f, 16f, 32f, 60f };
        const int DefaultSpeedIndex = 3; // ×8: 피크 시간 흐름을 금방 볼 수 있게

        ProjectInput In => App.CurrentInput;
        RectTransform _root;
        RectTransform _ghostLayer;   // 끌어다 놓는 아이콘이 날아다니는 층 (HUD 위)

        // 배치안
        List<LayoutPlan> _plans = new List<LayoutPlan>();
        int _planIndex = -1;
        LayoutPlan Plan => _planIndex >= 0 && _planIndex < _plans.Count ? _plans[_planIndex] : null;

        // 3D
        LayoutWorld _world;
        CameraRig _rig;
        readonly List<EquipmentInstance> _spawned = new List<EquipmentInstance>();
        EquipmentInstance _selected;

        // 자동 배치
        Coroutine _placing;
        bool _skip;
        bool _placementDone;
        int _flying;

        // 시간/플레이
        float _simTime;
        bool _playing;
        int _speedIndex = DefaultSpeedIndex;

        // 시뮬레이션
        FlowSimulator _sim;
        float _statsTimer;
        enum PanelMode { Plan, Item, Def, Input }
        PanelMode _panelMode = PanelMode.Plan;
        Text _timeText;
        Button _speedBtn;

        // 좌측 팔레트
        class PaletteEntry { public RectTransform Icon; public RawImage Picture; public Text Code; public Text Name; public EquipmentDef Def; public int Count; }
        readonly Dictionary<EquipmentType, PaletteEntry> _palette = new Dictionary<EquipmentType, PaletteEntry>();

        // 우측
        RectTransform _planList;
        Text _propTitle;
        Text _propBody;
        ScrollRect _propScroll;

        // 하단
        Text _progressText;
        RectTransform _progressFill;
        Text _logText;
        Button _skipBtn;
        readonly Queue<string> _logLines = new Queue<string>();

        // ================================================================

        public override void Build(RectTransform root)
        {
            _root = root;
            BuildTopBar(root);
            BuildPalette(root);
            BuildProperties(root);
            BuildBottomBar(root);
            _ghostLayer = UIFactory.Rect(root, "GhostLayer");
            UIFactory.Stretch(_ghostLayer);
            _ghostLayer.SetAsLastSibling();

            _world = new GameObject("LayoutWorld").AddComponent<LayoutWorld>();
            var cam = Camera.main;
            if (cam != null)
            {
                _rig = cam.GetComponent<CameraRig>();
                if (_rig == null) _rig = cam.gameObject.AddComponent<CameraRig>();
                _rig.enabled = true;
                _rig.Frame(In.building.widthM, In.building.depthM);
            }

            try
            {
                _plans = LayoutPlanner.Generate(In);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _plans = new List<LayoutPlan>();
            }

            // 배치안 전환 때 StopAllCoroutines에 끊기지 않도록 앱 쪽에서 돌린다
            IconRenderer.Ensure(App, Theme.PanelLight, () => { if (this != null) ApplyIcons(); });

            BuildPlanButtons();
            if (_plans.Count == 0)
            {
                _world.BuildEnvironment(null, In);
                _propTitle.text = "배치안 없음";
                _propBody.text = "입력 조건에 맞는 배치안을 만들지 못했습니다.\n\n· 설비 선호에서 보관 설비와 운반 설비가 허용되어 있는지\n· 건물 깊이가 충분한지 (도크·대기 구역을 빼고 12m 이상)\n\n를 확인한 뒤 '입력 수정'으로 돌아가 주세요.";
                SetProgress(0f, "배치안을 만들지 못했습니다.");
                return;
            }
            Log($"배치안 {_plans.Count}개 생성 · 추천: {_plans[0].Name} ({_plans[0].Score:0}점)");
            SelectPlan(0);
        }

        void OnDestroy()
        {
            if (_world != null) Destroy(_world.gameObject);
            if (_rig != null) _rig.enabled = false;
        }

        void Update()
        {
            if (_playing)
            {
                float dt = Time.deltaTime * Speeds[_speedIndex];
                _simTime += dt;
                UpdateTimeText();
                if (_sim != null)
                {
                    // 배속이 커도 움직임이 튀지 않게 잘게 나눠 진행
                    int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.5f), 1, 200);
                    for (int i = 0; i < steps; i++) _sim.Tick(dt / steps);
                }
            }

            _statsTimer -= Time.deltaTime;
            if (_sim != null && _statsTimer <= 0f)
            {
                _statsTimer = 0.3f;
                SetProgress(1f, _sim.Summary());
                if (_panelMode == PanelMode.Plan) ShowPlanDetails(keepScroll: true);
            }

            if (InputBridge.LeftPressed && !InputBridge.PointerOverUI)
                TrySelectUnderMouse();
        }

        // ================================================================
        // 배치안 선택과 자동 배치
        // ================================================================

        void SelectPlan(int index)
        {
            if (index < 0 || index >= _plans.Count) return;
            // 이전 배치안의 배치/고스트 애니메이션을 모두 멈추고 지운다
            StopAllCoroutines();
            _placing = null;
            if (_ghostLayer != null) UIFactory.ClearChildren(_ghostLayer);
            _planIndex = index;
            _sim = null; // 화물 등 실행 중 오브젝트는 BuildEnvironment에서 함께 지워진다
            _playing = false;
            _simTime = 0f;
            UpdateTimeText();
            Select(null);
            _spawned.Clear();
            _flying = 0;
            ResetPaletteCounts();
            RefreshPlanButtons();

            _world.BuildEnvironment(Plan, In);
            ShowPlanDetails();
            _placing = StartCoroutine(AutoPlace(Plan));
        }

        static int ShapeOrder(ItemShape s)
        {
            switch (s)
            {
                case ItemShape.RackRow: return 0;
                case ItemShape.ShuttleBlock: return 0;
                case ItemShape.Mast: return 1;
                case ItemShape.Conveyor: return 2;
                case ItemShape.Vehicle: return 3;
                default: return 4;
            }
        }

        IEnumerator AutoPlace(LayoutPlan plan)
        {
            _skip = false;
            _placementDone = false;
            _skipBtn.interactable = true;
            var items = plan.Items.OrderBy(i => ShapeOrder(i.Shape)).ToList();
            int total = items.Count;
            Log($"{plan.Name} 자동 배치 시작 · 설비 {total}개");

            for (int i = 0; i < total; i++)
            {
                if (_skip)
                {
                    for (int j = i; j < total; j++) Place(items[j], false);
                    SetProgress(1f, $"배치 중 · {total}/{total}");
                    break;
                }

                const float speed = 1f; // 배치 애니메이션 속도 (시뮬레이션 배속과 별개)
                float interval = Mathf.Clamp(12f / Mathf.Max(1, total), 0.04f, 0.4f) / speed;
                float flight = Mathf.Clamp(interval * 4f, 0.25f, 0.7f) / Mathf.Sqrt(speed);
                StartCoroutine(Fly(items[i], flight));
                SetProgress((float)(i + 1) / total, $"배치 중 · {i + 1}/{total} · {items[i].Label}");
                yield return new WaitForSeconds(interval);
            }

            while (_flying > 0) yield return null;
            OnPlacementDone(plan);
        }

        /// <summary>팔레트 아이콘에서 목표 위치까지 '끌어다 놓는' 고스트를 날린 뒤 3D 설비를 놓는다.</summary>
        IEnumerator Fly(PlacedItem item, float duration)
        {
            _flying++;
            var cam = Camera.main;
            _palette.TryGetValue(item.Type, out var entry);
            Vector3 targetWorld = item.Center + Vector3.up * item.Size.y * 0.5f;
            Vector3 screen = cam != null ? cam.WorldToScreenPoint(targetWorld) : Vector3.zero;

            if (entry == null || cam == null || screen.z < 0f)
            {
                Place(item, true);
                _flying--;
                yield break;
            }

            var ghost = UIFactory.Panel(_ghostLayer, "Ghost", entry.Def.Color);
            ghost.sizeDelta = new Vector2(56, 40);
            ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
            ghost.GetComponent<Image>().raycastTarget = false;
            ghost.sizeDelta = new Vector2(80, 60);
            if (IconRenderer.TryGet(entry.Def.Type, out var tex))
            {
                var pic = UIFactory.Rect(ghost, "Picture").gameObject.AddComponent<RawImage>();
                pic.texture = tex;
                pic.raycastTarget = false;
                UIFactory.Stretch(pic.rectTransform, 2, 2, 2, 2);
            }
            else
            {
                var gl = UIFactory.Label(ghost, entry.Def.Icon, 18, Color.black, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(gl.rectTransform);
            }

            Vector3 start = entry.Icon.position;     // 오버레이 캔버스라 화면 좌표와 같다
            Vector3 end = new Vector3(screen.x, screen.y, 0f);
            float arc = Mathf.Min(160f, Vector3.Distance(start, end) * 0.25f);
            float t = 0f;
            while (t < duration && !_skip)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float e = k * k * (3f - 2f * k);  // smoothstep
                var p = Vector3.Lerp(start, end, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * arc;
                ghost.position = p;
                float sc = Mathf.Lerp(1.1f, 0.6f, e);
                ghost.localScale = new Vector3(sc, sc, 1f);
                yield return null;
            }
            Destroy(ghost.gameObject);
            Place(item, !_skip);
            _flying--;
        }

        void Place(PlacedItem item, bool animate)
        {
            var inst = _world.Spawn(item);
            _spawned.Add(inst);
            if (animate) inst.PlayDrop(0.25f);
            if (_palette.TryGetValue(item.Type, out var entry) && CountsAsUnit(item))
            {
                entry.Count++;
                UpdatePaletteLabel(entry);
                if (animate) StartCoroutine(Pulse(entry.Icon));
            }
        }

        /// <summary>팔레트 개수에 셀 항목 (자동창고 랙 열은 크레인 1대에 딸린 구조물이라 제외)</summary>
        static bool CountsAsUnit(PlacedItem item)
        {
            if (item.Type == EquipmentType.StackerCraneSingle || item.Type == EquipmentType.StackerCraneDouble)
                return item.Shape == ItemShape.Mast;
            if (item.Type == EquipmentType.ShuttleRack)
                return item.Shape == ItemShape.ShuttleBlock;
            return true;
        }

        void OnPlacementDone(LayoutPlan plan)
        {
            _placing = null;
            _placementDone = true;
            _skipBtn.interactable = false;
            SetProgress(1f, $"배치 완료 · {plan.Name} · 시뮬레이션 시작");
            try
            {
                _sim = new FlowSimulator(plan, In, _world, _spawned);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _sim = null;
            }
            if (_sim != null && !_sim.HasMovers)
                Log("운반 설비가 없어 화물 이동은 시뮬레이션하지 않습니다.");
            Log($"배치 완료 · 설비 {_spawned.Count}개 · 시뮬레이션 자동 시작 (×{Speeds[_speedIndex]} 배속, 피크 시간 기준)");
            _playing = true;
        }

        // ================================================================
        // 선택
        // ================================================================

        void TrySelectUnderMouse()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(InputBridge.MousePosition);
            if (Physics.Raycast(ray, out var hit, 5000f))
            {
                var inst = hit.collider.GetComponentInParent<EquipmentInstance>();
                Select(inst);
            }
            else Select(null);
        }

        void Select(EquipmentInstance inst)
        {
            if (_selected != null) _selected.SetHighlight(false);
            _selected = inst;
            if (_selected == null)
            {
                if (Plan != null) ShowPlanDetails();
                return;
            }
            _selected.SetHighlight(true);
            ShowItemDetails(_selected.Item);
        }

        // ================================================================
        // 상단: 시간, 플레이 컨트롤
        // ================================================================

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

            UIFactory.Size(UIFactory.CreateButton(bar, "▶", () => { if (_placementDone) _playing = true; }, Theme.AccentDim, 24), width: 60);
            UIFactory.Size(UIFactory.CreateButton(bar, "∥", () => _playing = false, Theme.PanelLight, 20), width: 60);
            UIFactory.Size(UIFactory.CreateButton(bar, "■", () => { _playing = false; _simTime = 0f; UpdateTimeText(); }, Theme.PanelLight, 22), width: 60);
            _speedBtn = UIFactory.CreateButton(bar, "×" + Speeds[_speedIndex], () =>
            {
                _speedIndex = (_speedIndex + 1) % Speeds.Length;
                UIFactory.SetButtonLabel(_speedBtn, "×" + Speeds[_speedIndex]);
            }, Theme.PanelLight, 22);
            UIFactory.Size(_speedBtn, width: 70);

            var spacerR = UIFactory.Rect(bar, "Spacer");
            UIFactory.Size(spacerR, flexWidth: 1);

            UIFactory.Size(UIFactory.CreateButton(bar, "다시 배치", () => SelectPlan(Mathf.Max(0, _planIndex)), Theme.PanelLight, 20), width: 120);
            UIFactory.Size(UIFactory.CreateButton(bar, "입력 수정", () => App.ShowInput(), Theme.PanelLight, 20), width: 120);
            UIFactory.Size(UIFactory.CreateButton(bar, "처음으로", () => App.ShowStart(), Theme.PanelLight, 20), width: 120);
        }

        void UpdateTimeText()
        {
            if (_timeText == null) return;
            var t = TimeSpan.FromSeconds(_simTime);
            _timeText.text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        }

        // ================================================================
        // 좌측: 설비 팔레트
        // ================================================================

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
            var btn = UIFactory.CreateButton(parent, "", () => ShowEquipmentDef(def), Theme.PanelLight);
            UIFactory.Size(btn, height: 54);
            btn.interactable = allowed;
            UIFactory.ClearChildren(btn.transform);

            var row = UIFactory.Rect(btn.transform, "Row");
            UIFactory.Stretch(row, 6, 6, 5, 5);
            UIFactory.HLayout(row.gameObject, 10);

            var icon = UIFactory.Panel(row, "Icon", allowed ? def.Color : new Color(0.3f, 0.3f, 0.3f));
            UIFactory.Size(icon, width: 64);
            icon.GetComponent<Image>().raycastTarget = false;
            var code = UIFactory.Label(icon, def.Icon, 18, Color.black, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(code.rectTransform);
            var pic = UIFactory.Rect(icon, "Picture").gameObject.AddComponent<RawImage>();
            pic.raycastTarget = false;
            pic.enabled = false;
            UIFactory.Stretch(pic.rectTransform, 2, 2, 2, 2);

            var name = UIFactory.Label(row, allowed ? def.NameKo : def.NameKo + " (제외)", 19, allowed ? Theme.Text : Theme.TextDim);
            UIFactory.Size(name, flexWidth: 1);

            _palette[def.Type] = new PaletteEntry { Icon = icon, Picture = pic, Code = code, Name = name, Def = def };
        }

        /// <summary>3D 모델로 찍은 그림 아이콘을 팔레트에 붙인다.</summary>
        void ApplyIcons()
        {
            foreach (var e in _palette.Values)
            {
                if (e.Picture == null || !IconRenderer.TryGet(e.Def.Type, out var tex)) continue;
                e.Picture.texture = tex;
                e.Picture.enabled = true;
                e.Picture.color = In.IsAllowed(e.Def) ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
                e.Code.enabled = false;
            }
        }

        void ResetPaletteCounts()
        {
            foreach (var e in _palette.Values)
            {
                e.Count = 0;
                UpdatePaletteLabel(e);
            }
        }

        void UpdatePaletteLabel(PaletteEntry e)
        {
            bool allowed = In.IsAllowed(e.Def);
            string baseName = allowed ? e.Def.NameKo : e.Def.NameKo + " (제외)";
            e.Name.text = e.Count > 0 ? $"{baseName}  <b>×{e.Count}</b>" : baseName;
        }

        IEnumerator Pulse(RectTransform icon)
        {
            float t = 0f;
            while (t < 0.2f && icon != null)
            {
                t += Time.deltaTime;
                float s = 1f + 0.25f * Mathf.Sin(Mathf.Clamp01(t / 0.2f) * Mathf.PI);
                icon.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            if (icon != null) icon.localScale = Vector3.one;
        }

        // ================================================================
        // 우측: 배치안 비교와 속성
        // ================================================================

        void BuildProperties(RectTransform root)
        {
            var panel = UIFactory.Panel(root, "Properties", Theme.Panel);
            UIFactory.DockRight(panel, RightW, TopH, BottomH);
            UIFactory.VLayout(panel.gameObject, 8, new RectOffset(16, 16, 14, 14));

            var title = UIFactory.Label(panel, "배치안 추천", 24, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(title, height: 34);

            _planList = UIFactory.Rect(panel, "PlanList");
            UIFactory.VLayout(_planList.gameObject, 6);

            _propTitle = UIFactory.Label(panel, "", 21, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_propTitle, height: 32);

            _propBody = UIFactory.CreateScrollText(panel, 17, out _propScroll);
            _propBody.lineSpacing = 1.2f;
            UIFactory.Size(_propScroll, flexHeight: 1);

            var btn = UIFactory.CreateButton(panel, "프로젝트 입력값 보기", ShowInputProperties, Theme.PanelLight, 18);
            UIFactory.Size(btn, height: 40);
        }

        readonly List<Button> _planButtons = new List<Button>();

        void BuildPlanButtons()
        {
            UIFactory.ClearChildren(_planList);
            _planButtons.Clear();
            for (int i = 0; i < _plans.Count; i++)
            {
                int index = i;
                var b = UIFactory.CreateButton(_planList, "", () => SelectPlan(index), Theme.PanelLight, 18);
                UIFactory.Size(b, height: 44);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                _planButtons.Add(b);
            }
            UIFactory.Size(_planList, height: Mathf.Max(0, _plans.Count * 50 - 6));
            RefreshPlanButtons();
        }

        void RefreshPlanButtons()
        {
            for (int i = 0; i < _planButtons.Count; i++)
            {
                var p = _plans[i];
                UIFactory.SetButtonLabel(_planButtons[i], $"{p.Name}   {p.Score:0}점{(p.Recommended ? "  ★" : "")}");
                _planButtons[i].GetComponent<Image>().color = i == _planIndex ? Theme.AccentDim : Theme.PanelLight;
            }
        }

        void ShowPlanDetails(bool keepScroll = false)
        {
            var p = Plan;
            if (p == null) return;
            _panelMode = PanelMode.Plan;
            _propTitle.text = p.Name + (p.Recommended ? "  (추천)" : "");
            string text = p.ToDetailText();
            if (_sim != null) text = "[시뮬레이션]\n" + _sim.Report() + "\n[배치안]\n" + text;
            _propBody.text = text;
            if (!keepScroll) _propScroll.verticalNormalizedPosition = 1f;
        }

        void ShowItemDetails(PlacedItem item)
        {
            var def = EquipmentCatalog.Get(item.Type);
            var sb = new StringBuilder();
            sb.AppendLine($"종류:  {def.NameKo}");
            sb.AppendLine($"위치:  x {item.Center.x:0.0} m,  z {item.Center.z:0.0} m");
            sb.AppendLine($"크기:  {item.Size.x:0.0} × {item.Size.z:0.0} m,  높이 {item.Size.y:0.0} m");
            sb.AppendLine();
            sb.AppendLine(item.Detail);
            sb.AppendLine();
            sb.Append("빈 곳을 클릭하면 배치안 정보로 돌아갑니다.");
            _panelMode = PanelMode.Item;
            _propTitle.text = item.Label;
            _propBody.text = sb.ToString();
            _propScroll.verticalNormalizedPosition = 1f;
        }

        void ShowEquipmentDef(EquipmentDef def)
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
            if (Plan != null) sb.AppendLine($"이 배치안 사용 수:  {Plan.CountOf(def.Type)}");
            sb.AppendLine();
            sb.Append(def.Description);
            _panelMode = PanelMode.Def;
            _propTitle.text = def.NameKo;
            _propBody.text = sb.ToString();
            _propScroll.verticalNormalizedPosition = 1f;
        }

        void ShowInputProperties()
        {
            _panelMode = PanelMode.Input;
            _propTitle.text = "프로젝트 입력값";
            _propBody.text = In.ToSummary().Replace(",  ", "\n    ").Replace("[", "\n[").TrimStart('\n');
            _propScroll.verticalNormalizedPosition = 1f;
        }

        // ================================================================
        // 하단: 진행 상황
        // ================================================================

        void BuildBottomBar(RectTransform root)
        {
            var bar = UIFactory.Panel(root, "BottomBar", Theme.Panel);
            UIFactory.DockBottom(bar, BottomH);
            UIFactory.VLayout(bar.gameObject, 8, new RectOffset(20, 20, 12, 12));

            var row = UIFactory.Rect(bar, "ProgressRow");
            UIFactory.HLayout(row.gameObject, 12);
            UIFactory.Size(row, height: 34);
            _progressText = UIFactory.Label(row, "", 20, Theme.Text);
            UIFactory.Size(_progressText, flexWidth: 1);
            _skipBtn = UIFactory.CreateButton(row, "배치 건너뛰기 ▶▶", () => _skip = true, Theme.PanelLight, 18);
            UIFactory.Size(_skipBtn, width: 200);

            var track = UIFactory.Panel(bar, "ProgressTrack", Theme.Field);
            UIFactory.Size(track, height: 12);
            _progressFill = UIFactory.Panel(track, "Fill", Theme.Accent);
            _progressFill.anchorMin = Vector2.zero;
            _progressFill.anchorMax = new Vector2(0f, 1f);
            _progressFill.offsetMin = Vector2.zero;
            _progressFill.offsetMax = Vector2.zero;

            _logText = UIFactory.Label(bar, "", 17, Theme.TextDim, TextAnchor.UpperLeft);
            UIFactory.Size(_logText, flexHeight: 1);

            var help = UIFactory.Label(root, "파란 선: 입고 경로 · 주황 선: 출고 경로    |    오른쪽 드래그: 회전 · 휠: 확대/축소 · 가운데(Shift+오른쪽) 드래그: 이동 · 클릭: 설비 선택", 16, Theme.TextDim, TextAnchor.LowerCenter);
            UIFactory.Anchor(help.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(LeftW + 10, BottomH + 6), new Vector2(-RightW - 10, BottomH + 30));
        }

        void SetProgress(float ratio, string text)
        {
            ratio = Mathf.Clamp01(ratio);
            _progressFill.anchorMax = new Vector2(ratio, 1f);
            _progressText.text = $"진행 {ratio * 100f:0}%  ·  {text}";
        }

        void Log(string line)
        {
            _logLines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}");
            while (_logLines.Count > 3) _logLines.Dequeue();
            _logText.text = string.Join("\n", _logLines);
        }
    }
}
