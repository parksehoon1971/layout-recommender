using LayoutRecommender.Data;
using LayoutRecommender.Planning;
using UnityEngine;

namespace LayoutRecommender.World
{
    /// <summary>
    /// 설비 3D 형상을 절차적으로 만든다. 모든 모델은 바닥 중심이 원점이고 앞쪽이 +z.
    /// 박스/원기둥 조합이지만 실제 설비의 구성(마스트, 포크, 오버헤드가드, 빔, 롤러 등)을 따라 그린다.
    /// </summary>
    public static class ModelFactory
    {
        // ---------- 색 ----------
        static readonly Color Black = new Color(0.08f, 0.08f, 0.09f);
        static readonly Color DarkGray = new Color(0.22f, 0.23f, 0.25f);
        static readonly Color Steel = new Color(0.62f, 0.64f, 0.67f);
        static readonly Color Silver = new Color(0.80f, 0.82f, 0.85f);
        static readonly Color RackBlue = new Color(0.12f, 0.32f, 0.68f);
        static readonly Color BeamOrange = new Color(0.95f, 0.48f, 0.08f);
        static readonly Color Wood = new Color(0.70f, 0.53f, 0.32f);
        static readonly Color Carton = new Color(0.78f, 0.60f, 0.38f);
        static readonly Color CartonDark = new Color(0.66f, 0.49f, 0.29f);
        static readonly Color ToteBlue = new Color(0.18f, 0.42f, 0.82f);
        static readonly Color SafetyYellow = new Color(0.98f, 0.78f, 0.08f);
        static readonly Color Skin = new Color(0.93f, 0.78f, 0.64f);
        static readonly Color Navy = new Color(0.14f, 0.18f, 0.30f);
        static readonly Color Vest = new Color(1.00f, 0.52f, 0.10f);
        static readonly Color White = new Color(0.95f, 0.95f, 0.95f);
        static readonly Color Glass = new Color(0.35f, 0.55f, 0.70f);

        /// <summary>현재 프로젝트의 적재 단위 (LayoutWorld가 설정)</summary>
        public static LoadUnitType Unit = LoadUnitType.Pallet;
        public static Vector3 CargoDims = new Vector3(1.1f, 1.4f, 1.1f); // 폭(x) × 높이(y) × 길이(z), m

        // ================================================================
        // 진입점
        // ================================================================

        /// <summary>배치 항목 하나의 3D 모델 (루트에 클릭용 BoxCollider 포함)</summary>
        public static GameObject Build(PlacedItem item, Transform parent)
        {
            var mb = new MeshBuilder();
            var s = item.Size;
            Transform lift = null;

            switch (item.Shape)
            {
                case ItemShape.RackRow:
                    bool asrs = item.Type != EquipmentType.SelectiveRack;
                    RackRow(mb, s, item, asrs ? Steel : RackBlue, asrs ? Silver : BeamOrange);
                    break;
                case ItemShape.ShuttleBlock:
                    ShuttleBlock(mb, s, item);
                    break;
                case ItemShape.Conveyor:
                    Conveyor(mb, s, EquipmentCatalog.Get(item.Type).Color);
                    break;
                case ItemShape.Vehicle:
                    Vehicle(mb, item.Type, s);
                    break;
                case ItemShape.Person:
                    Worker(mb);
                    break;
            }

            GameObject root;
            if (item.Shape == ItemShape.Mast)
            {
                root = new GameObject(item.Label);
                root.transform.SetParent(parent, false);
                CraneBody(mb, s, EquipmentCatalog.Get(item.Type).Color);
                mb.Build("Body", root.transform);
                var lmb = new MeshBuilder();
                CraneLift(lmb, s, EquipmentCatalog.Get(item.Type).Color);
                lift = lmb.Build("Lift", root.transform).transform;
                lift.localPosition = new Vector3(0, 0.6f, 0);
            }
            else
            {
                root = new GameObject(item.Label);
                root.transform.SetParent(parent, false);
                mb.Build("Model", root.transform);
            }

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, s.y / 2f, 0);
            col.size = new Vector3(Mathf.Max(0.3f, s.x), Mathf.Max(0.3f, s.y), Mathf.Max(0.3f, s.z));
            return root;
        }

        /// <summary>운반 설비 위에서 화물이 놓이는 위치 (로컬)</summary>
        public static Vector3 CarryOffset(EquipmentType t, Vector3 size)
        {
            switch (t)
            {
                case EquipmentType.Forklift: return new Vector3(0, 0.12f, size.z * 0.28f + 0.75f);
                case EquipmentType.PalletTruck: return new Vector3(0, 0.1f, 0.3f);
                case EquipmentType.AGV: return new Vector3(0, 0.42f, 0);
                case EquipmentType.AMR: return new Vector3(0, 0.62f, 0);
                case EquipmentType.Worker: return new Vector3(0, 0.85f, 0.38f);
                default: return new Vector3(0, size.y, 0);
            }
        }

        /// <summary>시뮬레이션에서 움직이는 화물 한 단위</summary>
        public static GameObject BuildCargo(Transform parent, bool outbound)
        {
            var mb = new MeshBuilder();
            CargoUnit(mb, Vector3.zero, CargoDims, Unit, outbound ? 1 : 0);
            var go = mb.Build("Cargo", parent);
            return go;
        }

        // ================================================================
        // 화물
        // ================================================================

        /// <summary>바닥 중심 base에 적재 단위 하나 (파렛트+짐 / 토트 / 카톤)</summary>
        public static void CargoUnit(MeshBuilder mb, Vector3 b, Vector3 d, LoadUnitType unit, int variant)
        {
            switch (unit)
            {
                case LoadUnitType.Pallet:
                {
                    // 파렛트: 상판 + 받침 3줄
                    float ph = Mathf.Min(0.144f, d.y * 0.2f);
                    mb.Box(b + new Vector3(0, ph - 0.012f, 0), new Vector3(d.x, 0.024f, d.z), Wood);
                    for (int i = -1; i <= 1; i++)
                        mb.Box(b + new Vector3(d.x * 0.42f * i, (ph - 0.024f) / 2f, 0), new Vector3(0.1f, ph - 0.024f, d.z), Wood * 0.85f);
                    // 짐: 박스 덩어리 + 랩핑 띠
                    float lh = Mathf.Max(0.2f, d.y - ph);
                    var lc = variant % 3 == 0 ? Carton : (variant % 3 == 1 ? CartonDark : new Color(0.83f, 0.70f, 0.50f));
                    mb.Box(b + new Vector3(0, ph + lh / 2f, 0), new Vector3(d.x * 0.96f, lh, d.z * 0.96f), lc);
                    mb.Box(b + new Vector3(0, ph + lh * 0.55f, 0), new Vector3(d.x * 0.97f, 0.05f, d.z * 0.97f), Silver);
                    break;
                }
                case LoadUnitType.Tote:
                {
                    var c = variant % 2 == 0 ? ToteBlue : new Color(0.85f, 0.25f, 0.20f);
                    mb.Box(b + new Vector3(0, d.y * 0.45f, 0), new Vector3(d.x * 0.94f, d.y * 0.9f, d.z * 0.94f), c);
                    mb.Box(b + new Vector3(0, d.y * 0.95f, 0), new Vector3(d.x, d.y * 0.1f, d.z), c * 0.75f);
                    break;
                }
                default:
                {
                    var c = variant % 2 == 0 ? Carton : CartonDark;
                    mb.Box(b + new Vector3(0, d.y / 2f, 0), d * 0.98f, c);
                    mb.Box(b + new Vector3(0, d.y * 0.985f, 0), new Vector3(d.x * 0.2f, d.y * 0.03f, d.z * 0.99f), new Color(0.85f, 0.75f, 0.55f));
                    break;
                }
            }
        }

        // ================================================================
        // 보관 설비
        // ================================================================

        /// <summary>선택식/자동창고 랙 한 열: 프레임(기둥+브레이싱), 단마다 빔, 칸마다 적재물</summary>
        static void RackRow(MeshBuilder mb, Vector3 s, PlacedItem item, Color frame, Color beam)
        {
            int levels = Mathf.Max(1, item.Levels > 0 ? item.Levels : Mathf.RoundToInt(s.y / 1.6f));
            float pitch = item.LevelPitch > 0 ? item.LevelPitch : s.y / levels;
            float slotW = item.SlotWidth > 0 ? item.SlotWidth : 1.2f;
            int perBay = item.Unit == LoadUnitType.Pallet ? 2 : 3;
            float bayW = slotW * perBay + 0.1f;
            int bays = Mathf.Max(1, Mathf.FloorToInt(s.z / bayW));
            float realBay = s.z / bays;
            float hx = s.x / 2f - 0.05f;
            float topY = levels * pitch;

            // 프레임: 각 기둥 위치에 앞뒤 기둥 + 가로대 + 사선 브레이싱
            for (int i = 0; i <= bays; i++)
            {
                float z = -s.z / 2f + realBay * i;
                mb.Box(new Vector3(-hx, topY / 2f, z), new Vector3(0.09f, topY, 0.09f), frame);
                mb.Box(new Vector3(hx, topY / 2f, z), new Vector3(0.09f, topY, 0.09f), frame);
                int braces = Mathf.Max(1, Mathf.RoundToInt(topY / 1.5f));
                for (int k = 0; k < braces; k++)
                {
                    float y0 = topY * k / braces + 0.1f, y1 = topY * (k + 1) / braces;
                    mb.Box(new Vector3(0, y0, z), new Vector3(s.x - 0.1f, 0.04f, 0.04f), frame);
                    mb.Beam(new Vector3(-hx, y0, z), new Vector3(hx, y1, z), 0.035f, frame);
                }
                // 바닥 고정판
                mb.Box(new Vector3(-hx, 0.01f, z), new Vector3(0.18f, 0.02f, 0.18f), DarkGray);
                mb.Box(new Vector3(hx, 0.01f, z), new Vector3(0.18f, 0.02f, 0.18f), DarkGray);
            }

            // 빔: 1단은 바닥 적치, 2단부터 빔 위
            for (int l = 1; l <= levels; l++)
            {
                float y = l * pitch - 0.06f;
                if (l == levels) y = topY - 0.06f;
                mb.Box(new Vector3(-hx, y, 0), new Vector3(0.06f, 0.12f, s.z), beam);
                mb.Box(new Vector3(hx, y, 0), new Vector3(0.06f, 0.12f, s.z), beam);
            }

            // 적재물
            var rnd = new System.Random(item.Label != null ? item.Label.GetHashCode() : 7);
            var d = new Vector3(s.x - 0.15f, Mathf.Max(0.2f, pitch - (item.Unit == LoadUnitType.Pallet ? 0.3f : 0.12f)), slotW - 0.12f);
            int slots = bays * perBay;
            for (int l = 0; l < levels; l++)
            {
                float y = l == 0 ? 0f : l * pitch;
                for (int j = 0; j < slots; j++)
                {
                    if (rnd.NextDouble() > item.Fill) continue;
                    int bay = j / perBay, inBay = j % perBay;
                    float z = -s.z / 2f + realBay * bay + 0.05f + (inBay + 0.5f) * ((realBay - 0.1f) / perBay);
                    float hScale = 0.75f + 0.25f * (float)rnd.NextDouble();
                    CargoUnit(mb, new Vector3(0, y, z), new Vector3(d.x, d.y * hScale, d.z), item.Unit, rnd.Next(3));
                }
            }
        }

        /// <summary>셔틀랙 블록: 레인마다 레일, 기둥 격자, 레인 단위로 채운 적재물</summary>
        static void ShuttleBlock(MeshBuilder mb, Vector3 s, PlacedItem item)
        {
            var frame = new Color(0.20f, 0.30f, 0.55f);
            int levels = Mathf.Max(1, item.Levels);
            float pitch = item.LevelPitch > 0 ? item.LevelPitch : s.y / levels;
            float laneW = item.SlotWidth > 0 ? item.SlotWidth : 1.4f;
            int lanes = Mathf.Max(1, Mathf.RoundToInt(s.x / laneW));
            laneW = s.x / lanes;
            float topY = levels * pitch;
            int posts = Mathf.Max(2, Mathf.RoundToInt(s.z / 2.5f));

            for (int i = 0; i <= lanes; i++)
            {
                float x = -s.x / 2f + laneW * i;
                for (int k = 0; k <= posts; k++)
                {
                    float z = -s.z / 2f + s.z * k / posts;
                    mb.Box(new Vector3(x, topY / 2f, z), new Vector3(0.08f, topY, 0.08f), frame);
                }
                for (int l = 1; l <= levels; l++)
                    mb.Box(new Vector3(x, l * pitch - 0.05f, 0), new Vector3(0.1f, 0.1f, s.z), Silver);  // 셔틀 레일
            }

            var rnd = new System.Random(item.Label != null ? item.Label.GetHashCode() : 11);
            float unitLen = CargoDims.z + 0.1f;
            int perLane = Mathf.Max(1, Mathf.FloorToInt(s.z / unitLen));
            for (int i = 0; i < lanes; i++)
            {
                float x = -s.x / 2f + laneW * (i + 0.5f);
                for (int l = 0; l < levels; l++)
                {
                    float fill = Mathf.Clamp01(item.Fill + ((float)rnd.NextDouble() - 0.5f) * 0.5f);
                    int n = Mathf.RoundToInt(perLane * fill);
                    if (n <= 0) continue;
                    float y = l == 0 ? 0f : l * pitch;
                    float len = n * unitLen;
                    // 레인 안쪽(출고면 반대쪽)부터 채운 모습: 연속 파렛트 줄
                    float zc = s.z / 2f - len / 2f;
                    var d = new Vector3(laneW - 0.25f, Mathf.Max(0.2f, pitch - 0.3f) * (0.8f + 0.2f * (float)rnd.NextDouble()), len - 0.1f);
                    if (item.Unit == LoadUnitType.Pallet)
                    {
                        mb.Box(new Vector3(x, y + 0.07f, zc), new Vector3(d.x, 0.14f, d.z), Wood);
                        mb.Box(new Vector3(x, y + 0.14f + (d.y - 0.14f) / 2f, zc), new Vector3(d.x * 0.96f, d.y - 0.14f, d.z), l % 2 == 0 ? Carton : CartonDark);
                    }
                    else
                    {
                        mb.Box(new Vector3(x, y + d.y / 2f, zc), new Vector3(d.x, d.y, d.z), item.Unit == LoadUnitType.Tote ? ToteBlue : Carton);
                    }
                }
            }
        }

        /// <summary>스태커크레인 본체: 하부 주행대차, 2본 마스트, 상부 가이드, 제어반</summary>
        static void CraneBody(MeshBuilder mb, Vector3 s, Color c)
        {
            float H = s.y;
            mb.Box(new Vector3(0, 0.3f, 0), new Vector3(0.7f, 0.5f, 2.4f), c);                       // 하부 대차
            mb.Cylinder(new Vector3(0, 0.15f, 0.95f), 0.15f, 0.5f, Vector3.right, Black, 10);          // 주행 바퀴
            mb.Cylinder(new Vector3(0, 0.15f, -0.95f), 0.15f, 0.5f, Vector3.right, Black, 10);
            mb.Box(new Vector3(0, H / 2f, 0.55f), new Vector3(0.28f, H, 0.28f), c * 0.9f);             // 마스트
            mb.Box(new Vector3(0, H / 2f, -0.55f), new Vector3(0.28f, H, 0.28f), c * 0.9f);
            mb.Box(new Vector3(0, H - 0.15f, 0), new Vector3(0.5f, 0.3f, 1.6f), c);                     // 상부 프레임
            mb.Box(new Vector3(0.45f, 1.0f, -0.95f), new Vector3(0.35f, 1.2f, 0.4f), DarkGray);        // 제어반
        }

        /// <summary>승강대: 포크와 화물 받침 (시뮬레이션에서 위아래로 움직인다)</summary>
        static void CraneLift(MeshBuilder mb, Vector3 s, Color c)
        {
            float w = Mathf.Max(0.9f, s.x);
            mb.Box(new Vector3(0, 0, 0), new Vector3(w, 0.12f, 1.0f), c * 1.1f);
            mb.Box(new Vector3(-w * 0.3f, 0.06f, 0), new Vector3(w * 1.6f, 0.05f, 0.12f), Silver);  // 텔레스코픽 포크
            mb.Box(new Vector3(0, 0.3f, -0.48f), new Vector3(w, 0.5f, 0.05f), Silver);
        }

        // ================================================================
        // 이송/운반 설비
        // ================================================================

        /// <summary>롤러 컨베이어: 사이드 프레임, 롤러, 다리</summary>
        static void Conveyor(MeshBuilder mb, Vector3 s, Color c)
        {
            bool alongX = s.x > s.z;
            float len = alongX ? s.x : s.z;
            float wid = alongX ? s.z : s.x;
            Vector3 L = alongX ? Vector3.right : Vector3.forward;
            Vector3 W = alongX ? Vector3.forward : Vector3.right;
            float top = s.y;

            // 사이드 프레임
            mb.Box(W * (wid / 2f - 0.04f) + Vector3.up * (top - 0.08f), Abs(L * len + W * 0.08f) + Vector3.up * 0.16f, c);
            mb.Box(-W * (wid / 2f - 0.04f) + Vector3.up * (top - 0.08f), Abs(L * len + W * 0.08f) + Vector3.up * 0.16f, c);
            // 롤러
            int rollers = Mathf.Clamp(Mathf.FloorToInt(len / 0.2f), 2, 600);
            for (int i = 0; i < rollers; i++)
            {
                float t = -len / 2f + (i + 0.5f) * len / rollers;
                mb.Cylinder(L * t + Vector3.up * (top - 0.06f), 0.04f, wid - 0.12f, W, Silver, 6);
            }
            // 다리
            int legs = Mathf.Max(2, Mathf.CeilToInt(len / 1.5f) + 1);
            for (int i = 0; i < legs; i++)
            {
                float t = -len / 2f + 0.1f + (len - 0.2f) * i / (legs - 1);
                foreach (float side in new[] { -1f, 1f })
                    mb.Box(L * t + W * side * (wid / 2f - 0.06f) + Vector3.up * (top - 0.16f) / 2f, new Vector3(0.06f, top - 0.16f, 0.06f), DarkGray);
                mb.Box(L * t + Vector3.up * 0.25f, Abs(W * (wid - 0.1f)) + new Vector3(0.05f, 0.05f, 0.05f) - Abs(W * 0.05f), DarkGray);
            }
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static void Vehicle(MeshBuilder mb, EquipmentType t, Vector3 s)
        {
            switch (t)
            {
                case EquipmentType.Forklift: Forklift(mb, s); break;
                case EquipmentType.PalletTruck: PalletTruck(mb, s); break;
                case EquipmentType.AGV: Agv(mb, s); break;
                case EquipmentType.AMR: Amr(mb, s); break;
                default: ShuttleCarrier(mb, s); break; // 셔틀 차량
            }
        }

        /// <summary>카운터밸런스 지게차: 차체, 카운터웨이트, 오버헤드가드, 마스트, 포크, 바퀴</summary>
        static void Forklift(MeshBuilder mb, Vector3 s)
        {
            float w = s.x, l = s.z, h = s.y;
            float bodyLen = l * 0.62f;
            float bodyZ = -l / 2f + bodyLen / 2f + 0.05f;
            float frontZ = bodyZ + bodyLen / 2f;
            mb.Box(new Vector3(0, 0.55f, bodyZ), new Vector3(w * 0.95f, 0.55f, bodyLen), SafetyYellow);              // 차체
            mb.Box(new Vector3(0, 0.75f, -l / 2f + 0.3f), new Vector3(w * 0.95f, 0.85f, 0.55f), DarkGray);           // 카운터웨이트
            mb.Box(new Vector3(0, 0.95f, bodyZ - 0.1f), new Vector3(0.5f, 0.12f, 0.45f), Black);                       // 시트
            mb.Box(new Vector3(0, 1.2f, bodyZ - 0.32f), new Vector3(0.5f, 0.45f, 0.08f), Black);                      // 등받이
            mb.Cylinder(new Vector3(0, 1.25f, bodyZ + 0.35f), 0.16f, 0.04f, new Vector3(0, 1f, -0.6f), Black, 10);   // 핸들
            // 오버헤드가드
            float gy = 2.05f, gz0 = bodyZ - 0.55f, gz1 = bodyZ + 0.45f, gx = w / 2f - 0.1f;
            foreach (float gzz in new[] { gz0, gz1 })
                foreach (float sx in new[] { -gx, gx })
                    mb.Box(new Vector3(sx, (0.8f + gy) / 2f, gzz), new Vector3(0.06f, gy - 0.8f, 0.06f), Black);
            mb.Box(new Vector3(-gx, gy, (gz0 + gz1) / 2f), new Vector3(0.06f, 0.06f, gz1 - gz0), Black);
            mb.Box(new Vector3(gx, gy, (gz0 + gz1) / 2f), new Vector3(0.06f, 0.06f, gz1 - gz0), Black);
            for (int i = 0; i <= 4; i++)
                mb.Box(new Vector3(0, gy, gz0 + (gz1 - gz0) * i / 4f), new Vector3(w - 0.2f, 0.05f, 0.05f), Black);
            // 마스트
            float mz = frontZ + 0.1f;
            mb.Box(new Vector3(-0.32f, h / 2f, mz), new Vector3(0.1f, h, 0.14f), DarkGray);
            mb.Box(new Vector3(0.32f, h / 2f, mz), new Vector3(0.1f, h, 0.14f), DarkGray);
            mb.Box(new Vector3(0, h - 0.05f, mz), new Vector3(0.74f, 0.1f, 0.14f), DarkGray);
            // 포크 캐리지 + 포크
            mb.Box(new Vector3(0, 0.45f, mz + 0.1f), new Vector3(0.95f, 0.55f, 0.06f), DarkGray);
            mb.Box(new Vector3(-0.28f, 0.06f, mz + 0.1f + 0.55f), new Vector3(0.12f, 0.05f, 1.1f), Steel);
            mb.Box(new Vector3(0.28f, 0.06f, mz + 0.1f + 0.55f), new Vector3(0.12f, 0.05f, 1.1f), Steel);
            // 바퀴
            float wx = w / 2f - 0.08f;
            mb.Cylinder(new Vector3(-wx, 0.3f, frontZ - 0.35f), 0.3f, 0.25f, Vector3.right, Black, 14);
            mb.Cylinder(new Vector3(wx, 0.3f, frontZ - 0.35f), 0.3f, 0.25f, Vector3.right, Black, 14);
            mb.Cylinder(new Vector3(-wx, 0.24f, -l / 2f + 0.45f), 0.24f, 0.22f, Vector3.right, Black, 14);
            mb.Cylinder(new Vector3(wx, 0.24f, -l / 2f + 0.45f), 0.24f, 0.22f, Vector3.right, Black, 14);
            // 경광등
            mb.Cylinder(new Vector3(0, gy + 0.08f, gz0 + 0.1f), 0.06f, 0.1f, Vector3.up, Vest, 8);
        }

        /// <summary>전동 파렛트트럭: 포크 2개, 구동부, 손잡이</summary>
        static void PalletTruck(MeshBuilder mb, Vector3 s)
        {
            float l = s.z;
            mb.Box(new Vector3(-0.2f, 0.05f, 0.3f), new Vector3(0.17f, 0.08f, 1.15f), Steel);
            mb.Box(new Vector3(0.2f, 0.05f, 0.3f), new Vector3(0.17f, 0.08f, 1.15f), Steel);
            mb.Box(new Vector3(0, 0.5f, -l / 2f + 0.45f), new Vector3(0.7f, 0.9f, 0.45f), new Color(0.85f, 0.25f, 0.18f));
            mb.Beam(new Vector3(0, 0.95f, -l / 2f + 0.35f), new Vector3(0, 1.2f, -l / 2f + 0.05f), 0.05f, Black);
            mb.Box(new Vector3(0, 1.22f, -l / 2f + 0.03f), new Vector3(0.4f, 0.06f, 0.06f), Black);
            mb.Cylinder(new Vector3(0, 0.12f, -l / 2f + 0.45f), 0.12f, 0.1f, Vector3.right, Black, 10);
        }

        /// <summary>파렛트 AGV: 낮은 차체, 범퍼 띠, 리프트 판, 라이다</summary>
        static void Agv(MeshBuilder mb, Vector3 s)
        {
            float w = s.x, l = s.z;
            mb.Box(new Vector3(0, 0.2f, 0), new Vector3(w, 0.32f, l), new Color(0.92f, 0.92f, 0.94f));
            mb.Box(new Vector3(0, 0.2f, l / 2f + 0.02f), new Vector3(w * 0.9f, 0.1f, 0.05f), SafetyYellow);
            mb.Box(new Vector3(0, 0.2f, -l / 2f - 0.02f), new Vector3(w * 0.9f, 0.1f, 0.05f), SafetyYellow);
            mb.Box(new Vector3(0, 0.38f, 0), new Vector3(w * 0.85f, 0.04f, l * 0.8f), DarkGray);       // 리프트 판
            mb.Cylinder(new Vector3(w / 2f - 0.12f, 0.42f, l / 2f - 0.12f), 0.07f, 0.1f, Vector3.up, Black, 10); // 라이다
            mb.Box(new Vector3(-w / 2f + 0.1f, 0.4f, l / 2f - 0.1f), new Vector3(0.08f, 0.06f, 0.08f), new Color(0.2f, 0.9f, 0.4f)); // 상태등
            mb.Box(new Vector3(0, 0.03f, 0), new Vector3(w * 0.8f, 0.06f, l * 0.8f), Black);           // 하부
        }

        /// <summary>AMR: 둥근 느낌의 낮은 본체 위에 토트 선반</summary>
        static void Amr(MeshBuilder mb, Vector3 s)
        {
            float w = s.x, l = s.z;
            var body = new Color(0.94f, 0.95f, 0.97f);
            mb.Box(new Vector3(0, 0.17f, 0), new Vector3(w, 0.28f, l - 0.2f), body);
            mb.Cylinder(new Vector3(0, 0.17f, l / 2f - 0.1f), w / 2f, 0.28f, Vector3.up, body, 16);
            mb.Cylinder(new Vector3(0, 0.17f, -l / 2f + 0.1f), w / 2f, 0.28f, Vector3.up, body, 16);
            mb.Box(new Vector3(0, 0.12f, l / 2f), new Vector3(w * 0.6f, 0.06f, 0.06f), new Color(0.2f, 0.7f, 0.95f)); // 라이트 바
            // 선반 기둥과 판
            float px = w / 2f - 0.05f, pz = l / 2f - 0.15f;
            foreach (float sx in new[] { -px, px })
                foreach (float sz in new[] { -pz, pz })
                    mb.Box(new Vector3(sx, 0.75f, sz), new Vector3(0.03f, 0.9f, 0.03f), DarkGray);
            mb.Box(new Vector3(0, 0.58f, 0), new Vector3(w, 0.03f, l - 0.25f), Steel);
            mb.Box(new Vector3(0, 1.18f, 0), new Vector3(w, 0.03f, l - 0.25f), Steel);
            mb.Cylinder(new Vector3(0, 0.36f, l / 2f - 0.2f), 0.06f, 0.08f, Vector3.up, Black, 10);
        }

        static void ShuttleCarrier(MeshBuilder mb, Vector3 s)
        {
            mb.Box(new Vector3(0, s.y / 2f, 0), new Vector3(s.x * 0.9f, s.y, s.z), new Color(0.85f, 0.35f, 0.65f));
            mb.Box(new Vector3(0, s.y + 0.02f, 0), new Vector3(s.x * 0.8f, 0.04f, s.z * 0.8f), DarkGray);
        }

        /// <summary>작업자: 안전모, 머리, 조끼 입은 몸통, 팔, 다리</summary>
        static void Worker(MeshBuilder mb)
        {
            mb.Box(new Vector3(-0.1f, 0.42f, 0), new Vector3(0.15f, 0.84f, 0.18f), Navy);
            mb.Box(new Vector3(0.1f, 0.42f, 0), new Vector3(0.15f, 0.84f, 0.18f), Navy);
            mb.Box(new Vector3(0, 0.04f, 0.04f), new Vector3(0.42f, 0.08f, 0.26f), Black);   // 신발
            mb.Box(new Vector3(0, 1.15f, 0), new Vector3(0.44f, 0.62f, 0.26f), Vest);
            mb.Box(new Vector3(0, 1.1f, 0.135f), new Vector3(0.44f, 0.05f, 0.01f), Silver);  // 반사띠
            mb.Box(new Vector3(-0.28f, 1.12f, 0.05f), new Vector3(0.11f, 0.56f, 0.13f), Navy);
            mb.Box(new Vector3(0.28f, 1.12f, 0.05f), new Vector3(0.11f, 0.56f, 0.13f), Navy);
            mb.Cylinder(new Vector3(0, 1.6f, 0), 0.12f, 0.24f, Vector3.up, Skin, 12);
            mb.Cylinder(new Vector3(0, 1.74f, 0), 0.145f, 0.08f, Vector3.up, White, 12);       // 안전모
            mb.Box(new Vector3(0, 1.71f, 0.13f), new Vector3(0.22f, 0.02f, 0.08f), White);       // 챙
        }

        // ================================================================
        // 건물 요소
        // ================================================================

        /// <summary>도크 도어(셔터, 레벨러, 범퍼)와 바깥에 댄 트럭</summary>
        public static void Dock(MeshBuilder mb, Vector3 c, float width, bool inbound)
        {
            float dir = inbound ? -1f : 1f; // 벽 바깥 방향
            var door = inbound ? new Color(0.30f, 0.55f, 0.85f) : new Color(0.90f, 0.60f, 0.25f);
            mb.Box(c + new Vector3(0, 1.8f, 0), new Vector3(width, 3.6f, 0.15f), door);                         // 셔터
            for (int i = 1; i < 8; i++)
                mb.Box(c + new Vector3(0, 3.6f * i / 8f, -dir * 0.08f), new Vector3(width, 0.03f, 0.02f), door * 0.8f);
            mb.Box(c + new Vector3(-width / 2f - 0.1f, 1.9f, 0), new Vector3(0.2f, 3.8f, 0.3f), DarkGray);        // 문틀
            mb.Box(c + new Vector3(width / 2f + 0.1f, 1.9f, 0), new Vector3(0.2f, 3.8f, 0.3f), DarkGray);
            mb.Box(c + new Vector3(0, 3.75f, 0), new Vector3(width + 0.4f, 0.3f, 0.3f), DarkGray);
            mb.Box(c + new Vector3(0, 0.03f, -dir * 1.3f), new Vector3(2.0f, 0.06f, 2.4f), Steel);              // 도크 레벨러
            mb.Box(c + new Vector3(-0.9f, 1.0f, dir * 0.15f), new Vector3(0.25f, 0.4f, 0.15f), Black);          // 범퍼
            mb.Box(c + new Vector3(0.9f, 1.0f, dir * 0.15f), new Vector3(0.25f, 0.4f, 0.15f), Black);

            // 트럭: 트레일러 + 캡 + 바퀴
            float tz = c.z + dir * 6.6f;
            mb.Box(new Vector3(c.x, 2.4f, tz), new Vector3(2.5f, 2.7f, 12.5f), White);
            mb.Box(new Vector3(c.x, 1.0f, tz), new Vector3(2.3f, 0.2f, 12.5f), DarkGray);
            float cabZ = c.z + dir * 14.2f;
            mb.Box(new Vector3(c.x, 1.9f, cabZ), new Vector3(2.4f, 2.6f, 2.2f), inbound ? new Color(0.25f, 0.45f, 0.75f) : new Color(0.80f, 0.35f, 0.20f));
            mb.Box(new Vector3(c.x, 2.4f, cabZ + dir * 1.11f), new Vector3(2.1f, 0.9f, 0.02f), Glass);
            foreach (float wz in new[] { tz + dir * 3.5f, tz + dir * 4.8f, cabZ })
                foreach (float wx in new[] { -1.05f, 1.05f })
                    mb.Cylinder(new Vector3(c.x + wx, 0.5f, wz), 0.5f, 0.35f, Vector3.right, Black, 12);
        }

        /// <summary>바닥 경로선: 가는 띠 + 방향 화살표 (arrows=false면 화살표 없음)</summary>
        public static void FloorPath(MeshBuilder mb, Vector3 a, Vector3 b, Color c, bool arrows)
        {
            a.y = b.y = 0.035f;
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.2f) return;
            var dir = d / len;
            mb.Box((a + b) * 0.5f, new Vector3(0.28f, 0.02f, len), c, Quaternion.LookRotation(dir, Vector3.up));
            if (!arrows) return;
            int n = Mathf.Max(1, Mathf.FloorToInt(len / 5f));
            for (int i = 0; i < n; i++)
            {
                var p = a + dir * (len * (i + 0.6f) / n);
                p.y = 0.05f;
                var back1 = Quaternion.Euler(0, 35f, 0) * -dir;
                var back2 = Quaternion.Euler(0, -35f, 0) * -dir;
                mb.Beam(p, p + back1 * 0.9f, 0.16f, c);
                mb.Beam(p, p + back2 * 0.9f, 0.16f, c);
            }
        }
    }
}
