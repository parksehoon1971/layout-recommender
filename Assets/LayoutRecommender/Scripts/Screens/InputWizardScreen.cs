using System;
using System.Collections.Generic;
using System.Globalization;
using LayoutRecommender.Data;
using LayoutRecommender.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LayoutRecommender.Screens
{
    /// <summary>
    /// 입력 절차: 화물 특성 → 입출고량 → 건물 조건 → 설비 선호 → 확인
    /// </summary>
    public class InputWizardScreen : ScreenBase
    {
        static readonly string[] StepNames = { "화물 특성", "입출고량", "건물 조건", "설비 선호", "확인" };

        int _step;
        RectTransform _content;
        Text _derived;
        Text _error;
        Button _prevBtn;
        Button _nextBtn;
        readonly List<Image> _stepBgs = new List<Image>();
        readonly List<Text> _stepTexts = new List<Text>();

        ProjectInput In => App.CurrentInput;

        public override void Build(RectTransform root)
        {
            var bg = UIFactory.Panel(root, "Background", Theme.Background);
            UIFactory.Stretch(bg);

            // 상단: 제목 + 단계 표시
            var header = UIFactory.Panel(root, "Header", Theme.Panel);
            UIFactory.DockTop(header, 150);

            var title = UIFactory.Label(header, "프로젝트 입력", 34, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(60, -70), new Vector2(-60, -10));

            var steps = UIFactory.Rect(header, "Steps");
            UIFactory.Anchor(steps, new Vector2(0, 0), new Vector2(1, 0), new Vector2(60, 16), new Vector2(-60, 70));
            var hl = UIFactory.HLayout(steps.gameObject, 10);
            hl.childForceExpandWidth = true;
            for (int i = 0; i < StepNames.Length; i++)
            {
                int index = i;
                var b = UIFactory.CreateButton(steps, $"{i + 1}. {StepNames[i]}", () => JumpTo(index), Theme.PanelLight, 22);
                UIFactory.Size(b, flexWidth: 1);
                _stepBgs.Add(b.GetComponent<Image>());
                _stepTexts.Add(b.GetComponentInChildren<Text>());
            }

            // 본문
            var body = UIFactory.Panel(root, "Body", Theme.Panel);
            UIFactory.Anchor(body, Vector2.zero, Vector2.one, new Vector2(240, 130), new Vector2(-240, -180));
            UIFactory.VLayout(body.gameObject, 12, new RectOffset(50, 50, 40, 30));

            _content = UIFactory.Rect(body, "Content");
            UIFactory.VLayout(_content.gameObject, 12);
            UIFactory.Size(_content, flexHeight: 1);

            _derived = UIFactory.Label(body, "", 21, Theme.Success, TextAnchor.UpperLeft);
            UIFactory.Size(_derived, height: 90);

            // 하단: 이전/다음 + 오류
            var footer = UIFactory.Rect(root, "Footer");
            UIFactory.DockBottom(footer, 110, 240, 240);
            UIFactory.HLayout(footer.gameObject, 20, new RectOffset(0, 0, 25, 25));

            _prevBtn = UIFactory.CreateButton(footer, "◀ 이전", OnPrev, Theme.PanelLight, 24);
            UIFactory.Size(_prevBtn, width: 200);

            _error = UIFactory.Label(footer, "", 20, Theme.Danger, TextAnchor.MiddleCenter);
            UIFactory.Size(_error, flexWidth: 1);

            _nextBtn = UIFactory.CreateButton(footer, "다음 ▶", OnNext, Theme.Accent, 24);
            UIFactory.Size(_nextBtn, width: 260);

            RebuildStep();
        }

        // ---------- 이동 ----------

        void OnPrev()
        {
            if (_step == 0) { App.ShowStart(); return; }
            _step--;
            RebuildStep();
        }

        void OnNext()
        {
            if (!CheckStep(_step)) return;
            if (_step == StepNames.Length - 1)
            {
                InputStorage.Save(In);
                App.ShowLayout();
                return;
            }
            _step++;
            RebuildStep();
        }

        void JumpTo(int target)
        {
            // 뒤로는 자유롭게, 앞으로는 중간 단계 검증을 통과해야 이동
            for (int s = _step; s < target; s++)
            {
                if (!CheckStep(s)) { _step = s; RebuildStep(keepError: true); return; }
            }
            _step = target;
            RebuildStep();
        }

        bool CheckStep(int step)
        {
            var errors = In.Validate(step);
            if (errors.Count == 0) { _error.text = ""; return true; }
            _error.text = string.Join("\n", errors);
            return false;
        }

        void RebuildStep(bool keepError = false)
        {
            if (!keepError) _error.text = "";
            UIFactory.ClearChildren(_content);

            for (int i = 0; i < _stepBgs.Count; i++)
            {
                _stepBgs[i].color = i == _step ? Theme.Accent : (i < _step ? Theme.AccentDim : Theme.PanelLight);
                _stepTexts[i].color = i <= _step ? Theme.Text : Theme.TextDim;
            }
            UIFactory.SetButtonLabel(_prevBtn, _step == 0 ? "◀ 처음으로" : "◀ 이전");
            UIFactory.SetButtonLabel(_nextBtn, _step == StepNames.Length - 1 ? "레이아웃 생성 ▶" : "다음 ▶");

            switch (_step)
            {
                case 0: BuildCargo(); break;
                case 1: BuildThroughput(); break;
                case 2: BuildBuilding(); break;
                case 3: BuildPreference(); break;
                case 4: BuildSummary(); break;
            }
            RefreshDerived();
        }

        // ---------- 단계별 화면 ----------

        void BuildCargo()
        {
            Heading("적재 단위(물성)와 규격을 입력하세요.");
            var units = new[] { ProjectInput.UnitName(LoadUnitType.Pallet), ProjectInput.UnitName(LoadUnitType.Tote), ProjectInput.UnitName(LoadUnitType.Carton) };
            var row = Row("적재 단위");
            var sel = UIFactory.CreateSelector(row, units, (int)In.cargo.unitType, i =>
            {
                In.cargo.ApplyUnitDefaults((LoadUnitType)i);
                RebuildStep();
            });
            UIFactory.Size(sel, width: 360);
            Hint(row, "바꾸면 대표 규격으로 채워집니다");

            FloatRow("길이 (L)", () => In.cargo.lengthMm, v => In.cargo.lengthMm = v, "mm");
            FloatRow("폭 (W)", () => In.cargo.widthMm, v => In.cargo.widthMm = v, "mm");
            FloatRow("높이 (H)", () => In.cargo.heightMm, v => In.cargo.heightMm = v, "mm");
            FloatRow("중량", () => In.cargo.weightKg, v => In.cargo.weightKg = v, "kg");
            IntRow("SKU 수", () => In.cargo.skuCount, v => In.cargo.skuCount = v, "개");

            var tRow = Row("단적 (겹쳐 쌓기)");
            var tog = UIFactory.CreateToggle(tRow, "가능", In.cargo.stackable, v => { In.cargo.stackable = v; RefreshDerived(); });
            UIFactory.Size(tog, width: 200);
        }

        void BuildThroughput()
        {
            Heading("하루 입출고량과 보관 필요량을 입력하세요. (단위: 위에서 고른 적재 단위)");
            IntRow("일 입고량", () => In.throughput.inboundPerDay, v => In.throughput.inboundPerDay = v, "단위/일");
            IntRow("일 출고량", () => In.throughput.outboundPerDay, v => In.throughput.outboundPerDay = v, "단위/일");
            FloatRow("피크 계수", () => In.throughput.peakFactor, v => In.throughput.peakFactor = v, "배 (평균 대비)");
            FloatRow("일 가동시간", () => In.throughput.operatingHours, v => In.throughput.operatingHours = v, "시간");
            IntRow("보관 필요량", () => In.throughput.inventoryUnits, v => In.throughput.inventoryUnits = v, "단위 (최대 재고)");
        }

        void BuildBuilding()
        {
            Heading("건물(창고) 조건을 입력하세요.");
            FloatRow("건물 폭", () => In.building.widthM, v => In.building.widthM = v, "m");
            FloatRow("건물 깊이", () => In.building.depthM, v => In.building.depthM = v, "m");
            FloatRow("유효 층고", () => In.building.clearHeightM, v => In.building.clearHeightM = v, "m");
            IntRow("입고 도크 수", () => In.building.inboundDocks, v => In.building.inboundDocks = v, "개");
            IntRow("출고 도크 수", () => In.building.outboundDocks, v => In.building.outboundDocks = v, "개");
        }

        void BuildPreference()
        {
            Heading("자동화 수준을 고르고, 쓰지 않을 설비는 끄세요. (회색은 적재 단위에 맞지 않는 설비)");
            var names = new[]
            {
                ProjectInput.AutomationName(AutomationPreference.Manual),
                ProjectInput.AutomationName(AutomationPreference.SemiAuto),
                ProjectInput.AutomationName(AutomationPreference.FullAuto)
            };
            var row = Row("자동화 수준");
            var sel = UIFactory.CreateSelector(row, names, (int)In.preference.automation, i =>
            {
                In.preference.automation = (AutomationPreference)i;
                RefreshDerived();
            });
            UIFactory.Size(sel, width: 360);

            foreach (EquipmentCategory cat in Enum.GetValues(typeof(EquipmentCategory)))
            {
                var catLabel = UIFactory.Label(_content, EquipmentCatalog.CategoryName(cat), 22, Theme.TextDim, TextAnchor.LowerLeft, FontStyle.Bold);
                UIFactory.Size(catLabel, height: 34);

                var grid = UIFactory.Rect(_content, "Grid_" + cat);
                var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
                gl.cellSize = new Vector2(320, 48);
                gl.spacing = new Vector2(12, 10);
                gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gl.constraintCount = 4;

                int count = 0;
                foreach (var def in EquipmentCatalog.InCategory(cat))
                {
                    var d = def;
                    bool fits = EquipmentCatalog.Supports(d, In.cargo.unitType);
                    string label = fits ? d.NameKo : d.NameKo + " (부적합)";
                    UIFactory.CreateToggle(grid, label, fits && !In.preference.IsExcluded(d.Type),
                        on => { In.preference.SetAllowed(d.Type, on); RefreshDerived(); }, fits);
                    count++;
                }
                int rows = Mathf.CeilToInt(count / 4f);
                UIFactory.Size(grid, height: rows * 48 + (rows - 1) * 10);
            }
        }

        void BuildSummary()
        {
            Heading("입력 내용을 확인하세요. 수정하려면 위의 단계를 누르세요.");
            var nameRow = Row("프로젝트 이름");
            var nameField = UIFactory.CreateInputField(nameRow, In.projectName, InputField.ContentType.Standard,
                s => { if (!string.IsNullOrWhiteSpace(s)) In.projectName = s.Trim(); });
            UIFactory.Size(nameField, width: 500);

            var summary = UIFactory.Label(_content, In.ToSummary(), 22, Theme.Text, TextAnchor.UpperLeft);
            summary.lineSpacing = 1.3f;
            UIFactory.Size(summary, flexHeight: 1);
        }

        void RefreshDerived()
        {
            if (_derived == null) return;
            switch (_step)
            {
                case 0:
                    _derived.text = $"단위 부피 {In.cargo.VolumeM3:0.###} m³   ·   SKU당 평균 재고 {(In.cargo.skuCount > 0 ? (float)In.throughput.inventoryUnits / In.cargo.skuCount : 0):0.#} 단위";
                    break;
                case 1:
                    _derived.text =
                        $"평균 시간당 입고 {In.throughput.AvgInboundPerHour:0.#}  ·  출고 {In.throughput.AvgOutboundPerHour:0.#}\n" +
                        $"피크 시간당 합계 {In.throughput.PeakTotalPerHour:0.#} 건   ·   SKU당 평균 재고 {(In.cargo.skuCount > 0 ? (float)In.throughput.inventoryUnits / In.cargo.skuCount : 0):0.#} 단위";
                    break;
                case 2:
                    _derived.text =
                        $"바닥 면적 {In.building.AreaM2:N0} m²   ·   예상 적재 단수 약 {In.EstimatedRackLevels()}단 (화물 높이 + 0.25m 기준)";
                    break;
                case 3:
                    int allowed = 0;
                    foreach (var d in EquipmentCatalog.All) if (In.IsAllowed(d)) allowed++;
                    _derived.text = $"후보 설비 {allowed}종   ·   자동화 수준: {ProjectInput.AutomationName(In.preference.automation)}";
                    break;
                default:
                    _derived.text = "‘레이아웃 생성’을 누르면 배치 화면으로 이동합니다. 입력값은 자동 저장됩니다.";
                    break;
            }
        }

        // ---------- 폼 도우미 ----------

        void Heading(string text)
        {
            var h = UIFactory.Label(_content, text, 24, Theme.TextDim);
            UIFactory.Size(h, height: 44);
        }

        RectTransform Row(string label)
        {
            var row = UIFactory.Rect(_content, "Row_" + label);
            UIFactory.HLayout(row.gameObject, 14);
            UIFactory.Size(row, height: 52);
            var l = UIFactory.Label(row, label, 24);
            UIFactory.Size(l, width: 260);
            return row;
        }

        void Hint(RectTransform row, string text)
        {
            var t = UIFactory.Label(row, text, 20, Theme.TextDim);
            UIFactory.Size(t, flexWidth: 1);
        }

        static string Fmt(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        void FloatRow(string label, Func<float> get, Action<float> set, string unit)
        {
            var row = Row(label);
            InputField field = null;
            field = UIFactory.CreateInputField(row, Fmt(get()), InputField.ContentType.DecimalNumber, s =>
            {
                if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0f)
                {
                    set(v);
                    RefreshDerived();
                }
                field.text = Fmt(get());
            });
            UIFactory.Size(field, width: 240);
            Hint(row, unit);
        }

        void IntRow(string label, Func<int> get, Action<int> set, string unit)
        {
            var row = Row(label);
            InputField field = null;
            field = UIFactory.CreateInputField(row, get().ToString(CultureInfo.InvariantCulture), InputField.ContentType.IntegerNumber, s =>
            {
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 0)
                {
                    set(v);
                    RefreshDerived();
                }
                field.text = get().ToString(CultureInfo.InvariantCulture);
            });
            UIFactory.Size(field, width: 240);
            Hint(row, unit);
        }
    }
}
