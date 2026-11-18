using System.Collections.Generic;
using LayoutRecommender.UI;
using UnityEngine;

namespace LayoutRecommender.RL
{
    /// <summary>
    /// LayoutEnv 상태를 3D 큐브로 그린다. 이 오브젝트의 위치가 격자 중심이다.
    /// 격자 x → 월드 +X, 격자 y=0 → 화면 위쪽(+Z) 줄.
    /// </summary>
    public class LayoutView : MonoBehaviour
    {
        public float CellW = 1f;
        public float CellD = 1f;
        public bool ShowFloor = true;
        public bool ShowLabels = true;

        readonly List<GameObject> _items = new List<GameObject>();

        public Vector3 CellCenter(float gx, float gy)
        {
            float x = (gx - LayoutProblem.GridW * 0.5f) * CellW;
            float z = (LayoutProblem.GridH * 0.5f - gy) * CellD;
            return transform.position + new Vector3(x, 0f, z);
        }

        public void Clear()
        {
            foreach (var go in _items) if (go != null) Destroy(go);
            _items.Clear();
        }

        public void Render(LayoutEnv env, string caption = null)
        {
            Clear();
            if (env?.Problem == null) return;
            var p = env.Problem;
            float gw = LayoutProblem.GridW * CellW, gd = LayoutProblem.GridH * CellD;

            if (ShowFloor)
                Box("Floor", CellCenter(LayoutProblem.GridW * 0.5f, LayoutProblem.GridH * 0.5f) + Vector3.down * 0.05f,
                    new Vector3(gw, 0.1f, gd), new Color(0.30f, 0.33f, 0.36f));

            for (int x = 0; x < LayoutProblem.GridW; x++)
                for (int y = 0; y < LayoutProblem.GridH; y++)
                    if (p.Obstacle[x, y])
                        Box("Column", CellCenter(x + 0.5f, y + 0.5f) + Vector3.up * CellW * 0.5f,
                            new Vector3(CellW * 0.4f, CellW, CellD * 0.4f), new Color(0.15f, 0.15f, 0.17f));

            foreach (var d in p.InboundDocks) DockBox(d, new Color(0.2f, 0.5f, 1f));
            foreach (var d in p.OutboundDocks) DockBox(d, new Color(1f, 0.35f, 0.3f));

            float height = Mathf.Min(CellW, CellD) * 0.6f;
            for (int i = 0; i < env.Placed.Count; i++)
            {
                var z = env.Placed[i];
                var spec = p.Zones[i];
                var center = CellCenter(z.X + z.W * 0.5f, z.Y + z.H * 0.5f) + Vector3.up * height * 0.5f;
                var size = new Vector3(z.W * CellW * 0.94f, height, z.H * CellD * 0.94f);
                Box(spec.Label, center, size, spec.Color);
                if (ShowLabels) Label(spec.Label, center + Vector3.up * (height * 0.5f + 0.05f), Mathf.Min(CellW, CellD) * 0.45f);
            }

            if (!string.IsNullOrEmpty(caption))
                Label(caption, CellCenter(LayoutProblem.GridW * 0.5f, -0.8f), Mathf.Min(CellW, CellD) * 0.5f);
        }

        void DockBox(Vector2Int d, Color c)
        {
            Box("Dock", CellCenter(d.x + 0.5f, d.y + 0.5f) + Vector3.up * 0.05f,
                new Vector3(CellW * 0.9f, 0.1f, CellD * 0.9f), c);
        }

        void Box(string name, Vector3 pos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, true);
            go.transform.position = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.material.color = color;
            _items.Add(go);
        }

        void Label(string text, Vector3 pos, float size)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(transform, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 위에서 내려다볼 때 읽히도록
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = UIFactory.DefaultFont;
            tm.fontSize = 48;
            tm.characterSize = size * 0.1f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && tm.font != null) mr.material = tm.font.material;
            _items.Add(go);
        }

        void OnDestroy() => Clear();
    }
}
