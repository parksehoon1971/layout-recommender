using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LayoutRecommender.World
{
    /// <summary>
    /// 박스/원기둥을 하나의 메시에 모아 그린다. 색마다 서브메시를 나눠 재질을 붙인다.
    /// 랙 적재물처럼 개수가 많은 형상도 오브젝트 하나로 그려서 가볍다.
    /// </summary>
    public class MeshBuilder
    {
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly Dictionary<Color, List<int>> _tris = new Dictionary<Color, List<int>>();
        readonly List<Color> _order = new List<Color>();

        public bool IsEmpty => _verts.Count == 0;

        List<int> Tris(Color c)
        {
            if (!_tris.TryGetValue(c, out var list))
            {
                list = new List<int>();
                _tris[c] = list;
                _order.Add(c);
            }
            return list;
        }

        static readonly Vector3[] FaceNormals =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };

        /// <summary>중심과 크기로 박스를 추가한다 (회전 선택).</summary>
        public void Box(Vector3 center, Vector3 size, Color color, Quaternion? rotation = null)
        {
            var rot = rotation ?? Quaternion.identity;
            var h = size * 0.5f;
            var tris = Tris(color);
            foreach (var n in FaceNormals)
            {
                // 면의 두 접선 방향
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                Vector3 c0 = Vector3.Scale(n, h);
                Vector3 du = Vector3.Scale(u, h);
                Vector3 dv = Vector3.Scale(v, h);
                int start = _verts.Count;
                _verts.Add(center + rot * (c0 - du - dv));
                _verts.Add(center + rot * (c0 + du - dv));
                _verts.Add(center + rot * (c0 + du + dv));
                _verts.Add(center + rot * (c0 - du + dv));
                var wn = rot * n;
                for (int i = 0; i < 4; i++) _normals.Add(wn);
                // Unity는 바깥에서 볼 때 시계 방향이 앞면 (Cross·법선 > 0)
                var a = _verts[start]; var b = _verts[start + 1]; var c = _verts[start + 2];
                bool outward = Vector3.Dot(Vector3.Cross(b - a, c - a), wn) > 0f;
                if (outward)
                {
                    tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
                    tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
                }
                else
                {
                    tris.Add(start); tris.Add(start + 2); tris.Add(start + 1);
                    tris.Add(start); tris.Add(start + 3); tris.Add(start + 2);
                }
            }
        }

        /// <summary>두 점 사이를 잇는 막대 (사선 브레이싱 등)</summary>
        public void Beam(Vector3 a, Vector3 b, float thickness, Color color)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Box((a + b) * 0.5f, new Vector3(thickness, thickness, len), color, Quaternion.LookRotation(d / len, Mathf.Abs(d.y / len) > 0.99f ? Vector3.forward : Vector3.up));
        }

        /// <summary>원기둥 (바퀴, 롤러, 라이다). axis 방향으로 길이 length.</summary>
        public void Cylinder(Vector3 center, float radius, float length, Vector3 axis, Color color, int segments = 12)
        {
            axis = axis.normalized;
            var rot = Quaternion.FromToRotation(Vector3.up, axis);
            var tris = Tris(color);
            float hl = length * 0.5f;
            // 옆면
            int start = _verts.Count;
            for (int i = 0; i <= segments; i++)
            {
                float t = i * Mathf.PI * 2f / segments;
                var n = new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t));
                _verts.Add(center + rot * (n * radius + Vector3.down * hl));
                _verts.Add(center + rot * (n * radius + Vector3.up * hl));
                _normals.Add(rot * n);
                _normals.Add(rot * n);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2;
                tris.Add(a); tris.Add(a + 1); tris.Add(a + 3);
                tris.Add(a); tris.Add(a + 3); tris.Add(a + 2);
            }
            // 위/아래 뚜껑
            for (int cap = 0; cap < 2; cap++)
            {
                var n = cap == 0 ? Vector3.up : Vector3.down;
                int c = _verts.Count;
                _verts.Add(center + rot * (n * hl));
                _normals.Add(rot * n);
                for (int i = 0; i <= segments; i++)
                {
                    float t = i * Mathf.PI * 2f / segments;
                    _verts.Add(center + rot * (new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)) * radius + n * hl));
                    _normals.Add(rot * n);
                }
                for (int i = 0; i < segments; i++)
                {
                    if (cap == 0) { tris.Add(c); tris.Add(c + i + 2); tris.Add(c + i + 1); }
                    else { tris.Add(c); tris.Add(c + i + 1); tris.Add(c + i + 2); }
                }
            }
        }

        /// <summary>렌더러가 붙은 오브젝트로 만든다.</summary>
        public GameObject Build(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (IsEmpty) return go;

            var mesh = new Mesh { name = name };
            if (_verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_verts);
            mesh.SetNormals(_normals);
            mesh.subMeshCount = _order.Count;
            var mats = new Material[_order.Count];
            for (int i = 0; i < _order.Count; i++)
            {
                mesh.SetTriangles(_tris[_order[i]], i);
                mats[i] = LayoutWorld.MaterialFor(_order[i]);
            }
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }
    }
}
