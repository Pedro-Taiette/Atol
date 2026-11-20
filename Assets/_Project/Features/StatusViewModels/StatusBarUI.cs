using UnityEngine;
using UnityEngine.UI;

namespace Assets._Project.Features.StatusViewModels
{
    public interface IStatusPool
    {
        float Normalized { get; }
    }

    // Barra na tela (canto inferior direito). Cria o próprio canvas se a cena não tiver um.
    public sealed class StatusBarUI : MonoBehaviour
    {
        public string Label = "Status";
        public Color FillColor = new Color(1f, 0.8f, 0.25f, 1f);
        public Color BackgroundColor = new Color(0f, 0f, 0f, 0.55f);
        public Vector2 Size = new Vector2(320f, 16f);
        public Vector2 Offset = new Vector2(28f, 28f);
        public float Smoothing = 12f;

        private IStatusPool _pool;
        private GameObject _root;
        private RectTransform _fill;
        private float _displayed;

        public void Bind(IStatusPool pool, string label = null)
        {
            _pool = pool;

            if (!string.IsNullOrEmpty(label))
                Label = label;

            if (_root == null)
                CreateBar();

            _root.name = $"{Label} Bar";
            _displayed = _pool != null ? _pool.Normalized : 0f;
            ApplyFill();
        }

        private void Update()
        {
            if (_pool == null || _fill == null)
                return;

            _displayed = Mathf.Lerp(_displayed, _pool.Normalized, Smoothing * Time.deltaTime);
            ApplyFill();
        }

        private void OnEnable()
        {
            if (_root)
                _root.SetActive(true);
        }

        private void OnDisable()
        {
            if (_root)
                _root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_root)
                Destroy(_root);
        }

        private void ApplyFill()
        {
            if (_fill)
                _fill.anchorMax = new Vector2(Mathf.Clamp01(_displayed), 1f);
        }

        private void CreateBar()
        {
            Canvas canvas = FindAnyObjectByType<Canvas>();

            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                GameObject canvasObject = new GameObject("Status Bars Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 80;

                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            RectTransform bar = BuildBar(canvas.transform, Size, BackgroundColor, FillColor, out _fill);
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(1f, 0f);
            bar.anchoredPosition = new Vector2(-Offset.x, Offset.y);

            _root = bar.gameObject;
            _root.SetActive(isActiveAndEnabled);
        }

        public static RectTransform BuildBar(Transform parent, Vector2 size, Color backgroundColor, Color fillColor, out RectTransform fill)
        {
            GameObject barObject = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            barObject.transform.SetParent(parent, false);

            RectTransform bar = barObject.GetComponent<RectTransform>();
            bar.sizeDelta = size;

            Image background = barObject.GetComponent<Image>();
            background.color = backgroundColor;
            background.raycastTarget = false;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(bar, false);

            fill = fillObject.GetComponent<RectTransform>();
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;

            Image fillImage = fillObject.GetComponent<Image>();
            fillImage.color = fillColor;
            fillImage.raycastTarget = false;

            return bar;
        }
    }
}
