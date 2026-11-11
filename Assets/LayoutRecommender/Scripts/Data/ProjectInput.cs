using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace LayoutRecommender.Data
{
    public enum LoadUnitType
    {
        Pallet,  // 파렛트
        Tote,    // 토트
        Carton   // 카톤박스
    }

    public enum AutomationPreference
    {
        Manual,    // 수동 위주
        SemiAuto,  // 반자동
        FullAuto   // 자동화 우선
    }

    /// <summary>화물 특성 (물성)</summary>
    [Serializable]
    public class CargoSpec
    {
        public LoadUnitType unitType = LoadUnitType.Pallet;
        public float lengthMm = 1100f;
        public float widthMm = 1100f;
        public float heightMm = 1400f;
        public float weightKg = 800f;
        public int skuCount = 500;
        public bool stackable = false;

        public float VolumeM3 => lengthMm * widthMm * heightMm / 1e9f;

        /// <summary>단위 종류를 바꿀 때 대표 규격으로 채운다.</summary>
        public void ApplyUnitDefaults(LoadUnitType type)
        {
            unitType = type;
            switch (type)
            {
                case LoadUnitType.Pallet:
                    lengthMm = 1100f; widthMm = 1100f; heightMm = 1400f; weightKg = 800f; stackable = false;
                    break;
                case LoadUnitType.Tote:
                    lengthMm = 600f; widthMm = 400f; heightMm = 300f; weightKg = 15f; stackable = true;
                    break;
                case LoadUnitType.Carton:
                    lengthMm = 500f; widthMm = 400f; heightMm = 350f; weightKg = 12f; stackable = true;
                    break;
            }
        }
    }

    /// <summary>입출고량</summary>
    [Serializable]
    public class ThroughputSpec
    {
        public int inboundPerDay = 400;       // 일 입고 (단위 수)
        public int outboundPerDay = 400;      // 일 출고 (단위 수)
        public float peakFactor = 1.5f;       // 피크 계수 (평균 대비 배수)
        public float operatingHours = 16f;    // 일 가동시간
        public int inventoryUnits = 8000;     // 보관 필요량 (단위 수)

        public float AvgInboundPerHour => operatingHours > 0 ? inboundPerDay / operatingHours : 0f;
        public float AvgOutboundPerHour => operatingHours > 0 ? outboundPerDay / operatingHours : 0f;
        public float PeakTotalPerHour => (AvgInboundPerHour + AvgOutboundPerHour) * peakFactor;
    }

    /// <summary>건물 조건</summary>
    [Serializable]
    public class BuildingSpec
    {
        public float widthM = 120f;
        public float depthM = 80f;
        public float clearHeightM = 12f;  // 유효 층고
        public int inboundDocks = 6;
        public int outboundDocks = 6;

        public float AreaM2 => widthM * depthM;
    }

    /// <summary>설비 선호/제외</summary>
    [Serializable]
    public class EquipmentPreference
    {
        public AutomationPreference automation = AutomationPreference.SemiAuto;
        public List<EquipmentType> excluded = new List<EquipmentType>();

        public bool IsExcluded(EquipmentType t) => excluded.Contains(t);

        public void SetAllowed(EquipmentType t, bool allowed)
        {
            if (allowed) excluded.Remove(t);
            else if (!excluded.Contains(t)) excluded.Add(t);
        }
    }

    /// <summary>프로젝트 입력 전체. 레이아웃 생성과 추천의 입력값이 된다.</summary>
    [Serializable]
    public class ProjectInput
    {
        public string projectName = "새 물류센터";
        public CargoSpec cargo = new CargoSpec();
        public ThroughputSpec throughput = new ThroughputSpec();
        public BuildingSpec building = new BuildingSpec();
        public EquipmentPreference preference = new EquipmentPreference();

        public static ProjectInput CreateSample() => new ProjectInput();

        /// <summary>해당 설비를 레이아웃 후보로 쓸 수 있는지 (사용자 제외 + 적재 단위 적합성)</summary>
        public bool IsAllowed(EquipmentDef def)
        {
            return !preference.IsExcluded(def.Type) && EquipmentCatalog.Supports(def, cargo.unitType);
        }

        /// <summary>참고용: 층고와 단위 높이로 계산한 예상 적재 단수</summary>
        public int EstimatedRackLevels()
        {
            float levelPitch = cargo.heightMm / 1000f + 0.25f; // 단위 높이 + 빔/여유
            if (levelPitch <= 0f) return 0;
            return Mathf.Max(1, Mathf.FloorToInt((building.clearHeightM - 0.5f) / levelPitch));
        }

        /// <summary>입력 단계별 검증. 비어 있으면 통과.</summary>
        public List<string> Validate(int step)
        {
            var errors = new List<string>();
            switch (step)
            {
                case 0:
                    if (cargo.lengthMm <= 0 || cargo.widthMm <= 0 || cargo.heightMm <= 0)
                        errors.Add("규격(L/W/H)은 0보다 커야 합니다.");
                    if (cargo.weightKg <= 0) errors.Add("중량은 0보다 커야 합니다.");
                    if (cargo.skuCount <= 0) errors.Add("SKU 수는 1 이상이어야 합니다.");
                    break;
                case 1:
                    if (throughput.inboundPerDay < 0 || throughput.outboundPerDay < 0)
                        errors.Add("입출고량은 음수가 될 수 없습니다.");
                    if (throughput.inboundPerDay + throughput.outboundPerDay <= 0)
                        errors.Add("입고 또는 출고량을 입력하세요.");
                    if (throughput.peakFactor < 1f) errors.Add("피크 계수는 1.0 이상이어야 합니다.");
                    if (throughput.operatingHours <= 0 || throughput.operatingHours > 24)
                        errors.Add("가동시간은 0~24시간 사이여야 합니다.");
                    if (throughput.inventoryUnits <= 0) errors.Add("보관 필요량은 1 이상이어야 합니다.");
                    break;
                case 2:
                    if (building.widthM < 10 || building.depthM < 10) errors.Add("건물 폭/깊이는 10m 이상이어야 합니다.");
                    if (building.clearHeightM < 3) errors.Add("유효 층고는 3m 이상이어야 합니다.");
                    if (cargo.heightMm / 1000f >= building.clearHeightM) errors.Add("화물 높이가 층고보다 큽니다.");
                    if (building.inboundDocks < 1 || building.outboundDocks < 1) errors.Add("입고/출고 도크는 각각 1개 이상이어야 합니다.");
                    break;
                case 3:
                    bool anyStorage = false, anyHandling = false;
                    foreach (var d in EquipmentCatalog.All)
                    {
                        if (!IsAllowed(d)) continue;
                        if (d.Category == EquipmentCategory.Storage) anyStorage = true;
                        if (d.Category == EquipmentCategory.Handling || d.Category == EquipmentCategory.Conveying) anyHandling = true;
                    }
                    if (!anyStorage) errors.Add("보관 설비를 하나 이상 허용하세요.");
                    if (!anyHandling) errors.Add("운반 또는 이송 설비를 하나 이상 허용하세요.");
                    break;
            }
            return errors;
        }

        public static string UnitName(LoadUnitType t)
        {
            switch (t)
            {
                case LoadUnitType.Pallet: return "파렛트";
                case LoadUnitType.Tote: return "토트";
                case LoadUnitType.Carton: return "카톤박스";
                default: return t.ToString();
            }
        }

        public static string AutomationName(AutomationPreference a)
        {
            switch (a)
            {
                case AutomationPreference.Manual: return "수동 위주";
                case AutomationPreference.SemiAuto: return "반자동";
                case AutomationPreference.FullAuto: return "자동화 우선";
                default: return a.ToString();
            }
        }

        public string ToSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[화물]  {UnitName(cargo.unitType)}  {cargo.lengthMm:0}×{cargo.widthMm:0}×{cargo.heightMm:0} mm,  {cargo.weightKg:0.#} kg,  SKU {cargo.skuCount:N0}개,  단적 {(cargo.stackable ? "가능" : "불가")}");
            sb.AppendLine($"[입출고]  입고 {throughput.inboundPerDay:N0}/일,  출고 {throughput.outboundPerDay:N0}/일,  피크 ×{throughput.peakFactor:0.##},  가동 {throughput.operatingHours:0.#}시간");
            sb.AppendLine($"          피크 시간당 처리 {throughput.PeakTotalPerHour:N0}건,  보관 필요량 {throughput.inventoryUnits:N0} 단위");
            sb.AppendLine($"[건물]  {building.widthM:0.#} × {building.depthM:0.#} m (면적 {building.AreaM2:N0} m²),  유효 층고 {building.clearHeightM:0.#} m,  도크 입고 {building.inboundDocks} / 출고 {building.outboundDocks}");
            sb.Append($"[설비]  {AutomationName(preference.automation)},  사용 가능: ");
            var names = new List<string>();
            foreach (var d in EquipmentCatalog.All)
                if (IsAllowed(d)) names.Add(d.NameKo);
            sb.Append(names.Count > 0 ? string.Join(", ", names) : "없음");
            return sb.ToString();
        }
    }

    /// <summary>마지막 입력값을 JSON으로 저장/불러오기</summary>
    public static class InputStorage
    {
        static string FilePath => Path.Combine(Application.persistentDataPath, "last_input.json");

        public static bool Exists => File.Exists(FilePath);

        public static void Save(ProjectInput input)
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(input, true)); }
            catch (Exception e) { Debug.LogWarning($"[LayoutRecommender] 입력 저장 실패: {e.Message}"); }
        }

        public static ProjectInput Load()
        {
            try
            {
                if (!Exists) return null;
                return JsonUtility.FromJson<ProjectInput>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LayoutRecommender] 입력 불러오기 실패: {e.Message}");
                return null;
            }
        }
    }
}
