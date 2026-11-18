using System;
using System.Collections.Generic;
using LayoutRecommender.Data;
using UnityEngine;

namespace LayoutRecommender.RL
{
    /// <summary>배치 대상이 되는 기능 구역. 흐름 순서(입고→보관→피킹→포장→출고)대로 정의한다.</summary>
    public enum LayoutZone
    {
        Receiving = 0, // 입고 검수/스테이징
        Storage = 1,   // 보관 (랙/자동창고 블록)
        Picking = 2,   // 피킹
        Packing = 3,   // 포장/검수
        Shipping = 4   // 출고 스테이징
    }

    /// <summary>배치할 구역 하나. 크기는 격자 셀 단위.</summary>
    [Serializable]
    public class ZoneSpec
    {
        public LayoutZone Zone;
        public int W;             // 회전 0일 때 가로 셀 수
        public int H;             // 회전 0일 때 세로 셀 수
        public string Label;      // 화면 표시용 (예: "보관 · 랙 (선택식)")
        public Color Color;
        public int Cells => W * H;
    }

    /// <summary>
    /// 강화학습 환경이 푸는 문제 하나.
    /// 격자 크기는 신경망 입력 크기와 같아야 하므로 고정(GridW × GridH)이고,
    /// 실제 건물은 셀 크기(CellW/CellD, m)로 맞춘다.
    /// </summary>
    public class LayoutProblem
    {
        public const int GridW = 16;
        public const int GridH = 10;
        public const int ZoneTypeCount = 5;
        public const int MaxZones = 5;

        public float CellW = 7.5f;    // 셀 가로 (m)
        public float CellD = 8f;      // 셀 세로 (m)
        public float CellM => Mathf.Sqrt(CellW * CellD); // 거리 계산용 대표 셀 길이

        /// <summary>기둥 등 배치·통행 불가 셀</summary>
        public bool[,] Obstacle = new bool[GridW, GridH];
        /// <summary>입고 도크 셀 (왼쪽 벽 x=0)</summary>
        public List<Vector2Int> InboundDocks = new List<Vector2Int>();
        /// <summary>출고 도크 셀 (오른쪽 벽 x=GridW-1)</summary>
        public List<Vector2Int> OutboundDocks = new List<Vector2Int>();
        /// <summary>배치 순서대로의 구역 목록</summary>
        public List<ZoneSpec> Zones = new List<ZoneSpec>();

        /// <summary>
        /// 물동량 (단위/일). 노드 인덱스: 0 = 입고 도크, 1 = 출고 도크, 2 + i = Zones[i].
        /// </summary>
        public float[,] Flow;

        public int NodeCount => 2 + Zones.Count;
        public const int NodeInDock = 0, NodeOutDock = 1;
        public static int NodeOfZone(int zoneIndex) => 2 + zoneIndex;

        public float TotalFlow
        {
            get
            {
                float s = 0f;
                for (int a = 0; a < NodeCount; a++)
                    for (int b = 0; b < NodeCount; b++) s += Flow[a, b];
                return s;
            }
        }

        public int IndexOfZone(LayoutZone z)
        {
            for (int i = 0; i < Zones.Count; i++) if (Zones[i].Zone == z) return i;
            return -1;
        }

        public static string ZoneName(LayoutZone z)
        {
            switch (z)
            {
                case LayoutZone.Receiving: return "입고";
                case LayoutZone.Storage: return "보관";
                case LayoutZone.Picking: return "피킹";
                case LayoutZone.Packing: return "포장";
                case LayoutZone.Shipping: return "출고";
                default: return z.ToString();
            }
        }

        public static Color ZoneColor(LayoutZone z)
        {
            switch (z)
            {
                case LayoutZone.Receiving: return new Color(0.35f, 0.70f, 0.95f);
                case LayoutZone.Storage: return new Color(0.85f, 0.55f, 0.20f);
                case LayoutZone.Picking: return new Color(0.35f, 0.80f, 0.55f);
                case LayoutZone.Packing: return new Color(0.75f, 0.45f, 0.85f);
                case LayoutZone.Shipping: return new Color(0.95f, 0.45f, 0.45f);
                default: return Color.gray;
            }
        }

        /// <summary>입고 → 보관 → 피킹 → 포장 → 출고 직렬 흐름으로 물동량 행렬을 채운다.</summary>
        void BuildSerialFlow(float inboundPerDay, float outboundPerDay)
        {
            Flow = new float[NodeCount, NodeCount];
            int rcv = IndexOfZone(LayoutZone.Receiving), sto = IndexOfZone(LayoutZone.Storage);
            int pck = IndexOfZone(LayoutZone.Picking), pak = IndexOfZone(LayoutZone.Packing);
            int shp = IndexOfZone(LayoutZone.Shipping);

            var flow = Flow;
            Action<int, int, float> Add = (a, b, f) => { if (a >= 0 && b >= 0 && a != b) flow[a, b] += f; };
            Func<int, int> N = zi => zi < 0 ? -1 : NodeOfZone(zi);

            // 입고 흐름
            int inFirst = rcv >= 0 ? N(rcv) : N(sto);
            Add(NodeInDock, inFirst, inboundPerDay);
            if (rcv >= 0) Add(N(rcv), N(sto), inboundPerDay);

            // 출고 흐름: 보관 → (피킹) → (포장) → (출고) → 출고 도크
            var chain = new List<int>();
            foreach (var zi in new[] { sto, pck, pak, shp }) if (zi >= 0) chain.Add(N(zi));
            for (int i = 0; i + 1 < chain.Count; i++) Add(chain[i], chain[i + 1], outboundPerDay);
            if (chain.Count > 0) Add(chain[chain.Count - 1], NodeOutDock, outboundPerDay);
        }

        // ------------------------------------------------------------------
        // 문제 생성
        // ------------------------------------------------------------------

        /// <summary>
        /// 사용자 입력(ProjectInput)에서 문제를 만든다.
        /// 면적 산정은 프로토타입용 대략값이며, 합계가 격자의 maxFill을 넘으면 비율대로 줄인다.
        /// </summary>
        public static LayoutProblem FromInput(ProjectInput input, float maxFill = 0.5f)
        {
            var p = new LayoutProblem();
            var b = input.building;
            p.CellW = Mathf.Max(1f, b.widthM / GridW);
            p.CellD = Mathf.Max(1f, b.depthM / GridH);
            float cellArea = p.CellW * p.CellD;

            var cargo = input.cargo;
            var tp = input.throughput;
            float unitArea = Mathf.Max(0.05f, cargo.lengthMm * cargo.widthMm / 1e6f);
            int levels = Mathf.Max(1, input.EstimatedRackLevels());

            // 보관 설비: 허용된 보관 설비 중 자동화 선호에 맞는 것을 고른다
            EquipmentDef storageDef = null;
            foreach (var d in EquipmentCatalog.InCategory(EquipmentCategory.Storage))
            {
                if (!input.IsAllowed(d)) continue;
                bool wantAuto = input.preference.automation == AutomationPreference.FullAuto;
                if (storageDef == null || (wantAuto && d.Automated && !storageDef.Automated)) storageDef = d;
            }
            // 통로 포함 보관 1단위당 바닥 면적 계수: 선택식 랙 ≈ 2.0, 고밀도(셔틀/AS-RS) ≈ 1.3
            float storageFactor = storageDef != null && storageDef.Automated ? 1.3f : 2.0f;

            float peakIn = tp.AvgInboundPerHour * tp.peakFactor;
            float peakOut = tp.AvgOutboundPerHour * tp.peakFactor;

            float aStorage = tp.inventoryUnits / (float)levels * unitArea * storageFactor;
            float aReceiving = peakIn * unitArea * 1.5f + b.inboundDocks * 15f;
            float aShipping = peakOut * unitArea * 1.5f + b.outboundDocks * 15f;
            float aPicking = cargo.unitType == LoadUnitType.Pallet
                ? peakOut * unitArea * 2f
                : Mathf.Max(60f, cargo.skuCount * 0.4f);
            var worker = EquipmentCatalog.Get(EquipmentType.Worker);
            float workers = Mathf.Ceil(peakOut / Mathf.Max(1f, worker.CyclesPerHour));
            float aPacking = Mathf.Max(40f, workers * 12f);

            var areas = new List<(LayoutZone zone, float area, string label)>
            {
                (LayoutZone.Receiving, aReceiving, "입고"),
                (LayoutZone.Storage, aStorage, storageDef != null ? $"보관 · {storageDef.NameKo}" : "보관"),
                (LayoutZone.Picking, aPicking, "피킹"),
                (LayoutZone.Packing, aPacking, "포장"),
                (LayoutZone.Shipping, aShipping, "출고"),
            };

            // 면적 → 셀 수, 격자 대비 너무 크면 비율대로 축소
            float totalCells = 0f;
            foreach (var a in areas) totalCells += a.area / cellArea;
            float limit = GridW * GridH * maxFill;
            float scale = totalCells > limit ? limit / totalCells : 1f;
            if (scale < 1f)
                Debug.LogWarning($"[LayoutRL] 필요 면적이 건물의 {maxFill * 100:0}%를 넘어 구역 크기를 {scale * 100:0}%로 줄였습니다.");

            foreach (var a in areas)
            {
                int cells = Mathf.Max(1, Mathf.RoundToInt(a.area / cellArea * scale));
                var (w, h) = ShapeFor(cells);
                p.Zones.Add(new ZoneSpec { Zone = a.zone, W = w, H = h, Label = a.label, Color = ZoneColor(a.zone) });
            }

            p.InboundDocks = DockCells(0, b.inboundDocks);
            p.OutboundDocks = DockCells(GridW - 1, b.outboundDocks);
            p.BuildSerialFlow(tp.inboundPerDay, tp.outboundPerDay);
            return p;
        }

        /// <summary>
        /// 학습용 무작위 문제. 구역 크기, 물동량, 도크 위치, 기둥을 매 에피소드 바꿔서
        /// 특정 건물 하나에만 맞춰지지 않도록 한다 (도메인 무작위화).
        /// zoneCount를 3~5로 두면 커리큘럼 학습에 쓸 수 있다.
        /// </summary>
        public static LayoutProblem Random(System.Random rng, int zoneCount = MaxZones, float maxFill = 0.45f, int maxObstacles = 4)
        {
            zoneCount = Mathf.Clamp(zoneCount, 2, MaxZones);
            var p = new LayoutProblem();

            // 보관은 항상 포함, 나머지는 흐름 순서를 유지한 채 앞에서부터 고른다
            var order = new List<LayoutZone> { LayoutZone.Receiving, LayoutZone.Storage, LayoutZone.Picking, LayoutZone.Packing, LayoutZone.Shipping };
            var chosen = new List<LayoutZone> { LayoutZone.Storage };
            var others = new List<LayoutZone> { LayoutZone.Receiving, LayoutZone.Shipping, LayoutZone.Picking, LayoutZone.Packing };
            for (int i = 0; i < zoneCount - 1; i++) chosen.Add(others[i]);
            chosen.Sort((a, c) => order.IndexOf(a).CompareTo(order.IndexOf(c)));

            float limit = GridW * GridH * maxFill;
            float storageShare = 0.45f + (float)rng.NextDouble() * 0.3f; // 보관이 전체의 45~75%
            float rest = (1f - storageShare) / Mathf.Max(1, chosen.Count - 1);
            float fill = limit * (0.6f + 0.4f * (float)rng.NextDouble());
            foreach (var z in chosen)
            {
                float share = z == LayoutZone.Storage ? storageShare : rest * (0.6f + 0.8f * (float)rng.NextDouble());
                int cells = Mathf.Max(1, Mathf.RoundToInt(fill * share));
                var (w, h) = ShapeFor(cells, 1f + (float)rng.NextDouble());
                p.Zones.Add(new ZoneSpec { Zone = z, W = w, H = h, Label = ZoneName(z), Color = ZoneColor(z) });
            }

            p.InboundDocks = RandomDockCells(rng, 0);
            p.OutboundDocks = RandomDockCells(rng, GridW - 1);

            int obstacles = rng.Next(0, maxObstacles + 1);
            for (int i = 0; i < obstacles; i++)
            {
                int x = rng.Next(2, GridW - 2), y = rng.Next(1, GridH - 1);
                p.Obstacle[x, y] = true;
            }

            float inbound = 200f + (float)rng.NextDouble() * 800f;
            float outbound = inbound * (0.7f + 0.6f * (float)rng.NextDouble());
            p.BuildSerialFlow(inbound, outbound);
            return p;
        }

        /// <summary>셀 수를 가로세로 비율(aspect = 가로/세로)에 가깝고 격자에 들어가는 직사각형으로 바꾼다.</summary>
        static (int w, int h) ShapeFor(int cells, float aspect = 1.4f)
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(cells * aspect)), 1, GridW - 2);
            int h = Mathf.Clamp(Mathf.CeilToInt(cells / (float)w), 1, GridH - 2);
            if (w * h < cells) w = Mathf.Clamp(Mathf.CeilToInt(cells / (float)h), 1, GridW - 2);
            return (w, h);
        }

        static List<Vector2Int> DockCells(int x, int docks)
        {
            // 도크 수가 많아도 격자에서는 벽 가운데 최대 4셀로 표현
            int n = Mathf.Clamp(Mathf.CeilToInt(docks / 2f), 1, 4);
            int start = (GridH - n) / 2;
            var list = new List<Vector2Int>();
            for (int i = 0; i < n; i++) list.Add(new Vector2Int(x, start + i));
            return list;
        }

        static List<Vector2Int> RandomDockCells(System.Random rng, int x)
        {
            int n = rng.Next(1, 4);
            int start = rng.Next(0, GridH - n + 1);
            var list = new List<Vector2Int>();
            for (int i = 0; i < n; i++) list.Add(new Vector2Int(x, start + i));
            return list;
        }
    }
}
