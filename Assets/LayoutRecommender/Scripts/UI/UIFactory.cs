using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace LayoutRecommender.UI
{
    public static class Theme
    {
        public static readonly Color Background = new Color(0.10f, 0.12f, 0.15f, 1f);
        public static readonly Color Panel = new Color(0.15f, 0.18f, 0.22f, 0.97f);
        public static readonly Color PanelLight = new Color(0.22f, 0.26f, 0.31f, 1f);
        public static readonly Color Field = new Color(0.07f, 0.08f, 0.10f, 1f);
        public static readonly Color Accent = new Color(0.16f, 0.55f, 0.95f, 1f);
        public static readonly Color AccentDim = new Color(0.13f, 0.33f, 0.55f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.95f, 0.97f, 1f);
        public static readonly Color TextDim = new Color(0.62f, 0.67f, 0.73f, 1f);
        public static readonly Color Danger = new Color(0.95f, 0.40f, 0.35f, 1f);
        public static readonly Color Success = new Color(0.35f, 0.80f, 0.50f, 1f);
    }

    /// <summary>
    /// 코드로 UGUI를 만드는 도우미. 씬/프리팹 없이 Play만 누르면 화면이 구성되도록 한다.
    /// </summary>
    public static class UIFactory
    {
        static Font _font;
        static readonly string[] FontNames =
        {
            "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "AppleGothic",
            "Noto Sans CJK KR", "Noto Sans KR", "NanumGothic", "Arial"
        };

        /// <summary>한글이 나오는 OS 폰트를 우선 사용한다.</summary>
        public static Font DefaultFont
        {
            get
            {
                if (_font == null)
                {
                    try { _font = UnityEngine.Font.CreateDynamicFontFromOSFont(FontNames, 32); }
                    catch { _font = null; }
                    if (_font == null) _font = Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");
                }
                return _font;
            }
        }

        // ---------- 루트 ----------

        public static Canvas CreateCanvas(string name, int sortOrder = 0)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // ---------- 기본 요소 ----------

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        /// <summary>부모를 꽉 채운다. 값은 안쪽 여백(px).</summary>
        public static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static void Anchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        /// <summary>상단에 붙는 띠 (높이 h)</summary>
        public static void DockTop(RectTransform rt, float height, float left = 0, float right = 0)
            => Anchor(rt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(left, -height), new Vector2(-right, 0));

        /// <summary>하단에 붙는 띠 (높이 h)</summary>
        public static void DockBottom(RectTransform rt, float height, float left = 0, float right = 0)
            => Anchor(rt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(left, 0), new Vector2(-right, height));

        /// <summary>좌측에 붙는 기둥 (폭 w)</summary>
        public static void DockLeft(RectTransform rt, float width, float top = 0, float bottom = 0)
            => Anchor(rt, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, bottom), new Vector2(width, -top));

        /// <summary>우측에 붙는 기둥 (폭 w)</summary>
        public static void DockRight(RectTransform rt, float width, float top = 0, float bottom = 0)
            => Anchor(rt, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-width, bottom), new Vector2(0, -top));

        public static Text Label(Transform parent, string text, int size = 22, Color? color = null,
            TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Label");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = DefaultFont;
            t.fontSize = size;
            t.color = color ?? Theme.Text;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        // ---------- 레이아웃 ----------

        public static LayoutElement Size(Component c, float width = -1, float height = -1, float flexWidth = -1, float flexHeight = -1)
        {
            if (!c.TryGetComponent(out LayoutElement le)) le = c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0) { le.preferredWidth = width; le.minWidth = width; }
            if (height >= 0) { le.preferredHeight = height; le.minHeight = height; }
            if (flexWidth >= 0) le.flexibleWidth = flexWidth;
            if (flexHeight >= 0) le.flexibleHeight = flexHeight;
            return le;
        }

        public static VerticalLayoutGroup VLayout(GameObject go, float spacing = 8, RectOffset padding = null,
            TextAnchor align = TextAnchor.UpperLeft)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset(0, 0, 0, 0);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HLayout(GameObject go, float spacing = 8, RectOffset padding = null,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding ?? new RectOffset(0, 0, 0, 0);
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            return h;
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        // ---------- 컨트롤 ----------

        public static Button CreateButton(Transform parent, string label, Action onClick, Color? color = null, int fontSize = 22)
        {
            var rt = Panel(parent, "Button", color ?? Theme.Accent);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = rt.GetComponent<Image>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            btn.colors = colors;

            var t = Label(rt, label, fontSize, Theme.Text, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 8, 8, 2, 2);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        public static void SetButtonLabel(Button b, string label)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = label;
        }

        public static InputField CreateInputField(Transform parent, string initial, InputField.ContentType contentType,
            Action<string> onEndEdit, int fontSize = 22)
        {
            var rt = Panel(parent, "InputField", Theme.Field);
            var img = rt.GetComponent<Image>();

            var text = Label(rt, "", fontSize, Theme.Text, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(text.rectTransform, 12, 12, 4, 4);

            var placeholder = Label(rt, "", fontSize, Theme.TextDim, TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(placeholder.rectTransform, 12, 12, 4, 4);

            var field = rt.gameObject.AddComponent<InputField>();
            field.targetGraphic = img;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.contentType = contentType;
            field.lineType = InputField.LineType.SingleLine;
            field.text = initial;
            if (onEndEdit != null) field.onEndEdit.AddListener(s => onEndEdit(s));
            return field;
        }

        /// <summary>◀ 값 ▶ 형태의 선택기 (드롭다운 대용)</summary>
        public static RectTransform CreateSelector(Transform parent, string[] options, int index, Action<int> onChanged, int fontSize = 22)
        {
            var row = Rect(parent, "Selector");
            HLayout(row.gameObject, 6);
            int current = Mathf.Clamp(index, 0, options.Length - 1);
            Text valueLabel = null;

            void Step(int delta)
            {
                current = (current + delta + options.Length) % options.Length;
                valueLabel.text = options[current];
                onChanged?.Invoke(current);
            }

            var prev = CreateButton(row, "◀", () => Step(-1), Theme.PanelLight, fontSize);
            Size(prev, width: 52);

            var box = Panel(row, "Value", Theme.Field);
            Size(box, flexWidth: 1);
            valueLabel = Label(box, options[current], fontSize, Theme.Text, TextAnchor.MiddleCenter);
            Stretch(valueLabel.rectTransform);

            var next = CreateButton(row, "▶", () => Step(1), Theme.PanelLight, fontSize);
            Size(next, width: 52);
            return row;
        }

        /// <summary>세로 스크롤되는 긴 글 상자. 반환값은 내용을 넣을 Text.</summary>
        public static Text CreateScrollText(Transform parent, int fontSize, out ScrollRect scroll)
        {
            var root = Rect(parent, "ScrollText");
            var hitArea = root.gameObject.AddComponent<Image>();
            hitArea.color = new Color(0, 0, 0, 0); // 휠 입력을 받기 위한 투명 영역
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var viewport = Rect(root, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            VLayout(content.gameObject, 0, new RectOffset(0, 8, 0, 8));

            var text = Label(content, "", fontSize, Theme.Text, TextAnchor.UpperLeft);
            scroll.viewport = viewport;
            scroll.content = content;
            return text;
        }

        /// <summary>가로 슬라이더 (0~1 기본). 핸들을 끌거나 트랙을 눌러 값을 바꾼다.</summary>
        public static Slider CreateSlider(Transform parent, float min, float max, float value, Action<float> onChanged)
        {
            var root = Rect(parent, "Slider");
            var slider = root.gameObject.AddComponent<Slider>();

            var bg = Panel(root, "Background", Theme.Field);
            Anchor(bg, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -6), new Vector2(0, 6));

            var fillArea = Rect(root, "Fill Area");
            Anchor(fillArea, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, -6), new Vector2(0, 6));
            var fill = Panel(fillArea, "Fill", Theme.Accent);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0, 1);
            fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;

            var handleArea = Rect(root, "Handle Slide Area");
            Anchor(handleArea, Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0));
            var handle = Panel(handleArea, "Handle", Color.white);
            handle.anchorMin = new Vector2(0, 0); handle.anchorMax = new Vector2(0, 1);
            handle.sizeDelta = new Vector2(20, 0);

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            if (onChanged != null) slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        /// <summary>켜고 끄는 버튼 (■ 켜짐 / □ 꺼짐)</summary>
        public static Button CreateToggle(Transform parent, string label, bool initial, Action<bool> onChanged,
            bool interactable = true, int fontSize = 20)
        {
            bool state = initial;
            Button btn = null;
            Image img = null;

            void Apply()
            {
                img.color = !interactable ? Theme.Field : (state ? Theme.AccentDim : Theme.PanelLight);
                SetButtonLabel(btn, (state && interactable ? "■  " : "□  ") + label);
            }

            btn = CreateButton(parent, label, () =>
            {
                state = !state;
                Apply();
                onChanged?.Invoke(state);
            }, Theme.PanelLight, fontSize);
            img = btn.GetComponent<Image>();
            var t = btn.GetComponentInChildren<Text>();
            t.alignment = TextAnchor.MiddleLeft;
            if (!interactable) t.color = Theme.TextDim;
            btn.interactable = interactable;
            Apply();
            return btn;
        }
    }
}
