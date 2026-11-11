using System.Collections.Generic;
using System.Text;
using LayoutRecommender.Data;
using UnityEngine;

namespace LayoutRecommender.Planning
{
    /// <summary>3D로 그릴 모양</summary>
    public enum ItemShape
    {
        RackRow,       // 랙 한 열 (길쭉한 선반)
        ShuttleBlock,  // 셔틀랙 블록
        Mast,          // 스태커크레인 마스트
        Conveyor,      // 컨베이어 구간
        Vehicle,       // 지게차, AGV, AMR, 파렛트트럭, 셔틀 차량
        Person         // 작업자
    }

    /// <summary>배치될 설비 하나 (월드 좌표, m 단위. x: 건물 폭 방향, z: 건물 깊이 방향, 바닥 y=0)</summary>
    public class PlacedItem
    {
        public EquipmentType Type;
        public ItemShape Shape;
        public Vector3 Center;   // 바닥 중심 기준 (y는 0)
        public Vector3 Size;     // 폭(x) × 높이(y) × 길이(z)
        public string Label;     // 예: "랙 열 3"
        public string Detail;    // 속성창에 보일 설명

        // 랙/블록 그리기용
        public int Levels;           // 단 수
        public float LevelPitch;     // 단 간격 (m)
        public float SlotWidth;      // 칸 폭 (m, 길이 방향)
        public LoadUnitType Unit;    // 적재 단위
        public float Fill = 0.7f;    // 적재율 (표시용)

        // 시뮬레이션용
        public bool IsMover;         // 화물을 나르는 운반 설비/작업자
        public int AisleIndex = -1;  // 스태커크레인이 맡은 통로
        public float RailCenterZ;    // 크레인 레일 중심 z
        public float RailLength;     // 크레인 레일 길이
    }

    /// <summary>운반 설비가 화물을 주고받는 보관 쪽 지점</summary>
    public class AccessPoint
    {
        public float X;              // 접근 통로 x
        public float ZIn;            // 입고 화물을 내려놓는 z
        public float ZOut;           // 출고 화물을 집는 z
        public float SlotZMin, SlotZMax; // 통로 안으로 들어가는 경우 랜덤 위치 범위 (선택식 랙)
        public bool IntoAisle;       // true: 통로 안 임의 위치까지 들어감
        public int AisleIndex = -1;  // 크레인 통로 번호 (자동창고)
    }

    /// <summary>
    /// 이동 경로망: 입고/출고 횡단 통로(가로선)와 도크·보관 접근 지점을 잇는 세로선.
    /// 바닥 경로 표시와 시뮬레이션 경로 계산에 같이 쓴다.
    /// </summary>
    public class FlowNetwork
    {
        public float CorridorInZ;    // 입고 횡단 통로
        public float CorridorOutZ;   // 출고 횡단 통로
        public float InDockQueueZ;   // 입고 화물이 내려지는 위치 (도크 앞)
        public float OutDockQueueZ;  // 출고 화물을 모아두는 위치 (도크 앞)
        public readonly List<float> InDockX = new List<float>();
        public readonly List<float> OutDockX = new List<float>();
        public readonly List<AccessPoint> Access = new List<AccessPoint>();
        public float HandlingSeconds = 20f; // 싣기/내리기 시간
    }

    /// <summary>바닥에 칠할 구역 (입고 도크, 입고 대기, 보관, 출고 대기, 출고 도크)</summary>
    public class ZoneArea
    {
        public string Name;
        public Rect Area;        // x, z 평면 (xMin, zMin, width, depth)
        public Color Color;
    }

    /// <summary>도크 도어 위치</summary>
    public class DockDoor
    {
        public bool Inbound;
        public Vector3 Center;
        public float Width;
    }

    /// <summary>배치안 하나. 규모 산정 결과와 점수를 함께 가진다.</summary>
    public class LayoutPlan
    {
        public string Name;                        // 예: "B안 · 스태커크레인 (더블포크)"
        public string Concept;                     // 한 줄 설명
        public EquipmentType StorageType;
        public int AutomationLevel;                // 0 수동, 1 반자동, 2 자동
        public readonly List<PlacedItem> Items = new List<PlacedItem>();
        public readonly List<ZoneArea> Zones = new List<ZoneArea>();
        public readonly List<DockDoor> Docks = new List<DockDoor>();
        public readonly Dictionary<EquipmentType, int> Counts = new Dictionary<EquipmentType, int>();
        public readonly List<string> Notes = new List<string>();
        public FlowNetwork Flow = new FlowNetwork();

        public int Levels;
        public int PhysicalPositions;              // 물리적 보관 위치 수
        public int UsablePositions;                // SKU 분산을 고려한 유효 보관 위치 수
        public float ThroughputCapacity;           // 시간당 처리 가능 건수
        public float RequiredThroughput;           // 피크 시간당 필요 건수
        public int RequiredPositions;              // 보관 필요량
        public float StorageAreaM2;                // 보관 설비 점유 바닥 면적
        public float CapexEok;                     // 개략 투자비 (억원, 참고용)
        public int Workers;                        // 운영 인원 (교대당)
        public float Score;                        // 0~100
        public readonly Dictionary<string, float> ScoreParts = new Dictionary<string, float>();
        public bool Recommended;

        public float StorageFit => RequiredPositions > 0 ? (float)UsablePositions / RequiredPositions : 0f;
        public float ThroughputFit => RequiredThroughput > 0 ? ThroughputCapacity / RequiredThroughput : 0f;

        public void AddCount(EquipmentType t, int n)
        {
            if (n <= 0) return;
            Counts.TryGetValue(t, out int c);
            Counts[t] = c + n;
        }

        public int CountOf(EquipmentType t) => Counts.TryGetValue(t, out int c) ? c : 0;

        public string ToDetailText()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Concept);
            sb.AppendLine();
            sb.AppendLine($"점수  {Score:0} / 100{(Recommended ? "   ★ 추천" : "")}");
            foreach (var kv in ScoreParts) sb.AppendLine($"   · {kv.Key} {kv.Value:0.#}");
            sb.AppendLine();
            sb.AppendLine($"보관 위치  {UsablePositions:N0} / 필요 {RequiredPositions:N0}  ({StorageFit * 100f:0}%)");
            if (PhysicalPositions != UsablePositions) sb.AppendLine($"   (물리 위치 {PhysicalPositions:N0}, SKU 분산 반영)");
            sb.AppendLine($"적재 단수  {Levels}단");
            sb.AppendLine($"처리능력  {ThroughputCapacity:0} / 필요 {RequiredThroughput:0} 건/시간  ({ThroughputFit * 100f:0}%)");
            sb.AppendLine($"보관 점유 면적  {StorageAreaM2:N0} m²");
            sb.AppendLine($"개략 투자비  {CapexEok:0.#} 억원 (참고용)");
            sb.AppendLine($"운영 인원  {Workers}명 / 교대");
            sb.AppendLine();
            sb.AppendLine("설비 구성");
            foreach (var kv in Counts)
            {
                var def = EquipmentCatalog.Get(kv.Key);
                sb.AppendLine($"   · {def.NameKo}  {kv.Value}{UnitOf(kv.Key)}");
            }
            if (Notes.Count > 0)
            {
                sb.AppendLine();
                foreach (var n in Notes) sb.AppendLine("※ " + n);
            }
            return sb.ToString();
        }

        static string UnitOf(EquipmentType t)
        {
            switch (t)
            {
                case EquipmentType.SelectiveRack: return "열";
                case EquipmentType.ShuttleRack: return "블록";
                case EquipmentType.Conveyor: return "구간";
                case EquipmentType.Worker: return "명";
                default: return "대";
            }
        }
    }
}
