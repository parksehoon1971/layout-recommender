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
        public PlacedItem Item;
        readonly List<Renderer> _renderers = new List<Renderer>();
        readonly List<Color> _baseColors = new List<Color>();

        public void CacheRenderers()
        {
            _renderers.Clear();
            _baseColors.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                _renderers.Add(r);
                _baseColors.Add(r.sharedMaterial != null ? r.sharedMaterial.color : Color.white);
            }
        }

        public void SetHighlight(bool on)
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                if (_renderers[i] == null) continue;
                _renderers[i].sharedMaterial = LayoutWorld.MaterialFor(on ? Color.Lerp(_baseColors[i], Color.white, 0.55f) : _baseColors[i]);
            }
        }

        /// <summary>떨어지듯 나타나는 효과</summary>
        public void PlayDrop(float duration)
        {
            StartCoroutine(Drop(duration));
        }

        IEnumerator Drop(float duration)
        {
            var end = transform.localScale;
            var endPos = transform.position;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float e = 1f - Mathf.Pow(1f - k, 3f);                       // ease-out
                transform.localScale = Vector3.Lerp(end * 0.2f, end, e);
                transform.position = endPos + Vector3.up * (1f - e) * 6f;
                yield return null;
            }
            transform.localScale = end;
            transform.position = endPos;
        }
    }

    /// <summary>건물, 구역, 도크, 설비를 3D로 만든다. 모양은 단순한 박스/캡슐.</summary>
    public class LayoutWorld : MonoBehaviour
    {
        static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();
        static Material _baseMaterial;

        Transform _envRoot;
        Transform _equipRoot;

        public static Material MaterialFor(Color c)
        {
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
            _envRoot = new GameObject("Environment").transform;
            _envRoot.SetParent(transform, false);
            _equipRoot = new GameObject("Equipment").transform;
            _equipRoot.SetParent(transform, false);
        }

        void OnDestroy()
        {
            foreach (var m in Materials.Values) if (m != null) Destroy(m);
            Materials.Clear();
        }

        // ---------- 건물, 구역, 도크 ----------

        public void BuildEnvironment(LayoutPlan plan, ProjectInput input)
        {
            Clear();
            float W = Mathf.Max(20f, input.building.widthM);
            float D = Mathf.Max(20f, input.building.depthM);

            Box(_envRoot, "Floor", new Vector3(0, -0.05f, 0), new Vector3(W, 0.1f, D), new Color(0.28f, 0.30f, 0.33f), false);

            // 낮은 벽으로 외곽 표시
            var wall = new Color(0.55f, 0.58f, 0.62f);
            Box(_envRoot, "Wall_W", new Vector3(-W / 2f - 0.15f, 0.6f, 0), new Vector3(0.3f, 1.2f, D), wall, false);
            Box(_envRoot, "Wall_E", new Vector3(W / 2f + 0.15f, 0.6f, 0), new Vector3(0.3f, 1.2f, D), wall, false);
            Box(_envRoot, "Wall_S", new Vector3(0, 0.6f, -D / 2f - 0.15f), new Vector3(W + 0.6f, 1.2f, 0.3f), wall, false);
            Box(_envRoot, "Wall_N", new Vector3(0, 0.6f, D / 2f + 0.15f), new Vector3(W + 0.6f, 1.2f, 0.3f), wall, false);

            if (plan == null) return;
            foreach (var z in plan.Zones)
            {
                var a = z.Area;
                Box(_envRoot, "Zone_" + z.Name, new Vector3(a.center.x, 0.01f, a.center.y), new Vector3(a.width, 0.02f, a.height), z.Color, false);
            }
            foreach (var d in plan.Docks)
            {
                var c = d.Inbound ? new Color(0.30f, 0.60f, 0.95f) : new Color(0.95f, 0.65f, 0.25f);
                float zOff = d.Inbound ? -0.2f : 0.2f;
                Box(_envRoot, d.Inbound ? "InDock" : "OutDock", new Vector3(d.Center.x, 1.8f, d.Center.z + zOff), new Vector3(d.Width, 3.6f, 0.4f), c, false);
            }
        }

        // ---------- 설비 ----------

        public EquipmentInstance Spawn(PlacedItem item)
        {
            var def = EquipmentCatalog.Get(item.Type);
            var root = new GameObject(item.Label);
            root.transform.SetParent(_equipRoot, false);
            root.transform.position = item.Center;
            var s = item.Size;

            switch (item.Shape)
            {
                case ItemShape.RackRow:
                    BuildRack(root.transform, s, item.Type == EquipmentType.SelectiveRack ? def.Color : Color.Lerp(def.Color, new Color(0.7f, 0.72f, 0.75f), 0.55f));
                    break;
                case ItemShape.ShuttleBlock:
                    BuildRack(root.transform, s, def.Color);
                    break;
                case ItemShape.Mast:
                    Box(root.transform, "Mast", new Vector3(0, s.y / 2f, 0), new Vector3(s.x * 0.5f, s.y, s.z * 0.4f), def.Color, true);
                    Box(root.transform, "Carriage", new Vector3(0, s.y * 0.35f, 0), new Vector3(s.x, 1.2f, s.z), Color.Lerp(def.Color, Color.white, 0.3f), true);
                    Box(root.transform, "TopRail", new Vector3(0, s.y, 0), new Vector3(s.x * 0.6f, 0.3f, s.z), def.Color * 0.8f, true);
                    break;
                case ItemShape.Conveyor:
                    Box(root.transform, "Bed", new Vector3(0, s.y - 0.08f, 0), new Vector3(s.x, 0.16f, s.z), def.Color, true);
                    Box(root.transform, "Frame", new Vector3(0, (s.y - 0.16f) / 2f, 0), new Vector3(s.x * 0.85f, s.y - 0.16f, s.z * 0.95f), def.Color * 0.6f, true);
                    break;
                case ItemShape.Vehicle:
                    Box(root.transform, "Body", new Vector3(0, Mathf.Min(s.y, 0.9f) / 2f, 0), new Vector3(s.x, Mathf.Min(s.y, 0.9f), s.z), def.Color, true);
                    if (item.Type == EquipmentType.Forklift)
                        Box(root.transform, "Mast", new Vector3(0, s.y / 2f, s.z / 2f + 0.05f), new Vector3(s.x * 0.8f, s.y, 0.12f), new Color(0.2f, 0.2f, 0.2f), true);
                    break;
                case ItemShape.Person:
                    var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    cap.name = "Body";
                    cap.transform.SetParent(root.transform, false);
                    cap.transform.localPosition = new Vector3(0, s.y / 2f, 0);
                    cap.transform.localScale = new Vector3(s.x, s.y / 2f, s.z);
                    cap.GetComponent<Renderer>().sharedMaterial = MaterialFor(def.Color);
                    break;
            }

            var inst = root.AddComponent<EquipmentInstance>();
            inst.Item = item;
            inst.CacheRenderers();
            return inst;
        }

        /// <summary>랙: 기둥과 단마다 빔을 그려 선반처럼 보이게 한다. 칸이 많으면 단순 박스로.</summary>
        void BuildRack(Transform parent, Vector3 s, Color color)
        {
            // 클릭용 바운딩 (보이지 않음)
            var hit = new GameObject("Hitbox");
            hit.transform.SetParent(parent, false);
            hit.transform.localPosition = new Vector3(0, s.y / 2f, 0);
            var col = hit.AddComponent<BoxCollider>();
            col.size = s;

            var frame = color * 0.75f;
            frame.a = 1f;
            int uprights = Mathf.Clamp(Mathf.RoundToInt(s.z / 2.7f), 2, 40);
            for (int i = 0; i <= uprights; i++)
            {
                float z = -s.z / 2f + s.z * i / uprights;
                Box(parent, "Upright", new Vector3(0, s.y / 2f, z), new Vector3(s.x, s.y, 0.12f), frame, false);
            }
            int beams = Mathf.Clamp(Mathf.RoundToInt(s.y / 1.5f), 1, 16);
            for (int b = 1; b <= beams; b++)
            {
                float y = s.y * b / beams;
                Box(parent, "Beam", new Vector3(0, y - 0.06f, 0), new Vector3(s.x, 0.12f, s.z), color, false);
            }
            // 적재물 느낌을 주는 반투명하지 않은 내부 블록
            Box(parent, "Load", new Vector3(0, s.y * 0.45f, 0), new Vector3(s.x * 0.8f, s.y * 0.85f, s.z * 0.98f), Color.Lerp(color, new Color(0.6f, 0.5f, 0.35f), 0.6f), false);
        }

        static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Color color, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            if (!keepCollider) Destroy(go.GetComponent<Collider>());
            return go;
        }
    }
}
