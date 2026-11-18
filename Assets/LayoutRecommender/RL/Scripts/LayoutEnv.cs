using System.Collections.Generic;
using UnityEngine;

namespace LayoutRecommender.RL
{
    /// <summary>배치된 구역 하나 (좌상단 기준 셀 좌표, 회전 반영된 크기)</summary>
    public struct PlacedZone
    {
        public int X, Y, W, H;
        public bool Rotated;
        public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
    }

    /// <summary>
    /// 순차 배치 환경 (ML-Agents와 무관한 순수 C#).
    /// 한 스텝에 구역 하나를 (x, y, 회전)으로 놓는다. 행동 인덱스 = (y * GridW + x) * 2 + 회전.
    /// - 유효 행동: 격자 안, 빈 칸, 그리고 놓은 뒤에도 모든 도크와 구역이 하나로 이어진 통로에 닿아 있을 것
    /// - 비용: Σ 물동량 × 통로 최단거리(m). 비용이 낮을수록 좋다.
    /// </summary>
    public class LayoutEnv
    {
        public const int GridW = LayoutProblem.GridW;
        public const int GridH = LayoutProblem.GridH;
        public const int ActionCount = GridW * GridH * 2;

        // 셀 상태
        public const int Free = -1, Blocked = -2, Dock = -3;

        public LayoutProblem Problem { get; private set; }
        public readonly int[,] Cell = new int[GridW, GridH]; // Free / Blocked / Dock / 구역 인덱스(0..)
        public readonly List<PlacedZone> Placed = new List<PlacedZone>();

        public int CurrentIndex => Placed.Count;
        public bool Done => Problem != null && Placed.Count >= Problem.Zones.Count;
        public ZoneSpec Current => Done ? null : Problem.Zones[CurrentIndex];

        readonly bool[] _valid = new bool[ActionCount];
        bool _validDirty = true;
        int _validCount;

        // BFS 버퍼
        readonly int[,] _dist = new int[GridW, GridH];
        readonly Queue<Vector2Int> _queue = new Queue<Vector2Int>();
        static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        public void Reset(LayoutProblem problem)
        {
            Problem = problem;
            Placed.Clear();
            for (int x = 0; x < GridW; x++)
                for (int y = 0; y < GridH; y++)
                    Cell[x, y] = problem.Obstacle[x, y] ? Blocked : Free;
            foreach (var d in problem.InboundDocks) Cell[d.x, d.y] = Dock;
            foreach (var d in problem.OutboundDocks) Cell[d.x, d.y] = Dock;
            _validDirty = true;
        }

        // ------------------------------------------------------------------
        // 행동 인코딩
        // ------------------------------------------------------------------

        public static int Encode(int x, int y, bool rotated) => (y * GridW + x) * 2 + (rotated ? 1 : 0);

        public static void Decode(int action, out int x, out int y, out bool rotated)
        {
            rotated = (action & 1) == 1;
            int cell = action >> 1;
            x = cell % GridW;
            y = cell / GridW;
        }

        // ------------------------------------------------------------------
        // 유효 행동 (action masking)
        // ------------------------------------------------------------------

        public bool IsValid(int action) { EnsureValid(); return _valid[action]; }
        public int ValidCount { get { EnsureValid(); return _validCount; } }
        public bool HasValidAction => ValidCount > 0;

        void EnsureValid()
        {
            if (!_validDirty) return;
            _validDirty = false;
            _validCount = 0;
            var z = Current;
            for (int a = 0; a < ActionCount; a++)
            {
                _valid[a] = z != null && CheckPlacement(a, z);
                if (_valid[a]) _validCount++;
            }
        }

        bool CheckPlacement(int action, ZoneSpec z)
        {
            Decode(action, out int x, out int y, out bool rot);
            int w = rot ? z.H : z.W, h = rot ? z.W : z.H;
            if (rot && z.W == z.H) return false;          // 정사각형은 회전이 같은 결과 → 중복 행동 제거
            if (x + w > GridW || y + h > GridH) return false;
            for (int i = x; i < x + w; i++)
                for (int j = y; j < y + h; j++)
                    if (Cell[i, j] != Free) return false;

            // 임시로 놓고 통로 연결성 확인
            int idx = Placed.Count;
            Fill(x, y, w, h, idx);
            bool ok = AllConnected(idx + 1);
            Fill(x, y, w, h, Free);
            return ok;
        }

        void Fill(int x, int y, int w, int h, int value)
        {
            for (int i = x; i < x + w; i++)
                for (int j = y; j < y + h; j++)
                    Cell[i, j] = value;
        }

        /// <summary>도크와 구역 0..zoneCount-1이 모두 같은 통로(빈 칸 연결 요소)에 닿아 있는지</summary>
        bool AllConnected(int zoneCount)
        {
            // 입고 도크에서 시작해 통행 가능한 칸(빈 칸, 도크)을 BFS
            Bfs(Problem.InboundDocks);
            foreach (var d in Problem.OutboundDocks)
                if (_dist[d.x, d.y] < 0) return false;
            for (int k = 0; k < zoneCount; k++)
                if (AccessDistance(k) < 0) return false;
            return true;
        }

        static bool Walkable(int c) => c == Free || c == Dock;

        void Bfs(List<Vector2Int> sources)
        {
            for (int x = 0; x < GridW; x++)
                for (int y = 0; y < GridH; y++) _dist[x, y] = -1;
            _queue.Clear();
            foreach (var s in sources) { _dist[s.x, s.y] = 0; _queue.Enqueue(s); }
            while (_queue.Count > 0)
            {
                var c = _queue.Dequeue();
                foreach (var d in Dirs)
                {
                    var n = c + d;
                    if (n.x < 0 || n.y < 0 || n.x >= GridW || n.y >= GridH) continue;
                    if (_dist[n.x, n.y] >= 0 || !Walkable(Cell[n.x, n.y])) continue;
                    _dist[n.x, n.y] = _dist[c.x, c.y] + 1;
                    _queue.Enqueue(n);
                }
            }
        }

        /// <summary>마지막 BFS 기준으로, 구역 k의 가장자리에 붙은 통로 칸까지의 최소 거리 (닿지 않으면 -1)</summary>
        int AccessDistance(int k)
        {
            int best = -1;
            for (int x = 0; x < GridW; x++)
                for (int y = 0; y < GridH; y++)
                {
                    if (Cell[x, y] != k) continue;
                    foreach (var d in Dirs)
                    {
                        int nx = x + d.x, ny = y + d.y;
                        if (nx < 0 || ny < 0 || nx >= GridW || ny >= GridH) continue;
                        int v = _dist[nx, ny];
                        if (v >= 0 && (best < 0 || v < best)) best = v;
                    }
                }
            return best;
        }

        List<Vector2Int> AccessCells(int node)
        {
            if (node == LayoutProblem.NodeInDock) return Problem.InboundDocks;
            if (node == LayoutProblem.NodeOutDock) return Problem.OutboundDocks;
            int k = node - 2;
            var list = new List<Vector2Int>();
            for (int x = 0; x < GridW; x++)
                for (int y = 0; y < GridH; y++)
                {
                    if (Cell[x, y] != k) continue;
                    foreach (var d in Dirs)
                    {
                        int nx = x + d.x, ny = y + d.y;
                        if (nx < 0 || ny < 0 || nx >= GridW || ny >= GridH) continue;
                        if (Walkable(Cell[nx, ny])) list.Add(new Vector2Int(nx, ny));
                    }
                }
            return list;
        }

        bool NodePlaced(int node) => node < 2 || node - 2 < Placed.Count;

        // ------------------------------------------------------------------
        // 배치와 비용
        // ------------------------------------------------------------------

        /// <summary>현재 구역을 놓는다. 유효하지 않으면 false.</summary>
        public bool Apply(int action)
        {
            if (Done || !IsValid(action)) return false;
            Decode(action, out int x, out int y, out bool rot);
            var z = Current;
            int w = rot ? z.H : z.W, h = rot ? z.W : z.H;
            Fill(x, y, w, h, Placed.Count);
            Placed.Add(new PlacedZone { X = x, Y = y, W = w, H = h, Rotated = rot });
            _validDirty = true;
            return true;
        }

        /// <summary>
        /// 현재 구역을 action 위치에 놓았다고 가정한 PartialCost. 상태와 유효 행동 캐시는 그대로 둔다.
        /// 기준선(탐욕 배치)과 휴리스틱에서 쓴다.
        /// </summary>
        public float CostIfPlaced(int action)
        {
            Decode(action, out int x, out int y, out bool rot);
            var z = Current;
            int w = rot ? z.H : z.W, h = rot ? z.W : z.H;
            Fill(x, y, w, h, Placed.Count);
            Placed.Add(new PlacedZone { X = x, Y = y, W = w, H = h, Rotated = rot });
            float c = PartialCost();
            Placed.RemoveAt(Placed.Count - 1);
            Fill(x, y, w, h, Free);
            return c;
        }

        /// <summary>
        /// 지금까지 놓인 노드끼리의 물동량 × 통로거리 합 (단위·m/일).
        /// 구역 사이 거리 = 한 구역 가장자리 통로에서 다른 구역 가장자리 통로까지 BFS 거리 + 1셀(구역 내부 이동 근사).
        /// </summary>
        public float PartialCost()
        {
            var p = Problem;
            float cost = 0f;
            for (int a = 0; a < p.NodeCount; a++)
            {
                if (!NodePlaced(a)) continue;
                bool any = false;
                for (int b = 0; b < p.NodeCount; b++) if (p.Flow[a, b] > 0 && NodePlaced(b)) { any = true; break; }
                if (!any) continue;

                Bfs(AccessCells(a));
                for (int b = 0; b < p.NodeCount; b++)
                {
                    float f = p.Flow[a, b];
                    if (f <= 0 || !NodePlaced(b)) continue;
                    int dist = MinDistTo(b);
                    if (dist < 0) dist = GridW + GridH; // 이론상 마스킹으로 막히지만 안전값
                    cost += f * (dist + 1) * p.CellM;
                }
            }
            return cost;
        }

        int MinDistTo(int node)
        {
            int best = -1;
            foreach (var c in AccessCells(node))
            {
                int v = _dist[c.x, c.y];
                if (v >= 0 && (best < 0 || v < best)) best = v;
            }
            return best;
        }

        /// <summary>보상 정규화용 상한: 모든 물동량이 격자 둘레 거리만큼 이동한다고 가정</summary>
        public float CostScale => Mathf.Max(1f, Problem.TotalFlow * (GridW + GridH) * Problem.CellM);

        /// <summary>점유율 (배치된 구역 셀 / 전체 셀)</summary>
        public float FillRatio
        {
            get
            {
                int n = 0;
                foreach (var z in Placed) n += z.W * z.H;
                return n / (float)(GridW * GridH);
            }
        }
    }
}
