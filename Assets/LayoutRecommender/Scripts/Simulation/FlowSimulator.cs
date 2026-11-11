using System.Collections.Generic;
using System.Linq;
using System.Text;
using LayoutRecommender.Data;
using LayoutRecommender.Planning;
using LayoutRecommender.World;
using UnityEngine;

namespace LayoutRecommender.Simulation
{
    /// <summary>
    /// 입출고 흐름 시뮬레이션 (피크 시간 기준).
    /// 입고: 트럭 도착 → 도크 앞에 화물 하차 → 운반 설비가 경로를 따라 보관 지점까지 운반 → 적치 (자동창고는 크레인이 랙에 입고)
    /// 출고: 보관 지점에서 화물 인출 (자동창고는 크레인이 출고대로) → 경로를 따라 출고 도크 앞에 내려놓음 → 트럭 상차
    /// 운반 설비는 바닥 경로망(입고/출고 횡단 통로, 접근 통로, 건물 측면 통로)만 따라 움직인다.
    /// </summary>
    public class FlowSimulator
    {
        enum Phase { Idle, ToPickup, Loading, ToDrop, Unloading }

        class SimTask
        {
            public bool Inbound;
            public int Dock;
            public int DockSlot = -1;
            public AccessPoint Access;
            public float SlotZ;
            public GameObject Cargo;
        }

        class Mover
        {
            public EquipmentInstance Inst;
            public EquipmentDef Def;
            public Phase Phase = Phase.Idle;
            public List<Vector3> Path = new List<Vector3>();
            public int PathIndex;
            public SimTask Task;
            public float Timer;
            public float BusyTime;
            public Vector3 CarryOffset;
        }

        class CraneJob { public float Z, Y, Hold; }

        class Crane
        {
            public EquipmentInstance Inst;
            public readonly Queue<CraneJob> Jobs = new Queue<CraneJob>();
            public CraneJob Current;
            public float Hold;
            public float ZMin, ZMax, YMax;
            public float BusyTime;
        }

        const int SlotsPerDock = 6;
        const float ShipAfterSeconds = 180f; // 출고 도크 앞 화물이 트럭에 실리기까지

        readonly LayoutPlan _plan;
        readonly ProjectInput _in;
        readonly LayoutWorld _world;
        readonly FlowNetwork _f;
        readonly List<Mover> _movers = new List<Mover>();
        readonly Dictionary<int, Crane> _cranes = new Dictionary<int, Crane>();
        readonly List<SimTask> _waiting = new List<SimTask>();
        readonly GameObject[][] _inSlots;
        readonly GameObject[][] _outSlots;
        readonly float[][] _outSlotTime;
        readonly List<float> _crossLanes = new List<float>();
        readonly System.Random _rnd = new System.Random(20261111);
        readonly GameObject _cargoIn, _cargoOut;
        readonly float _cargoLen;
        readonly float _midZ;

        float _inAcc, _outAcc;

        public float SimTime { get; private set; }
        public int InCreated { get; private set; }
        public int OutCreated { get; private set; }
        public int InDone { get; private set; }
        public int OutDone { get; private set; }
        public bool HasMovers => _movers.Count > 0;

        // 재고: 보관 위치 수(용량) 대비 현재 수량. 출고는 재고가 있을 때만 생기고 입고는 용량까지만 들어간다.
        public int Capacity { get; private set; }
        public int Stock { get; private set; }
        public int StockOutSkipped { get; private set; }   // 재고가 없어 출고하지 못한 건수
        public int FullSkipped { get; private set; }       // 보관 위치가 꽉 차 입고하지 못한 건수
        public float StockLevel => Capacity > 0 ? (float)Stock / Capacity : 0f;

        public void SetStockLevel(float level)
        {
            Stock = Mathf.Clamp(Mathf.RoundToInt(Capacity * Mathf.Clamp01(level)), 0, Capacity);
        }

        public FlowSimulator(LayoutPlan plan, ProjectInput input, LayoutWorld world, IEnumerable<EquipmentInstance> instances, float stockLevel)
        {
            Capacity = Mathf.Max(1, plan.UsablePositions);
            SetStockLevel(stockLevel);
            _plan = plan;
            _in = input;
            _world = world;
            _f = plan.Flow;
            _midZ = (_f.CorridorInZ + _f.CorridorOutZ) / 2f;

            foreach (var inst in instances)
            {
                if (inst == null) continue;
                var it = inst.Item;
                if (it.IsMover)
                {
                    _movers.Add(new Mover
                    {
                        Inst = inst,
                        Def = EquipmentCatalog.Get(it.Type),
                        CarryOffset = ModelFactory.CarryOffset(it.Type, it.Size)
                    });
                }
                else if (it.Shape == ItemShape.Mast && it.AisleIndex >= 0)
                {
                    _cranes[it.AisleIndex] = new Crane
                    {
                        Inst = inst,
                        ZMin = it.RailCenterZ - it.RailLength / 2f + 1f,
                        ZMax = it.RailCenterZ + it.RailLength / 2f - 1f,
                        YMax = Mathf.Max(1f, it.Size.y - 1.5f)
                    };
                }
            }

            int inDocks = Mathf.Max(1, _f.InDockX.Count), outDocks = Mathf.Max(1, _f.OutDockX.Count);
            _inSlots = new GameObject[inDocks][];
            for (int i = 0; i < inDocks; i++) _inSlots[i] = new GameObject[SlotsPerDock];
            _outSlots = new GameObject[outDocks][];
            _outSlotTime = new float[outDocks][];
            for (int i = 0; i < outDocks; i++) { _outSlots[i] = new GameObject[SlotsPerDock]; _outSlotTime[i] = new float[SlotsPerDock]; }

            // 남↔북을 건너는 길: 건물 양쪽 측면 통로 + 통로 안으로 들어갈 수 있는 랙 통로
            float W = Mathf.Max(20f, input.building.widthM);
            _crossLanes.Add(-W / 2f + 1f);
            _crossLanes.Add(W / 2f - 1f);
            foreach (var a in _f.Access) if (a.IntoAisle) _crossLanes.Add(a.X);

            _cargoLen = ModelFactory.CargoDims.z + 0.4f;
            _cargoIn = ModelFactory.BuildCargo(world.DynamicRoot, false);
            _cargoIn.SetActive(false);
            _cargoOut = ModelFactory.BuildCargo(world.DynamicRoot, true);
            _cargoOut.SetActive(false);

            // 시작하자마자 움직임이 보이도록 입고/출고 하나씩
            CreateInbound();
            CreateOutbound();
        }

        // ================================================================

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            SimTime += dt;

            // 피크 시간 기준 발생률 (건/초)
            float hours = Mathf.Max(1f, _in.throughput.operatingHours);
            float peak = Mathf.Max(1f, _in.throughput.peakFactor);
            _inAcc += dt * _in.throughput.inboundPerDay / hours * peak / 3600f;
            _outAcc += dt * _in.throughput.outboundPerDay / hours * peak / 3600f;
            while (_inAcc >= 1f) { _inAcc -= 1f; CreateInbound(); }
            while (_outAcc >= 1f) { _outAcc -= 1f; CreateOutbound(); }

            ShipOutboundCargo();
            Assign();
            foreach (var m in _movers) StepMover(m, dt);
            foreach (var c in _cranes.Values) StepCrane(c, dt);
        }

        // ---------- 작업 생성 ----------

        AccessPoint RandomAccess() => _f.Access[_rnd.Next(_f.Access.Count)];

        float RandomSlotZ(AccessPoint a) =>
            a.IntoAisle ? Mathf.Lerp(a.SlotZMin, a.SlotZMax, (float)_rnd.NextDouble()) : a.ZIn;

        void CreateInbound()
        {
            if (_f.Access.Count == 0) return;
            // 입고 예정분까지 포함해 보관 위치가 남아 있어야 받는다
            if (Stock + WaitingInbound + InTransit(true) >= Capacity) { FullSkipped++; return; }
            var t = new SimTask { Inbound = true, Dock = _rnd.Next(_inSlots.Length), Access = RandomAccess() };
            t.SlotZ = RandomSlotZ(t.Access);

            // 도크 앞 빈 자리에 하차 (자리가 없으면 마지막 자리에 겹쳐 둔다)
            var slots = _inSlots[t.Dock];
            int s = System.Array.FindIndex(slots, g => g == null);
            t.DockSlot = s;
            t.Cargo = Object.Instantiate(_cargoIn, _world.DynamicRoot);
            t.Cargo.SetActive(true);
            t.Cargo.transform.position = InSlotPos(t.Dock, s < 0 ? SlotsPerDock - 1 : s);
            if (s >= 0) slots[s] = t.Cargo;

            _waiting.Add(t);
            InCreated++;
        }

        void CreateOutbound()
        {
            if (_f.Access.Count == 0) return;
            if (Stock <= 0) { StockOutSkipped++; return; }
            Stock--; // 출고 지시 시점에 재고를 할당
            var t = new SimTask { Inbound = false, Dock = _rnd.Next(_outSlots.Length), Access = RandomAccess() };
            t.SlotZ = t.Access.IntoAisle ? RandomSlotZ(t.Access) : t.Access.ZOut;
            _waiting.Add(t);
            OutCreated++;

            // 자동창고: 크레인이 랙에서 꺼내 출고대로 내보낸다
            if (_cranes.TryGetValue(t.Access.AisleIndex, out var c))
            {
                EnqueueCrane(c, RandomZ(c), RandomY(c), 5f);
                EnqueueCrane(c, c.ZMax, 0.6f, 3f);
            }
        }

        Vector3 InSlotPos(int dock, int slot)
        {
            float x = _f.InDockX.Count > 0 ? _f.InDockX[dock] : 0f;
            return new Vector3(x, 0f, _f.InDockQueueZ + slot * _cargoLen);
        }

        Vector3 OutSlotPos(int dock, int slot)
        {
            float x = _f.OutDockX.Count > 0 ? _f.OutDockX[dock] : 0f;
            return new Vector3(x, 0f, _f.OutDockQueueZ - slot * _cargoLen);
        }

        void ShipOutboundCargo()
        {
            for (int d = 0; d < _outSlots.Length; d++)
                for (int s = 0; s < SlotsPerDock; s++)
                    if (_outSlots[d][s] != null && SimTime - _outSlotTime[d][s] > ShipAfterSeconds)
                    {
                        Object.Destroy(_outSlots[d][s]);
                        _outSlots[d][s] = null;
                    }
        }

        // ---------- 배정 ----------

        Vector3 PickupPoint(SimTask t)
        {
            if (t.Inbound) return t.Cargo != null ? Flat(t.Cargo.transform.position) : InSlotPos(t.Dock, 0);
            return new Vector3(t.Access.X, 0f, t.SlotZ);
        }

        Vector3 DropPoint(SimTask t)
        {
            if (t.Inbound) return new Vector3(t.Access.X, 0f, t.SlotZ);
            return OutSlotPos(t.Dock, 0);
        }

        void Assign()
        {
            for (int i = 0; i < _waiting.Count; i++)
            {
                var t = _waiting[i];
                var pickup = PickupPoint(t);
                Mover best = null;
                float bestD = float.MaxValue;
                foreach (var m in _movers)
                {
                    if (m.Phase != Phase.Idle) continue;
                    float d = (Flat(m.Inst.transform.position) - pickup).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = m; }
                }
                if (best == null) return; // 놀고 있는 설비가 없음

                _waiting.RemoveAt(i);
                i--;
                best.Task = t;
                best.Phase = Phase.ToPickup;
                SetPath(best, Route(Flat(best.Inst.transform.position), pickup));
            }
        }

        // ---------- 경로 ----------

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        bool IsAisleX(float x)
        {
            foreach (var a in _f.Access) if (a.IntoAisle && Mathf.Abs(a.X - x) < 0.05f) return true;
            return false;
        }

        /// <summary>-1: 입고 쪽(남), 1: 출고 쪽(북), 0: 랙 통로 안 (어느 쪽으로든 나갈 수 있음)</summary>
        int SideOf(Vector3 p)
        {
            if (p.z > _f.CorridorInZ + 0.5f && p.z < _f.CorridorOutZ - 0.5f && IsAisleX(p.x)) return 0;
            return p.z < _midZ ? -1 : 1;
        }

        /// <summary>바닥 경로망을 따라가는 꺾은선 경로</summary>
        List<Vector3> Route(Vector3 a, Vector3 b)
        {
            var pts = new List<Vector3>();
            int sa = SideOf(a), sb = SideOf(b);
            if (Mathf.Abs(a.x - b.x) < 0.05f && (sa == sb || sa == 0 || sb == 0 || IsAisleX(a.x)))
            {
                pts.Add(b);
                return pts;
            }

            if (sa == 0 || sb == 0 || sa == sb)
            {
                float c;
                int side = sa != 0 ? sa : sb;
                if (side == -1) c = _f.CorridorInZ;
                else if (side == 1) c = _f.CorridorOutZ;
                else
                {
                    float d1 = Mathf.Abs(a.z - _f.CorridorInZ) + Mathf.Abs(b.z - _f.CorridorInZ);
                    float d2 = Mathf.Abs(a.z - _f.CorridorOutZ) + Mathf.Abs(b.z - _f.CorridorOutZ);
                    c = d1 <= d2 ? _f.CorridorInZ : _f.CorridorOutZ;
                }
                pts.Add(new Vector3(a.x, 0, c));
                pts.Add(new Vector3(b.x, 0, c));
                pts.Add(b);
                return pts;
            }

            // 남↔북: 측면 통로 또는 랙 통로로 건너간다
            float ca = sa < 0 ? _f.CorridorInZ : _f.CorridorOutZ;
            float cb = sb < 0 ? _f.CorridorInZ : _f.CorridorOutZ;
            float lane = _crossLanes.OrderBy(x => Mathf.Abs(a.x - x) + Mathf.Abs(b.x - x)).First();
            pts.Add(new Vector3(a.x, 0, ca));
            pts.Add(new Vector3(lane, 0, ca));
            pts.Add(new Vector3(lane, 0, cb));
            pts.Add(new Vector3(b.x, 0, cb));
            pts.Add(b);
            return pts;
        }

        static void SetPath(Mover m, List<Vector3> path)
        {
            m.Path = path;
            m.PathIndex = 0;
        }

        // ---------- 운반 설비 ----------

        void StepMover(Mover m, float dt)
        {
            if (m.Phase != Phase.Idle) m.BusyTime += dt;
            var tr = m.Inst.transform;

            switch (m.Phase)
            {
                case Phase.ToPickup:
                case Phase.ToDrop:
                {
                    float speed = Mathf.Max(0.3f, m.Def.SpeedMps) * (m.Phase == Phase.ToDrop ? 0.8f : 1f);
                    float remaining = speed * dt;
                    var pos = Flat(tr.position);
                    while (remaining > 0f && m.PathIndex < m.Path.Count)
                    {
                        var target = m.Path[m.PathIndex];
                        var d = target - pos;
                        float dist = d.magnitude;
                        if (dist > 0.01f)
                        {
                            var dir = d / dist;
                            // 지게차는 짐을 들고 갈 때도 앞을 보고 간다
                            tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(dir, Vector3.up), Mathf.Clamp01(dt * 8f));
                        }
                        if (dist <= remaining)
                        {
                            pos = target;
                            remaining -= dist;
                            m.PathIndex++;
                        }
                        else
                        {
                            pos += d / dist * remaining;
                            remaining = 0f;
                        }
                    }
                    tr.position = pos;
                    if (m.PathIndex >= m.Path.Count)
                    {
                        m.Timer = _f.HandlingSeconds;
                        m.Phase = m.Phase == Phase.ToPickup ? Phase.Loading : Phase.Unloading;
                    }
                    break;
                }

                case Phase.Loading:
                    m.Timer -= dt;
                    if (m.Timer > 0f) break;
                    PickUp(m);
                    m.Phase = Phase.ToDrop;
                    SetPath(m, Route(Flat(tr.position), DropPoint(m.Task)));
                    break;

                case Phase.Unloading:
                    m.Timer -= dt;
                    if (m.Timer > 0f) break;
                    DropOff(m);
                    m.Task = null;
                    m.Phase = Phase.Idle;
                    break;
            }
        }

        void PickUp(Mover m)
        {
            var t = m.Task;
            if (t.Inbound)
            {
                if (t.DockSlot >= 0 && _inSlots[t.Dock][t.DockSlot] == t.Cargo) _inSlots[t.Dock][t.DockSlot] = null;
                if (t.Cargo == null)
                {
                    t.Cargo = Object.Instantiate(_cargoIn, _world.DynamicRoot);
                    t.Cargo.SetActive(true);
                }
            }
            else
            {
                t.Cargo = Object.Instantiate(_cargoOut, _world.DynamicRoot);
                t.Cargo.SetActive(true);
            }
            var ct = t.Cargo.transform;
            ct.SetParent(m.Inst.transform, false);
            ct.localPosition = m.CarryOffset;
            ct.localRotation = Quaternion.identity;
        }

        void DropOff(Mover m)
        {
            var t = m.Task;
            if (t.Cargo == null) return;
            if (t.Inbound)
            {
                Object.Destroy(t.Cargo);
                InDone++;
                Stock = Mathf.Min(Capacity, Stock + 1);
                // 자동창고: 크레인이 입고대에서 받아 랙에 넣는다
                if (_cranes.TryGetValue(t.Access.AisleIndex, out var c))
                {
                    EnqueueCrane(c, c.ZMin, 0.6f, 3f);
                    EnqueueCrane(c, RandomZ(c), RandomY(c), 5f);
                }
                return;
            }

            // 출고 도크 앞 빈 자리에 내려놓는다 (가득 차면 가장 오래된 화물을 트럭에 실은 것으로 처리)
            var slots = _outSlots[t.Dock];
            int s = System.Array.FindIndex(slots, g => g == null);
            if (s < 0)
            {
                s = 0;
                for (int i = 1; i < SlotsPerDock; i++) if (_outSlotTime[t.Dock][i] < _outSlotTime[t.Dock][s]) s = i;
                Object.Destroy(slots[s]);
            }
            var ct = t.Cargo.transform;
            ct.SetParent(_world.DynamicRoot, true);
            ct.position = OutSlotPos(t.Dock, s);
            ct.rotation = Quaternion.identity;
            slots[s] = t.Cargo;
            _outSlotTime[t.Dock][s] = SimTime;
            OutDone++;
        }

        // ---------- 스태커크레인 ----------

        float RandomZ(Crane c) => Mathf.Lerp(c.ZMin, c.ZMax, (float)_rnd.NextDouble());
        float RandomY(Crane c) => Mathf.Lerp(0.6f, c.YMax, (float)_rnd.NextDouble());

        static void EnqueueCrane(Crane c, float z, float y, float hold)
        {
            if (c.Jobs.Count < 12) c.Jobs.Enqueue(new CraneJob { Z = z, Y = y, Hold = hold });
        }

        static void StepCrane(Crane c, float dt)
        {
            if (c.Current == null)
            {
                if (c.Jobs.Count == 0) return;
                c.Current = c.Jobs.Dequeue();
                c.Hold = c.Current.Hold;
            }
            c.BusyTime += dt;
            var tr = c.Inst.transform;
            var p = tr.position;
            bool zDone = Mathf.Abs(p.z - c.Current.Z) < 0.01f;
            p.z = Mathf.MoveTowards(p.z, c.Current.Z, 3.0f * dt);
            tr.position = p;

            bool yDone = true;
            if (c.Inst.Lift != null)
            {
                var lp = c.Inst.Lift.localPosition;
                yDone = Mathf.Abs(lp.y - c.Current.Y) < 0.01f;
                lp.y = Mathf.MoveTowards(lp.y, c.Current.Y, 1.0f * dt);
                c.Inst.Lift.localPosition = lp;
            }

            if (zDone && yDone)
            {
                c.Hold -= dt;
                if (c.Hold <= 0f) c.Current = null;
            }
        }

        // ---------- 지표 ----------

        int InTransit(bool inbound) => _movers.Count(m => m.Task != null && m.Task.Inbound == inbound);

        public int WaitingInbound => _waiting.Count(t => t.Inbound);
        public int WaitingOutbound => _waiting.Count(t => !t.Inbound);

        public float ThroughputPerHour => SimTime > 1f ? (InDone + OutDone) / (SimTime / 3600f) : 0f;

        /// <summary>하단 진행 표시줄용 한 줄 요약</summary>
        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append($"재고 {Stock:N0}/{Capacity:N0} ({StockLevel * 100f:0}%)  ·  입고 {InDone}건 완료 (대기 {WaitingInbound})  ·  출고 {OutDone}건 완료 (대기 {WaitingOutbound})");
            if (SimTime > 600f) sb.Append($"  ·  시간당 {ThroughputPerHour:0}건 (피크 필요 {_plan.RequiredThroughput:0})");
            sb.Append("  ·  가동률 ");
            sb.Append(string.Join(", ", Utilization().Select(kv => $"{kv.Key} {kv.Value * 100f:0}%")));
            return sb.ToString();
        }

        /// <summary>설비 종류별 평균 가동률 (0~1)</summary>
        public List<KeyValuePair<string, float>> Utilization()
        {
            var list = new List<KeyValuePair<string, float>>();
            if (SimTime <= 0f) return list;
            foreach (var g in _movers.GroupBy(m => m.Def.NameKo))
                list.Add(new KeyValuePair<string, float>(g.Key, g.Average(m => m.BusyTime) / SimTime));
            if (_cranes.Count > 0)
                list.Add(new KeyValuePair<string, float>("크레인", _cranes.Values.Average(c => c.BusyTime) / SimTime));
            return list;
        }

        /// <summary>우측 패널용 자세한 결과</summary>
        public string Report()
        {
            var sb = new StringBuilder();
            var t = System.TimeSpan.FromSeconds(SimTime);
            sb.AppendLine($"시뮬레이션 시간  {(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}  (피크 시간 발생률 기준)");
            sb.AppendLine($"재고  {Stock:N0} / {Capacity:N0} 위치  ({StockLevel * 100f:0}%)");
            sb.AppendLine($"입고  발생 {InCreated} · 완료 {InDone} · 대기 {WaitingInbound}");
            sb.AppendLine($"출고  발생 {OutCreated} · 완료 {OutDone} · 대기 {WaitingOutbound}");
            if (SimTime > 600f) sb.AppendLine($"처리 실적  시간당 {ThroughputPerHour:0}건 / 피크 필요 {_plan.RequiredThroughput:0}건");
            sb.AppendLine("가동률");
            foreach (var kv in Utilization()) sb.AppendLine($"   · {kv.Key}  {kv.Value * 100f:0}%");
            if (StockOutSkipped > 0) sb.AppendLine($"※ 재고가 없어 출고하지 못한 건 {StockOutSkipped}건");
            if (FullSkipped > 0) sb.AppendLine($"※ 보관 위치가 꽉 차 받지 못한 입고 {FullSkipped}건");
            int waiting = _waiting.Count;
            if (SimTime > 1800f && waiting > Mathf.Max(5, _movers.Count * 2))
                sb.AppendLine("※ 대기 작업이 쌓이고 있습니다. 운반 설비가 부족할 수 있습니다.");
            return sb.ToString();
        }
    }
}
