using System.Collections;
using System.Collections.Generic;
using LayoutRecommender.Data;
using LayoutRecommender.Planning;
using UnityEngine;

namespace LayoutRecommender.World
{
    /// <summary>3D로 배치된 설비 하나. 클릭하면 이 컴포넌트로 배치 정보를 찾는다.</summary>
    public class EquipmentInstance : MonoBehaviour
    {
        public PlacedItem Item { get; set; }   // 실행 중에만 쓰는 값 (직렬화하지 않음)
        public Transform Lift;   // 스태커크레인 승강대 (없으면 null)

        readonly List<Renderer> _renderers = new List<Renderer>();
        readonly List<Material[]> _baseMats = new List<Material[]>();
        Material[][] _highlightMats;

        public void CacheRenderers()
        {
            _renderers.Clear();
            _baseMats.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                _renderers.Add(r);
                _baseMats.Add(r.sharedMaterials);
            }
            _highlightMats = null;
        }

        public void SetHighlight(bool on)
        {
            if (on && _highlightMats == null)
            {
                _highlightMats = new Material[_baseMats.Count][];
                for (int i = 0; i < _baseMats.Count; i++)
                {
                    var src = _baseMats[i];
                    var dst = new Material[src.Length];
                    for (int k = 0; k < src.Length; k++)
                        dst[k] = LayoutWorld.MaterialFor(Color.Lerp(src[k] != null ? src[k].color : Color.white, new Color(0.4f, 1f, 0.9f), 0.45f));
                    _highlightMats[i] = dst;
                }
            }
            for (int i = 0; i < _renderers.Count; i++)
                if (_renderers[i] != null) _renderers[i].sharedMaterials = on ? _highlightMats[i] : _baseMats[i];
        }

        /// <summary>위에서 떨어지듯 나타나는 효과</summary>
        public void PlayDrop(float duration) => StartCoroutine(Drop(duration));

        IEnumerator Drop(float duration)
        {
            var end = transform.localScale;
            var endPos = transform.position;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                transform.localScale = Vector3.Lerp(end * 0.3f, end, e);
                transform.position = endPos + Vector3.up * (1f - e) * 6f;
                yield return null;
            }
            transform.localScale = end;
            transform.position = endPos;
        }
    }

    /// <summary>건물, 구역, 도크, 바닥 경로선, 설비를 3D로 만든다.</summary>
    public class LayoutWorld : MonoBehaviour
    {
        public static readonly Color InboundColor = new Color(0.20f, 0.62f, 1.00f);
        public static readonly Color OutboundColor = new Color(1.00f, 0.58f, 0.15f);

        static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();
        static Material _baseMaterial;

        Transform _envRoot;
        Transform _equipRoot;

        /// <summary>시뮬레이션 화물처럼 실행 중에 생기는 오브젝트를 두는 곳 (Clear 때 같이 지워진다)</summary>
        public Transform DynamicRoot { get; private set; }

        public static Material MaterialFor(Color c)
        {
            c.a = 1f;
            if (_baseMaterial == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _baseMaterial = new Material(tmp.GetComponent<Renderer>().sharedMaterial);
                Destroy(tmp);
            }
            if (!Materials.TryGetValue(c, out var m) || m == null)
            {
                m = new Material(_baseMaterial) { color = c };
                Materials[c] = m;
            }
            return m;
        }

        public void Clear()
        {
            if (_envRoot != null) Destroy(_envRoot.gameObject);
            if (_equipRoot != null) Destroy(_equipRoot.gameObject);
            if (DynamicRoot != null) Destroy(DynamicRoot.gameObject);
            _envRoot = new GameObject("Environment").transform;
            _envRoot.SetParent(transform, false);
            _equipRoot = new GameObject("Equipment").transform;
            _equipRoot.SetParent(transform, false);
            DynamicRoot = new GameObject("Dynamic").transform;
            DynamicRoot.SetParent(transform, false);
        }

        void OnDestroy()
        {
            foreach (var m in Materials.Values) if (m != null) Destroy(m);
            Materials.Clear();
        }

        // ---------- 건물, 구역, 도크, 경로 ----------

        public void BuildEnvironment(LayoutPlan plan, ProjectInput input)
        {
            Clear();
            ModelFactory.Unit = input.cargo.unitType;
            ModelFactory.CargoDims = new Vector3(input.cargo.widthMm, input.cargo.heightMm, input.cargo.lengthMm) / 1000f;

            float W = Mathf.Max(20f, input.building.widthM);
            float D = Mathf.Max(20f, input.building.depthM);

            var mb = new MeshBuilder();
            mb.Box(new Vector3(0, -0.05f, 0), new Vector3(W, 0.1f, D), new Color(0.30f, 0.32f, 0.35f));
            // 바깥 마당 (트럭이 서는 곳)
            mb.Box(new Vector3(0, -0.12f, -D / 2f - 12f), new Vector3(W + 20f, 0.1f, 24f), new Color(0.20f, 0.21f, 0.23f));
            mb.Box(new Vector3(0, -0.12f, D / 2f + 12f), new Vector3(W + 20f, 0.1f, 24f), new Color(0.20f, 0.21f, 0.23f));

            // 낮은 벽으로 외곽 표시 (도크 쪽은 기둥만)
            var wall = new Color(0.58f, 0.61f, 0.65f);
            mb.Box(new Vector3(-W / 2f - 0.15f, 1.0f, 0), new Vector3(0.3f, 2.0f, D), wall);
            mb.Box(new Vector3(W / 2f + 0.15f, 1.0f, 0), new Vector3(0.3f, 2.0f, D), wall);
            for (float x = -W / 2f; x <= W / 2f + 0.01f; x += 10f)
            {
                mb.Box(new Vector3(x, 2.0f, -D / 2f), new Vector3(0.4f, 4.0f, 0.4f), wall);
                mb.Box(new Vector3(x, 2.0f, D / 2f), new Vector3(0.4f, 4.0f, 0.4f), wall);
            }
            mb.Build("Building", _envRoot);

            if (plan == null) return;

            var zones = new MeshBuilder();
            foreach (var z in plan.Zones)
            {
                var a = z.Area;
                zones.Box(new Vector3(a.center.x, 0.01f, a.center.y), new Vector3(a.width, 0.02f, a.height), z.Color);
                // 구역 경계선 (노란 바닥 라인)
                var line = new Color(0.95f, 0.85f, 0.2f);
                zones.Box(new Vector3(a.center.x, 0.025f, a.yMin + 0.05f), new Vector3(a.width, 0.01f, 0.1f), line);
                zones.Box(new Vector3(a.center.x, 0.025f, a.yMin + a.height - 0.05f), new Vector3(a.width, 0.01f, 0.1f), line);
            }
            zones.Build("Zones", _envRoot);

            var docks = new MeshBuilder();
            foreach (var d in plan.Docks)
                ModelFactory.Dock(docks, d.Center, d.Width, d.Inbound);
            docks.Build("Docks", _envRoot);

            BuildFlowPaths(plan.Flow);
        }

        /// <summary>입고(파랑)/출고(주황) 이동 경로를 바닥에 선과 화살표로 그린다.</summary>
        void BuildFlowPaths(FlowNetwork f)
        {
            if (f == null || f.Access.Count == 0) return;
            var mb = new MeshBuilder();
            float y = 0f;

            // 입고: 도크 → 입고 통로 → 보관 접근 지점
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var x in f.InDockX) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
            foreach (var a in f.Access) { minX = Mathf.Min(minX, a.X); maxX = Mathf.Max(maxX, a.X); }
            foreach (var x in f.InDockX)
                ModelFactory.FloorPath(mb, new Vector3(x, y, f.InDockQueueZ - 1f), new Vector3(x, y, f.CorridorInZ), InboundColor, true);
            ModelFactory.FloorPath(mb, new Vector3(minX, y, f.CorridorInZ), new Vector3(maxX, y, f.CorridorInZ), InboundColor, false);
            foreach (var a in f.Access)
            {
                float zEnd = a.IntoAisle ? (a.SlotZMin + a.SlotZMax) / 2f : a.ZIn;
                ModelFactory.FloorPath(mb, new Vector3(a.X, y, f.CorridorInZ), new Vector3(a.X, y, zEnd), InboundColor, true);
            }

            // 출고: 보관 접근 지점 → 출고 통로 → 도크
            minX = float.MaxValue; maxX = float.MinValue;
            foreach (var x in f.OutDockX) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
            foreach (var a in f.Access) { minX = Mathf.Min(minX, a.X); maxX = Mathf.Max(maxX, a.X); }
            foreach (var a in f.Access)
            {
                float zStart = a.IntoAisle ? (a.SlotZMin + a.SlotZMax) / 2f : a.ZOut;
                ModelFactory.FloorPath(mb, new Vector3(a.X + (a.IntoAisle ? 0.35f : 0f), y, zStart), new Vector3(a.X + (a.IntoAisle ? 0.35f : 0f), y, f.CorridorOutZ), OutboundColor, true);
            }
            ModelFactory.FloorPath(mb, new Vector3(minX, y, f.CorridorOutZ), new Vector3(maxX, y, f.CorridorOutZ), OutboundColor, false);
            foreach (var x in f.OutDockX)
                ModelFactory.FloorPath(mb, new Vector3(x, y, f.CorridorOutZ), new Vector3(x, y, f.OutDockQueueZ + 1f), OutboundColor, true);

            mb.Build("FlowPaths", _envRoot);
        }

        // ---------- 설비 ----------

        public EquipmentInstance Spawn(PlacedItem item)
        {
            var root = ModelFactory.Build(item, _equipRoot);
            root.transform.position = item.Center;

            if (item.Shape == ItemShape.Mast && item.RailLength > 0f)
            {
                // 크레인 주행 레일(바닥)과 상부 가이드 레일: 움직이지 않는 구조물
                var rails = new MeshBuilder();
                float H = item.Size.y;
                rails.Box(new Vector3(0, 0.06f, 0), new Vector3(0.15f, 0.12f, item.RailLength + 2f), new Color(0.45f, 0.47f, 0.5f));
                rails.Box(new Vector3(0, H + 0.1f, 0), new Vector3(0.2f, 0.15f, item.RailLength + 2f), new Color(0.45f, 0.47f, 0.5f));
                var r = rails.Build("Rail " + item.Label, _equipRoot);
                r.transform.position = new Vector3(item.Center.x, 0, item.RailCenterZ);
            }

            var inst = root.AddComponent<EquipmentInstance>();
            inst.Item = item;
            inst.Lift = root.transform.Find("Lift");
            inst.CacheRenderers();
            return inst;
        }
    }
}
