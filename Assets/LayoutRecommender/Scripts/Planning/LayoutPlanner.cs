using System;
using System.Collections.Generic;
using System.Linq;
using LayoutRecommender.Data;
using UnityEngine;

namespace LayoutRecommender.Planning
{
    /// <summary>
    /// 입력값으로 보관 방식별 배치안을 만들고 점수를 매겨 추천한다.
    /// 규모 산정은 경험칙 기반의 개략 계산이며, 계수는 이 파일 상단 상수에서 조정한다.
    /// 좌표계: x = 건물 폭 방향, z = 건물 깊이 방향 (입고 도크 z-, 출고 도크 z+), 건물 중심이 원점.
    /// </summary>
    public static class LayoutPlanner
    {
        // ---------- 조정 가능한 계수 ----------
        const float DockDepth = 4f;            // 도크 구역 깊이
        const float SideMargin = 2f;           // 벽과 보관 구역 사이 여유
        const float StorageSafety = 1.1f;      // 보관 위치 여유율
        const float VehicleUtilization = 0.85f;
        const float ForkliftMaxLift = 10.5f;   // 지게차(리치) 최대 적치 높이
        const float HandShelfHeight = 2.4f;    // 사람이 손으로 꺼내는 선반 높이

        // 개략 단가 (억원)
        const float CostRackPosPallet = 0.0015f, CostRackPosSmall = 0.0005f;
        const float CostShuttlePos = 0.004f, CostShuttleCarrier = 1.2f;
        const float CostAsrsPos = 0.003f, CostCraneSingle = 3.0f, CostCraneDouble = 3.8f;
        const float CostConveyor = 0.25f, CostForklift = 0.45f, CostPalletTruck = 0.03f, CostAgv = 1.0f, CostAmr = 0.4f;

        /// <summary>건물 안의 구역 경계</summary>
        class Site
        {
            public float W, D, X0, X1, StorZ0, StorZ1;
            public Rect InStage, OutStage;
            public float StorageWidth => X1 - X0;
            public float StorageLen => StorZ1 - StorZ0;
            public float StorCenterZ => (StorZ0 + StorZ1) * 0.5f;
        }

        /// <summary>적재 단위 치수 (m)</summary>
        class Unit
        {
            public float Face, Depth, Pitch;
            public bool Pallet;
        }

        // ================================================================

        public static List<LayoutPlan> Generate(ProjectInput input)
        {
            var plans = new List<LayoutPlan>();
            var site = MakeSite(input);
            if (site == null) return plans;
            var unit = MakeUnit(input);

            TryAdd(plans, input, EquipmentType.SelectiveRack, () => BuildSelective(input, site, unit));
            TryAdd(plans, input, EquipmentType.ShuttleRack, () => BuildShuttle(input, site, unit));
            TryAdd(plans, input, EquipmentType.StackerCraneSingle, () => BuildCrane(input, site, unit, false));
            TryAdd(plans, input, EquipmentType.StackerCraneDouble, () => BuildCrane(input, site, unit, true));

            ScorePlans(plans, input);
            plans = plans.OrderByDescending(p => p.Score).Take(3).ToList();

            const string letters = "ABC";
            for (int i = 0; i < plans.Count; i++)
            {
                var p = plans[i];
                p.Recommended = i == 0;
                p.Name = $"{letters[i]}안 · {EquipmentCatalog.Get(p.StorageType).NameKo}";
            }
            return plans;
        }

        static void TryAdd(List<LayoutPlan> plans, ProjectInput input, EquipmentType storage, Func<LayoutPlan> build)
        {
            if (!input.IsAllowed(EquipmentCatalog.Get(storage))) return;
            var plan = build();
            if (plan != null && plan.UsablePositions > 0) plans.Add(plan);
        }

        // ---------- 공통 ----------

        static Site MakeSite(ProjectInput input)
        {
            float W = Mathf.Max(20f, input.building.widthM);
            float D = Mathf.Max(20f, input.building.depthM);
            float stage = Mathf.Clamp(D * 0.12f, 6f, 15f);
            var s = new Site
            {
                W = W, D = D,
                X0 = -W / 2f + SideMargin, X1 = W / 2f - SideMargin,
                StorZ0 = -D / 2f + DockDepth + stage,
                StorZ1 = D / 2f - DockDepth - stage,
                InStage = new Rect(-W / 2f, -D / 2f + DockDepth, W, stage),
                OutStage = new Rect(-W / 2f, D / 2f - DockDepth - stage, W, stage),
            };
            return s.StorageLen >= 12f ? s : null;
        }

        static Unit MakeUnit(ProjectInput input)
        {
            var c = input.cargo;
            bool pallet = c.unitType == LoadUnitType.Pallet;
            return new Unit
            {
                Pallet = pallet,
                Face = c.widthMm / 1000f + 0.1f,
                Depth = c.lengthMm / 1000f + 0.1f,
                Pitch = c.heightMm / 1000f + (pallet ? 0.25f : 0.1f),
            };
        }

        static int Levels(Unit u, float maxHeight) => Mathf.Max(1, Mathf.FloorToInt((maxHeight - 0.3f) / u.Pitch));

        static bool Allowed(ProjectInput input, EquipmentType t) => input.IsAllowed(EquipmentCatalog.Get(t));

        static int FleetSize(float perHour, EquipmentType t)
        {
            float cycles = EquipmentCatalog.Get(t).CyclesPerHour;
            if (cycles <= 0) return 1;
            return Mathf.Clamp(Mathf.CeilToInt(perHour / (cycles * VehicleUtilization)), 1, 60);
        }

        static float PeakPerHour(ProjectInput input) => Mathf.Max(1f, input.throughput.PeakTotalPerHour);

        /// <summary>도크와 보관 구역 사이 수평 운반 설비 선택</summary>
        static EquipmentType? ChooseTransport(ProjectInput input)
        {
            bool pallet = input.cargo.unitType == LoadUnitType.Pallet;
            var a = input.preference.automation;
            EquipmentType[] order;
            if (pallet)
                order = a == AutomationPreference.Manual
                    ? new[] { EquipmentType.Forklift, EquipmentType.PalletTruck, EquipmentType.AGV }
                    : new[] { EquipmentType.AGV, EquipmentType.Forklift, EquipmentType.PalletTruck };
            else
                order = a == AutomationPreference.Manual
                    ? new[] { EquipmentType.Worker, EquipmentType.AMR, EquipmentType.AGV }
                    : new[] { EquipmentType.AMR, EquipmentType.AGV, EquipmentType.Worker };
            foreach (var t in order)
                if (Allowed(input, t)) return t;
            return null;
        }

        static void AddSiteFeatures(LayoutPlan plan, ProjectInput input, Site s)
        {
            plan.Zones.Add(new ZoneArea { Name = "입고 도크", Area = new Rect(-s.W / 2f, -s.D / 2f, s.W, DockDepth), Color = new Color(0.25f, 0.45f, 0.65f) });
            plan.Zones.Add(new ZoneArea { Name = "입고 대기", Area = s.InStage, Color = new Color(0.30f, 0.40f, 0.48f) });
            plan.Zones.Add(new ZoneArea { Name = "보관", Area = new Rect(s.X0, s.StorZ0, s.StorageWidth, s.StorageLen), Color = new Color(0.33f, 0.35f, 0.38f) });
            plan.Zones.Add(new ZoneArea { Name = "출고 대기", Area = s.OutStage, Color = new Color(0.45f, 0.40f, 0.30f) });
            plan.Zones.Add(new ZoneArea { Name = "출고 도크", Area = new Rect(-s.W / 2f, s.D / 2f - DockDepth, s.W, DockDepth), Color = new Color(0.65f, 0.50f, 0.25f) });

            AddDocks(plan, s, input.building.inboundDocks, true);
            AddDocks(plan, s, input.building.outboundDocks, false);
        }

        static void AddDocks(LayoutPlan plan, Site s, int count, bool inbound)
        {
            count = Mathf.Clamp(count, 1, 40);
            float spacing = s.W / (count + 1);
            float z = inbound ? -s.D / 2f : s.D / 2f;
            for (int i = 0; i < count; i++)
                plan.Docks.Add(new DockDoor { Inbound = inbound, Center = new Vector3(-s.W / 2f + spacing * (i + 1), 0, z), Width = Mathf.Min(3.5f, spacing * 0.8f) });
        }

        /// <summary>대기 구역에 차량/인원을 줄 세워 배치 (입고/출고 번갈아)</summary>
        static void PlaceFleet(LayoutPlan plan, Site s, EquipmentType t, int count, string labelPrefix)
        {
            if (count <= 0) return;
            var def = EquipmentCatalog.Get(t);
            bool person = t == EquipmentType.Worker;
            var size = person ? new Vector3(0.6f, 1.7f, 0.6f) : new Vector3(def.FootprintWidthM, VehicleHeight(t), def.FootprintDepthM);
            float stepX = size.x + 1.5f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((s.W - 8f) / stepX));

            int inIdx = 0, outIdx = 0;
            for (int i = 0; i < count; i++)
            {
                bool inbound = i % 2 == 0;
                int k = inbound ? inIdx++ : outIdx++;
                var zone = inbound ? s.InStage : s.OutStage;
                int row = k / perRow, col = k % perRow;
                float x = -s.W / 2f + 4f + stepX * col + size.x / 2f;
                // 차종마다 대기 구역 안에서 다른 줄을 쓰도록 오프셋
                float lane = 0.25f + 0.15f * ((int)t % 4) + 0.08f * row;
                float z = zone.yMin + zone.height * Mathf.Clamp01(lane);
                plan.Items.Add(new PlacedItem
                {
                    Type = t, Shape = person ? ItemShape.Person : ItemShape.Vehicle,
                    Center = new Vector3(x, 0, z), Size = size,
                    Label = $"{labelPrefix} {i + 1}",
                    Detail = $"{(inbound ? "입고" : "출고")} 측 대기 · {def.Description}"
                });
            }
            plan.AddCount(t, count);
        }

        static float VehicleHeight(EquipmentType t)
        {
            switch (t)
            {
                case EquipmentType.Forklift: return 2.2f;
                case EquipmentType.PalletTruck: return 1.2f;
                case EquipmentType.AGV: return 0.6f;
                case EquipmentType.AMR: return 0.45f;
                default: return 0.6f;
            }
        }

        static int Checkers(ProjectInput input) => Mathf.Max(2, (input.building.inboundDocks + input.building.outboundDocks) / 3);

        static float VehicleCost(EquipmentType t)
        {
            switch (t)
            {
                case EquipmentType.Forklift: return CostForklift;
                case EquipmentType.PalletTruck: return CostPalletTruck;
                case EquipmentType.AGV: return CostAgv;
                case EquipmentType.AMR: return CostAmr;
                default: return 0f;
            }
        }

        static bool NeedsDriver(EquipmentType t) => t == EquipmentType.Forklift || t == EquipmentType.PalletTruck;

        // ================================================================
        // 1) 선택식 랙
        // ================================================================

        static LayoutPlan BuildSelective(ProjectInput input, Site s, Unit u)
        {
            float P = PeakPerHour(input);
            var plan = new LayoutPlan { StorageType = EquipmentType.SelectiveRack, RequiredThroughput = P, RequiredPositions = input.throughput.inventoryUnits };

            EquipmentType handler;
            float aisle, maxH;
            if (u.Pallet)
            {
                if (!Allowed(input, EquipmentType.Forklift)) return null; // 고단 적치에 지게차 필요
                handler = EquipmentType.Forklift;
                aisle = 3.5f;
                maxH = Mathf.Min(input.building.clearHeightM - 0.5f, ForkliftMaxLift);
            }
            else
            {
                if (!Allowed(input, EquipmentType.Worker)) return null;  // 선반 피킹은 작업자
                handler = EquipmentType.Worker;
                aisle = 1.4f;
                maxH = Mathf.Min(input.building.clearHeightM - 0.5f, HandShelfHeight);
            }

            int levels = Levels(u, maxH);
            float rowDepth = u.Depth;
            float pairW = 2f * rowDepth + 0.2f;
            float pitchX = pairW + aisle;
            float rowLen = s.StorageLen - 6f;               // 양 끝 3m 횡단 통로
            int perRowLevel = Mathf.FloorToInt(rowLen / u.Face);
            if (perRowLevel < 1) return null;
            int perPair = 2 * perRowLevel * levels;

            int pairsMax = Mathf.FloorToInt((s.StorageWidth - aisle) / pitchX);
            if (pairsMax < 1) return null;
            int pairsNeeded = Mathf.CeilToInt(input.throughput.inventoryUnits * StorageSafety / perPair);
            int pairs = Mathf.Clamp(pairsNeeded, 1, pairsMax);
            if (pairsNeeded > pairsMax) plan.Notes.Add($"건물 폭이 부족해 랙 {pairsNeeded * 2}열 중 {pairsMax * 2}열만 배치했습니다.");

            AddSiteFeatures(plan, input, s);

            float blockW = pairs * pitchX + aisle;
            float startX = -blockW / 2f + aisle;
            float rackH = levels * u.Pitch;
            var aisleCenters = new List<float> { startX - aisle / 2f };
            int rowNo = 1;
            for (int i = 0; i < pairs; i++)
            {
                float x0 = startX + i * pitchX;
                for (int side = 0; side < 2; side++)
                {
                    float cx = x0 + rowDepth / 2f + side * (rowDepth + 0.2f);
                    plan.Items.Add(new PlacedItem
                    {
                        Type = EquipmentType.SelectiveRack, Shape = ItemShape.RackRow,
                        Center = new Vector3(cx, 0, s.StorCenterZ), Size = new Vector3(rowDepth, rackH, rowLen),
                        Label = $"랙 {rowNo++}열",
                        Detail = $"{levels}단 × {perRowLevel}칸 = {levels * perRowLevel}위치 · 통로폭 {aisle:0.0}m"
                    });
                }
                aisleCenters.Add(x0 + pairW + aisle / 2f);
            }
            plan.AddCount(EquipmentType.SelectiveRack, pairs * 2);

            plan.Levels = levels;
            plan.PhysicalPositions = plan.UsablePositions = pairs * perPair;
            plan.StorageAreaM2 = blockW * rowLen;

            // 보관 작업 설비: 통로마다 나눠 배치
            int handlers;
            float handlerCycles;
            int extraWorkers = 0;
            float capex = plan.PhysicalPositions * (u.Pallet ? CostRackPosPallet : CostRackPosSmall);
            if (u.Pallet)
            {
                handlers = FleetSize(P, EquipmentType.Forklift);
                handlerCycles = EquipmentCatalog.Get(EquipmentType.Forklift).CyclesPerHour;
                capex += handlers * CostForklift;
                extraWorkers += handlers;
                plan.AutomationLevel = 0;
                plan.Concept = "선택식 파렛트 랙 + 지게차. 모든 파렛트에 바로 접근할 수 있어 SKU가 많고 회전이 높을 때 유리합니다.";
            }
            else
            {
                bool amr = input.preference.automation != AutomationPreference.Manual && Allowed(input, EquipmentType.AMR);
                handlerCycles = EquipmentCatalog.Get(EquipmentType.Worker).CyclesPerHour * (amr ? 1.5f : 1f);
                handlers = Mathf.Clamp(Mathf.CeilToInt(P / (handlerCycles * VehicleUtilization)), 1, 80);
                extraWorkers += handlers;
                if (amr)
                {
                    int amrs = FleetSize(P, EquipmentType.AMR);
                    PlaceFleet(plan, s, EquipmentType.AMR, amrs, "AMR");
                    capex += amrs * CostAmr;
                    plan.AutomationLevel = 1;
                    plan.Concept = "경량 선반 + 작업자 피킹 + AMR 반송. 작업자는 선반 구역에서 피킹하고 AMR이 대기 구역까지 나릅니다.";
                }
                else
                {
                    plan.AutomationLevel = 0;
                    plan.Concept = "경량 선반 + 작업자 피킹. 초기 투자가 가장 적고 SKU 변화에 유연합니다.";
                }
            }

            var hdef = EquipmentCatalog.Get(handler);
            for (int i = 0; i < handlers; i++)
            {
                float ax = aisleCenters[i % aisleCenters.Count];
                float z = s.StorZ0 + 4f + (rowLen - 2f) * ((i / aisleCenters.Count) * 0.37f % 1f);
                bool person = handler == EquipmentType.Worker;
                plan.Items.Add(new PlacedItem
                {
                    Type = handler, Shape = person ? ItemShape.Person : ItemShape.Vehicle,
                    Center = new Vector3(ax, 0, z),
                    Size = person ? new Vector3(0.6f, 1.7f, 0.6f) : new Vector3(hdef.FootprintWidthM, VehicleHeight(handler), hdef.FootprintDepthM),
                    Label = $"{hdef.NameKo} {i + 1}", Detail = "보관 구역 통로 작업 · " + hdef.Description
                });
            }
            plan.AddCount(handler, handlers);

            if (u.Pallet && Allowed(input, EquipmentType.PalletTruck))
            {
                int pts = Mathf.Max(1, (input.building.inboundDocks + input.building.outboundDocks) / 3);
                PlaceFleet(plan, s, EquipmentType.PalletTruck, pts, "파렛트트럭");
                capex += pts * CostPalletTruck;
                extraWorkers += pts;
            }

            plan.ThroughputCapacity = handlers * handlerCycles;
            plan.CapexEok = capex;
            plan.Workers = extraWorkers + Checkers(input);
            return plan;
        }

        // ================================================================
        // 2) 셔틀랙
        // ================================================================

        static LayoutPlan BuildShuttle(ProjectInput input, Site s, Unit u)
        {
            float P = PeakPerHour(input);
            var plan = new LayoutPlan { StorageType = EquipmentType.ShuttleRack, RequiredThroughput = P, RequiredPositions = input.throughput.inventoryUnits };

            bool conveyorOk = Allowed(input, EquipmentType.Conveyor);
            bool forkliftOk = Allowed(input, EquipmentType.Forklift);
            // 리프트+컨베이어 공급(자동) 또는 지게차 공급(반자동). 토트/카톤은 리프트 공급만.
            bool liftFed = conveyorOk && (!u.Pallet || input.preference.automation == AutomationPreference.FullAuto || !forkliftOk);
            if (!liftFed && !(u.Pallet && forkliftOk)) return null;

            float maxH = liftFed ? input.building.clearHeightM - 0.8f : Mathf.Min(input.building.clearHeightM - 0.5f, ForkliftMaxLift);
            int levels = Levels(u, maxH);
            float laneW = u.Face + 0.3f;
            float laneLen = Mathf.Min(s.StorageLen - 8f, 36f);  // 양쪽 면 4m 작업 통로
            int perLane = Mathf.FloorToInt(laneLen / u.Depth);
            if (perLane < 3) return null;

            int sku = Mathf.Max(1, input.cargo.skuCount);
            int inv = input.throughput.inventoryUnits;
            float avgPerSku = (float)inv / sku;
            int laneLevelsNeeded = Mathf.Max(sku, Mathf.CeilToInt(inv * StorageSafety / perLane));
            int lanesNeeded = Mathf.CeilToInt((float)laneLevelsNeeded / levels);
            int lanesMax = Mathf.FloorToInt(s.StorageWidth * 0.95f / laneW);
            if (lanesMax < 1) return null;
            int lanes = Mathf.Clamp(lanesNeeded, 1, lanesMax);
            if (lanesNeeded > lanesMax) plan.Notes.Add($"건물 폭이 부족해 레인 {lanesNeeded}개 중 {lanesMax}개만 배치했습니다.");

            // SKU 단위로 레인을 채우므로 SKU당 재고가 레인 길이보다 적으면 빈 칸이 생긴다
            float lanesPerSku = Mathf.Ceil(avgPerSku / perLane);
            float fill = Mathf.Clamp01(avgPerSku / Mathf.Max(1f, lanesPerSku * perLane));
            plan.Levels = levels;
            plan.PhysicalPositions = lanes * levels * perLane;
            plan.UsablePositions = Mathf.FloorToInt(plan.PhysicalPositions * fill);
            if (fill < 0.7f) plan.Notes.Add($"SKU당 재고({avgPerSku:0.#})가 레인 깊이({perLane})보다 적어 레인 활용률이 {fill * 100f:0}%입니다.");

            AddSiteFeatures(plan, input, s);

            const int lanesPerBlock = 12;
            int blocks = Mathf.CeilToInt((float)lanes / lanesPerBlock);
            float gap = 0.4f;
            float totalW = lanes * laneW + (blocks - 1) * gap;
            float x = -totalW / 2f;
            float rackH = levels * u.Pitch;
            int remaining = lanes;
            var blockCenters = new List<float>();
            for (int b = 0; b < blocks; b++)
            {
                int n = Mathf.Min(lanesPerBlock, remaining);
                remaining -= n;
                float w = n * laneW;
                blockCenters.Add(x + w / 2f);
                plan.Items.Add(new PlacedItem
                {
                    Type = EquipmentType.ShuttleRack, Shape = ItemShape.ShuttleBlock,
                    Center = new Vector3(x + w / 2f, 0, s.StorCenterZ), Size = new Vector3(w, rackH, laneLen),
                    Label = $"셔틀랙 블록 {b + 1}",
                    Detail = $"레인 {n}개 × {levels}단 × 깊이 {perLane} = {n * levels * perLane}위치"
                });
                x += w + gap;
            }
            plan.AddCount(EquipmentType.ShuttleRack, blocks);
            plan.StorageAreaM2 = totalW * laneLen;

            // 셔틀 차량: 블록 앞면에 표시
            var sdef = EquipmentCatalog.Get(EquipmentType.ShuttleRack);
            int shuttles = Mathf.Clamp(Mathf.CeilToInt(P / (sdef.CyclesPerHour * VehicleUtilization)), 1, lanes);
            for (int i = 0; i < shuttles; i++)
            {
                float bx = blockCenters[i % blocks] + ((i / blocks) % 3 - 1) * laneW;
                plan.Items.Add(new PlacedItem
                {
                    Type = EquipmentType.ShuttleRack, Shape = ItemShape.Vehicle,
                    Center = new Vector3(bx, 0, s.StorCenterZ - laneLen / 2f - 0.8f), Size = new Vector3(u.Face, 0.3f, 1.2f),
                    Label = $"셔틀 {i + 1}", Detail = "레인 안을 오가며 적재 단위를 넣고 뺍니다."
                });
            }

            float capex = plan.PhysicalPositions * CostShuttlePos + shuttles * CostShuttleCarrier;
            float feedCapacity;
            int workers = Checkers(input);

            float faceZIn = s.StorCenterZ - laneLen / 2f - 2.5f;
            float faceZOut = s.StorCenterZ + laneLen / 2f + 2.5f;
            if (liftFed)
            {
                AddConveyorRun(plan, new Vector3(0, 0, faceZIn), new Vector3(totalW, 0.8f, 1.2f), "입고 컨베이어 (블록 앞면)");
                AddConveyorRun(plan, new Vector3(0, 0, faceZOut), new Vector3(totalW, 0.8f, 1.2f), "출고 컨베이어 (블록 뒷면)");
                capex += 2 * CostConveyor * Mathf.Max(1f, totalW / 10f);
                feedCapacity = EquipmentCatalog.Get(EquipmentType.Conveyor).CyclesPerHour;
                plan.AutomationLevel = 2;
                plan.Concept = "셔틀랙 + 리프트/컨베이어 공급. 레인 깊이 방향으로 빽빽하게 보관해 SKU당 수량이 많을 때 공간 효율이 가장 좋습니다.";
            }
            else
            {
                int forks = FleetSize(P, EquipmentType.Forklift);
                var fdef = EquipmentCatalog.Get(EquipmentType.Forklift);
                for (int i = 0; i < forks; i++)
                {
                    bool front = i % 2 == 0;
                    float fx = -totalW / 2f + (totalW) * ((i / 2 + 0.5f) / Mathf.Max(1, (forks + 1) / 2));
                    plan.Items.Add(new PlacedItem
                    {
                        Type = EquipmentType.Forklift, Shape = ItemShape.Vehicle,
                        Center = new Vector3(fx, 0, front ? faceZIn - 0.5f : faceZOut + 0.5f),
                        Size = new Vector3(fdef.FootprintWidthM, VehicleHeight(EquipmentType.Forklift), fdef.FootprintDepthM),
                        Label = $"지게차 {i + 1}", Detail = (front ? "입고 면" : "출고 면") + "에서 레인 입구에 적치"
                    });
                }
                plan.AddCount(EquipmentType.Forklift, forks);
                capex += forks * CostForklift;
                workers += forks;
                feedCapacity = forks * fdef.CyclesPerHour;
                plan.AutomationLevel = 1;
                plan.Concept = "셔틀랙 + 지게차 공급. 지게차는 레인 입구까지만 넣고 셔틀이 안쪽으로 옮깁니다. 고밀도 보관에 적합합니다.";
            }

            // 도크 ↔ 블록 운반
            var transport = ChooseTransport(input);
            if (transport.HasValue && (liftFed || transport.Value != EquipmentType.Forklift))
            {
                int n = FleetSize(P, transport.Value);
                PlaceFleet(plan, s, transport.Value, n, EquipmentCatalog.Get(transport.Value).NameKo);
                capex += n * VehicleCost(transport.Value);
                if (transport.Value == EquipmentType.Worker || NeedsDriver(transport.Value)) workers += n;
            }

            plan.ThroughputCapacity = Mathf.Min(shuttles * sdef.CyclesPerHour, feedCapacity);
            plan.CapexEok = capex;
            plan.Workers = workers;
            return plan;
        }

        // ================================================================
        // 3) 스태커크레인 (AS/RS)
        // ================================================================

        static LayoutPlan BuildCrane(ProjectInput input, Site s, Unit u, bool doubleFork)
        {
            var type = doubleFork ? EquipmentType.StackerCraneDouble : EquipmentType.StackerCraneSingle;
            var def = EquipmentCatalog.Get(type);
            if (input.building.clearHeightM < 6f) return null; // 낮은 건물에서는 의미가 적다

            float P = PeakPerHour(input);
            var plan = new LayoutPlan { StorageType = type, RequiredThroughput = P, RequiredPositions = input.throughput.inventoryUnits };

            float cycles = def.CyclesPerHour * (u.Pallet ? 1f : 2.5f); // 토트/카톤용 미니로드는 더 빠르다
            float aisle = doubleFork ? 1.8f : 1.6f;
            float rowDepth = u.Depth;
            float module = 2f * rowDepth + aisle + 0.3f;
            int levels = Levels(u, input.building.clearHeightM - 0.8f);
            float len = s.StorageLen - 10f;                 // 양 끝 P&D 구역 5m
            if (len < 10f) return null;
            int perAisle = 2 * Mathf.FloorToInt(len / u.Face) * levels;
            int aislesMax = Mathf.FloorToInt(s.StorageWidth / module);
            if (aislesMax < 1 || perAisle < 1) return null;

            int needS = Mathf.CeilToInt(input.throughput.inventoryUnits * StorageSafety / perAisle);
            int needT = Mathf.CeilToInt(P / (cycles * 0.9f));
            int aisles = Mathf.Clamp(Mathf.Max(needS, needT), 1, aislesMax);
            if (Mathf.Max(needS, needT) > aislesMax) plan.Notes.Add($"건물 폭이 부족해 통로 {Mathf.Max(needS, needT)}개 중 {aislesMax}개만 배치했습니다.");
            if (needT > needS) plan.Notes.Add("보관량보다 처리량 때문에 통로(크레인) 수가 정해졌습니다.");

            AddSiteFeatures(plan, input, s);

            float blockW = aisles * module;
            float startX = -blockW / 2f;
            float rackH = levels * u.Pitch;
            int perRowLevel = Mathf.FloorToInt(len / u.Face);
            var craneDef = def;
            for (int i = 0; i < aisles; i++)
            {
                float x0 = startX + i * module;
                float r1 = x0 + rowDepth / 2f;
                float ac = x0 + rowDepth + 0.15f + aisle / 2f;
                float r2 = x0 + rowDepth + 0.3f + aisle + rowDepth / 2f;
                plan.Items.Add(new PlacedItem
                {
                    Type = type, Shape = ItemShape.RackRow, Center = new Vector3(r1, 0, s.StorCenterZ), Size = new Vector3(rowDepth, rackH, len),
                    Label = $"자동창고 랙 {i * 2 + 1}열", Detail = $"{levels}단 × {perRowLevel}칸"
                });
                plan.Items.Add(new PlacedItem
                {
                    Type = type, Shape = ItemShape.RackRow, Center = new Vector3(r2, 0, s.StorCenterZ), Size = new Vector3(rowDepth, rackH, len),
                    Label = $"자동창고 랙 {i * 2 + 2}열", Detail = $"{levels}단 × {perRowLevel}칸"
                });
                float cz = s.StorCenterZ + len * 0.35f * Mathf.Sin(i * 1.7f);
                plan.Items.Add(new PlacedItem
                {
                    Type = type, Shape = ItemShape.Mast, Center = new Vector3(ac, 0, cz), Size = new Vector3(aisle * 0.7f, rackH + 1f, 2.0f),
                    Label = $"{craneDef.NameKo} {i + 1}호기", Detail = $"통로 {i + 1} · {cycles:0}건/시간 · {craneDef.Description}"
                });
            }
            plan.AddCount(type, aisles);
            plan.Levels = levels;
            plan.PhysicalPositions = plan.UsablePositions = aisles * perAisle;
            plan.StorageAreaM2 = blockW * len;

            float capex = plan.PhysicalPositions * CostAsrsPos + aisles * (doubleFork ? CostCraneDouble : CostCraneSingle);
            int workers = Checkers(input) + Mathf.Max(1, aisles / 4); // 관제/모니터링

            // P&D 컨베이어: 통로마다 양 끝 + 앞뒤 횡단 구간
            if (Allowed(input, EquipmentType.Conveyor))
            {
                float zSouth = s.StorCenterZ - len / 2f;
                float zNorth = s.StorCenterZ + len / 2f;
                for (int i = 0; i < aisles; i++)
                {
                    float ac = startX + i * module + rowDepth + 0.15f + aisle / 2f;
                    AddConveyorRun(plan, new Vector3(ac, 0, zSouth - 2.2f), new Vector3(1.2f, 0.8f, 3.4f), $"입고 P&D {i + 1}");
                    AddConveyorRun(plan, new Vector3(ac, 0, zNorth + 2.2f), new Vector3(1.2f, 0.8f, 3.4f), $"출고 P&D {i + 1}");
                }
                AddConveyorRun(plan, new Vector3(0, 0, zSouth - 4.5f), new Vector3(blockW, 0.8f, 1.2f), "입고 횡단 컨베이어");
                AddConveyorRun(plan, new Vector3(0, 0, zNorth + 4.5f), new Vector3(blockW, 0.8f, 1.2f), "출고 횡단 컨베이어");
                capex += (aisles * 2 + 2) * CostConveyor;
            }
            else
            {
                plan.Notes.Add("컨베이어를 제외해서 P&D 지점에서 운반 설비가 직접 주고받습니다.");
            }

            var transport = ChooseTransport(input);
            if (transport.HasValue)
            {
                int n = FleetSize(P, transport.Value);
                PlaceFleet(plan, s, transport.Value, n, EquipmentCatalog.Get(transport.Value).NameKo);
                capex += n * VehicleCost(transport.Value);
                if (transport.Value == EquipmentType.Worker || NeedsDriver(transport.Value)) workers += n;
            }
            else plan.Notes.Add("도크와 자동창고 사이 운반 설비가 없습니다. 설비 선호에서 하나 이상 허용하세요.");

            plan.ThroughputCapacity = aisles * cycles;
            plan.CapexEok = capex;
            plan.Workers = workers;
            plan.AutomationLevel = 2;
            plan.Concept = doubleFork
                ? "자동창고 (스태커크레인 더블포크). 한 번에 두 단위를 처리해 처리량과 보관 밀도가 모두 필요할 때 유리합니다."
                : "자동창고 (스태커크레인 싱글포크). 층고를 최대로 써서 적은 바닥 면적에 많이 보관하고 인력을 줄입니다.";
            return plan;
        }

        static void AddConveyorRun(LayoutPlan plan, Vector3 center, Vector3 size, string label)
        {
            plan.Items.Add(new PlacedItem
            {
                Type = EquipmentType.Conveyor, Shape = ItemShape.Conveyor, Center = center, Size = size,
                Label = label, Detail = $"길이 {Mathf.Max(size.x, size.z):0.#}m · " + EquipmentCatalog.Get(EquipmentType.Conveyor).Description
            });
            plan.AddCount(EquipmentType.Conveyor, 1);
        }

        // ================================================================
        // 점수
        // ================================================================

        static void ScorePlans(List<LayoutPlan> plans, ProjectInput input)
        {
            if (plans.Count == 0) return;
            int pref = (int)input.preference.automation;
            float bestDensity = plans.Max(p => p.UsablePositions / Mathf.Max(1f, p.StorageAreaM2));
            float bestCostPerPos = plans.Min(p => p.CapexEok / Mathf.Max(1, p.UsablePositions));

            foreach (var p in plans)
            {
                float storage = 30f * Mathf.Min(1f, p.StorageFit);
                float through = 25f * Mathf.Min(1f, p.ThroughputFit);
                float auto = 15f * (1f - Mathf.Abs(p.AutomationLevel - pref) / 2f);
                float cost = 15f * (bestCostPerPos / Mathf.Max(1e-6f, p.CapexEok / Mathf.Max(1, p.UsablePositions)));
                float density = 15f * ((p.UsablePositions / Mathf.Max(1f, p.StorageAreaM2)) / Mathf.Max(1e-6f, bestDensity));

                p.ScoreParts["보관 충족"] = storage;
                p.ScoreParts["처리 충족"] = through;
                p.ScoreParts["자동화 선호"] = auto;
                p.ScoreParts["투자 효율"] = cost;
                p.ScoreParts["공간 효율"] = density;
                p.Score = storage + through + auto + cost + density;

                if (p.StorageFit < 1f) p.Notes.Add($"보관 위치가 필요량의 {p.StorageFit * 100f:0}%입니다.");
                if (p.ThroughputFit < 1f) p.Notes.Add($"처리능력이 피크 필요량의 {p.ThroughputFit * 100f:0}%입니다.");
            }
        }
    }
}
