using System;
using System.Collections;
using System.Collections.Generic;
using LayoutRecommender.Data;
using LayoutRecommender.Planning;
using UnityEngine;

namespace LayoutRecommender.World
{
    /// <summary>
    /// 설비 3D 모델을 한 번씩 렌더 텍스처로 찍어 팔레트 아이콘으로 쓴다.
    /// 장면과 멀리 떨어진 곳에 모델과 전용 카메라를 두고 한 프레임 렌더링한 뒤 정리한다.
    /// </summary>
    public static class IconRenderer
    {
        static readonly Dictionary<EquipmentType, RenderTexture> Cache = new Dictionary<EquipmentType, RenderTexture>();

        public static bool TryGet(EquipmentType t, out Texture tex)
        {
            tex = null;
            if (Cache.TryGetValue(t, out var rt) && rt != null && rt.IsCreated()) { tex = rt; return true; }
            return false;
        }

        /// <summary>아직 없는 아이콘을 만들고 끝나면 onDone을 부른다.</summary>
        public static void Ensure(MonoBehaviour host, Color background, Action onDone)
        {
            bool all = true;
            foreach (var d in EquipmentCatalog.All) if (!TryGet(d.Type, out _)) all = false;
            if (all) { onDone?.Invoke(); return; }
            host.StartCoroutine(RenderAll(background, onDone));
        }

        static IEnumerator RenderAll(Color background, Action onDone)
        {
            var root = new GameObject("IconStudio").transform;
            root.position = new Vector3(0, -5000f, 0);
            var cams = new List<Camera>();
            Light light = null;
            if (UnityEngine.Object.FindAnyObjectByType<Light>() == null)
            {
                light = new GameObject("IconLight").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50, -30, 0);
            }

            int i = 0;
            foreach (var def in EquipmentCatalog.All)
            {
                if (TryGet(def.Type, out _)) continue;
                var spot = root.position + new Vector3(i * 200f, 0, 0);
                i++;

                GameObject model;
                try { model = SampleModel(def, root); }
                catch (Exception e) { Debug.LogWarning($"[LayoutRecommender] 아이콘 모델 생성 실패 {def.NameKo}: {e.Message}"); continue; }
                model.transform.position = spot;

                var bounds = new Bounds(spot + Vector3.up, Vector3.one);
                bool first = true;
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    if (first) { bounds = r.bounds; first = false; }
                    else bounds.Encapsulate(r.bounds);
                }

                var rt = new RenderTexture(160, 120, 24) { name = "Icon_" + def.Type, antiAliasing = 4 };
                rt.Create();
                Cache[def.Type] = rt;

                var cam = new GameObject("IconCam_" + def.Type).AddComponent<Camera>();
                cam.transform.SetParent(root, true);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = background;
                cam.fieldOfView = 28f;
                cam.targetTexture = rt;
                cam.depth = -50;
                var viewDir = new Vector3(1.0f, 0.75f, 1.25f).normalized;   // 앞-오른쪽-위에서 본 모습
                float radius = bounds.extents.magnitude;
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
                cam.transform.position = bounds.center + viewDir * dist;
                cam.transform.LookAt(bounds.center);
                cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 2f);
                cam.farClipPlane = dist + radius * 2f;
                cams.Add(cam);
            }

            // 카메라가 렌더 텍스처에 한 번 그리도록 두 프레임 기다린다
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return null;

            foreach (var c in cams) if (c != null) c.enabled = false;
            UnityEngine.Object.Destroy(root.gameObject);
            if (light != null) UnityEngine.Object.Destroy(light.gameObject);
            onDone?.Invoke();
        }

        /// <summary>아이콘용 대표 모델</summary>
        static GameObject SampleModel(EquipmentDef def, Transform parent)
        {
            var unit = def.Type == EquipmentType.AMR ? LoadUnitType.Tote : LoadUnitType.Pallet;
            var prevUnit = ModelFactory.Unit;
            var prevDims = ModelFactory.CargoDims;
            ModelFactory.Unit = unit;
            ModelFactory.CargoDims = unit == LoadUnitType.Pallet ? new Vector3(1.1f, 1.2f, 1.1f) : new Vector3(0.4f, 0.3f, 0.6f);

            PlacedItem item;
            switch (def.Type)
            {
                case EquipmentType.SelectiveRack:
                    item = new PlacedItem { Type = def.Type, Shape = ItemShape.RackRow, Size = new Vector3(1.2f, 4.5f, 5.6f), Levels = 3, LevelPitch = 1.5f, SlotWidth = 1.3f, Unit = LoadUnitType.Pallet, Fill = 0.85f, Label = "icon" };
                    break;
                case EquipmentType.ShuttleRack:
                    item = new PlacedItem { Type = def.Type, Shape = ItemShape.ShuttleBlock, Size = new Vector3(4.2f, 4.5f, 6f), Levels = 3, LevelPitch = 1.5f, SlotWidth = 1.4f, Unit = LoadUnitType.Pallet, Fill = 0.7f, Label = "icon" };
                    break;
                case EquipmentType.StackerCraneSingle:
                case EquipmentType.StackerCraneDouble:
                    item = new PlacedItem { Type = def.Type, Shape = ItemShape.Mast, Size = new Vector3(def.Type == EquipmentType.StackerCraneDouble ? 1.3f : 1.1f, 7f, 2f), Label = "icon" };
                    break;
                case EquipmentType.Conveyor:
                    item = new PlacedItem { Type = def.Type, Shape = ItemShape.Conveyor, Size = new Vector3(1.2f, 0.8f, 3f), Label = "icon" };
                    break;
                case EquipmentType.Worker:
                    item = new PlacedItem { Type = def.Type, Shape = ItemShape.Person, Size = new Vector3(0.6f, 1.7f, 0.6f), Label = "icon" };
                    break;
                default:
                    float h = def.Type == EquipmentType.Forklift ? 2.2f : (def.Type == EquipmentType.PalletTruck ? 1.2f : 0.6f);
                    item = new PlacedItem { Type = def.Type, Shape = ItemShape.Vehicle, Size = new Vector3(def.FootprintWidthM, h, def.FootprintDepthM), Label = "icon" };
                    break;
            }

            var go = ModelFactory.Build(item, parent);
            if (item.Shape == ItemShape.RackRow || item.Shape == ItemShape.ShuttleBlock)
                ModelFactory.BuildLoads(item, 0.85f, go.transform);
            var lift = go.transform.Find("Lift");
            if (lift != null) lift.localPosition = new Vector3(0, 2.5f, 0);

            // 운반 설비는 화물을 실은 모습으로
            if (item.Shape == ItemShape.Vehicle)
            {
                var cargo = ModelFactory.BuildCargo(go.transform, false);
                cargo.transform.localPosition = ModelFactory.CarryOffset(def.Type, item.Size);
            }

            ModelFactory.Unit = prevUnit;
            ModelFactory.CargoDims = prevDims;
            return go;
        }
    }
}
