using System;
using System.Collections.Generic;
using UnityEngine;

namespace LayoutRecommender.Data
{
    /// <summary>설비 카테고리 (HUD 좌측 팔레트의 그룹)</summary>
    public enum EquipmentCategory
    {
        Storage,    // 보관
        Handling,   // 운반
        Conveying,  // 이송
        Workforce   // 인력
    }

    /// <summary>프로토타입에서 다루는 설비 종류</summary>
    public enum EquipmentType
    {
        SelectiveRack,       // 선반/파렛트 랙
        ShuttleRack,         // 셔틀랙
        StackerCraneSingle,  // 스태커크레인 (싱글포크)
        StackerCraneDouble,  // 스태커크레인 (더블포크)
        Forklift,            // 지게차
        PalletTruck,         // 파렛트트럭
        AGV,
        AMR,
        Conveyor,            // 컨베이어
        Worker               // 작업자
    }

    [Flags]
    public enum LoadUnitMask
    {
        None = 0,
        Pallet = 1,
        Tote = 2,
        Carton = 4,
        All = Pallet | Tote | Carton
    }

    /// <summary>설비 한 종류의 정의. 처리능력/규격 값은 프로토타입용 대표값이며 이후 보정 대상.</summary>
    public class EquipmentDef
    {
        public EquipmentType Type;
        public EquipmentCategory Category;
        public string NameKo;
        public string Icon;              // 팔레트 아이콘에 표시할 약어
        public Color Color;
        public LoadUnitMask Supports;
        public bool Automated;
        public float FootprintWidthM;    // 대표 점유 폭 (m)
        public float FootprintDepthM;    // 대표 점유 깊이 (m)
        public float SpeedMps;           // 이동 속도 (m/s), 고정 설비는 0
        public float CyclesPerHour;      // 대표 처리능력 (회/시간)
        public string Description;
    }

    public static class EquipmentCatalog
    {
        public static readonly IReadOnlyList<EquipmentDef> All = new List<EquipmentDef>
        {
            new EquipmentDef {
                Type = EquipmentType.SelectiveRack, Category = EquipmentCategory.Storage,
                NameKo = "랙 (선택식)", Icon = "RK", Color = new Color(0.85f, 0.55f, 0.20f),
                Supports = LoadUnitMask.All, Automated = false,
                FootprintWidthM = 2.7f, FootprintDepthM = 1.1f, SpeedMps = 0f, CyclesPerHour = 0f,
                Description = "모든 단위에 직접 접근 가능. SKU가 많고 회전이 높을 때 유리. 통로 면적이 큼."
            },
            new EquipmentDef {
                Type = EquipmentType.ShuttleRack, Category = EquipmentCategory.Storage,
                NameKo = "셔틀랙", Icon = "SH", Color = new Color(0.75f, 0.35f, 0.65f),
                Supports = LoadUnitMask.All, Automated = true,
                FootprintWidthM = 2.7f, FootprintDepthM = 12f, SpeedMps = 1.5f, CyclesPerHour = 40f,
                Description = "고밀도 보관. SKU당 수량이 많은 경우 유리. 레인 단위 선입선출/후입선출."
            },
            new EquipmentDef {
                Type = EquipmentType.StackerCraneSingle, Category = EquipmentCategory.Storage,
                NameKo = "스태커크레인 (싱글포크)", Icon = "SC1", Color = new Color(0.30f, 0.65f, 0.85f),
                Supports = LoadUnitMask.All, Automated = true,
                FootprintWidthM = 1.6f, FootprintDepthM = 40f, SpeedMps = 3.0f, CyclesPerHour = 30f,
                Description = "자동창고(AS/RS). 고층 보관에 적합. 통로당 1대, 1회 1단위 처리."
            },
            new EquipmentDef {
                Type = EquipmentType.StackerCraneDouble, Category = EquipmentCategory.Storage,
                NameKo = "스태커크레인 (더블포크)", Icon = "SC2", Color = new Color(0.20f, 0.50f, 0.80f),
                Supports = LoadUnitMask.Pallet | LoadUnitMask.Tote, Automated = true,
                FootprintWidthM = 1.8f, FootprintDepthM = 40f, SpeedMps = 3.0f, CyclesPerHour = 45f,
                Description = "1회 2단위 처리 또는 더블딥 보관. 처리량과 보관밀도가 모두 필요할 때."
            },
            new EquipmentDef {
                Type = EquipmentType.Forklift, Category = EquipmentCategory.Handling,
                NameKo = "지게차", Icon = "FL", Color = new Color(0.95f, 0.80f, 0.20f),
                Supports = LoadUnitMask.Pallet, Automated = false,
                FootprintWidthM = 1.2f, FootprintDepthM = 3.2f, SpeedMps = 2.5f, CyclesPerHour = 20f,
                Description = "파렛트 입출고, 랙 적치. 통로폭 3.5m 이상 필요."
            },
            new EquipmentDef {
                Type = EquipmentType.PalletTruck, Category = EquipmentCategory.Handling,
                NameKo = "파렛트트럭", Icon = "PT", Color = new Color(0.90f, 0.60f, 0.30f),
                Supports = LoadUnitMask.Pallet, Automated = false,
                FootprintWidthM = 0.8f, FootprintDepthM = 2.0f, SpeedMps = 1.5f, CyclesPerHour = 15f,
                Description = "도크와 스테이징 구역 간 단거리 수평 이동."
            },
            new EquipmentDef {
                Type = EquipmentType.AGV, Category = EquipmentCategory.Handling,
                NameKo = "AGV", Icon = "AGV", Color = new Color(0.35f, 0.80f, 0.55f),
                Supports = LoadUnitMask.Pallet | LoadUnitMask.Tote, Automated = true,
                FootprintWidthM = 1.2f, FootprintDepthM = 1.6f, SpeedMps = 1.5f, CyclesPerHour = 12f,
                Description = "정해진 경로를 따라 자동 운반. 파렛트/토트 반송."
            },
            new EquipmentDef {
                Type = EquipmentType.AMR, Category = EquipmentCategory.Handling,
                NameKo = "AMR", Icon = "AMR", Color = new Color(0.25f, 0.75f, 0.70f),
                Supports = LoadUnitMask.Tote | LoadUnitMask.Carton, Automated = true,
                FootprintWidthM = 0.7f, FootprintDepthM = 0.9f, SpeedMps = 1.8f, CyclesPerHour = 25f,
                Description = "자율주행 로봇. 피킹 지원, 토트/카톤 반송. 경로 변경이 유연함."
            },
            new EquipmentDef {
                Type = EquipmentType.Conveyor, Category = EquipmentCategory.Conveying,
                NameKo = "컨베이어", Icon = "CV", Color = new Color(0.60f, 0.60f, 0.65f),
                Supports = LoadUnitMask.All, Automated = true,
                FootprintWidthM = 1.2f, FootprintDepthM = 1.5f, SpeedMps = 0.5f, CyclesPerHour = 600f,
                Description = "구간 연속 이송. 1.5m 모듈 단위로 연결."
            },
            new EquipmentDef {
                Type = EquipmentType.Worker, Category = EquipmentCategory.Workforce,
                NameKo = "작업자", Icon = "WK", Color = new Color(0.95f, 0.45f, 0.45f),
                Supports = LoadUnitMask.All, Automated = false,
                FootprintWidthM = 0.6f, FootprintDepthM = 0.6f, SpeedMps = 1.2f, CyclesPerHour = 60f,
                Description = "피킹, 검수, 포장 작업. 카톤/토트 기준 시간당 처리 건수."
            },
        };

        public static EquipmentDef Get(EquipmentType type)
        {
            foreach (var d in All)
                if (d.Type == type) return d;
            return null;
        }

        public static IEnumerable<EquipmentDef> InCategory(EquipmentCategory category)
        {
            foreach (var d in All)
                if (d.Category == category) yield return d;
        }

        public static string CategoryName(EquipmentCategory c)
        {
            switch (c)
            {
                case EquipmentCategory.Storage: return "보관";
                case EquipmentCategory.Handling: return "운반";
                case EquipmentCategory.Conveying: return "이송";
                case EquipmentCategory.Workforce: return "인력";
                default: return c.ToString();
            }
        }

        public static LoadUnitMask ToMask(LoadUnitType unit)
        {
            switch (unit)
            {
                case LoadUnitType.Pallet: return LoadUnitMask.Pallet;
                case LoadUnitType.Tote: return LoadUnitMask.Tote;
                case LoadUnitType.Carton: return LoadUnitMask.Carton;
                default: return LoadUnitMask.None;
            }
        }

        public static bool Supports(EquipmentDef def, LoadUnitType unit)
        {
            return (def.Supports & ToMask(unit)) != 0;
        }
    }
}
