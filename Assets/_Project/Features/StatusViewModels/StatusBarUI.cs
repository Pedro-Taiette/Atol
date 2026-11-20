using UnityEngine;
using UnityEngine.UI;

namespace Assets._Project.Features.StatusViewModels
{
    /// <summary>A pool that can be shown as a fill bar (stamina, health...).</summary>
    public interface IStatusPool
    {
        /// <summary>Current value as a 0..1 fraction of the maximum.</summary>
        float Normalized { get; }
    }

    /// <summary>
    /// Screen-space fill bar for any <see cref="IStatusPool"/>. Builds its own overlay
    /// in the bottom-right corner when bound, so no Canvas setup is needed in the scene.
    /// </summary>
    public sealed class StatusBarUI : MonoBehaviour
    {
        [SerializeField] private string label = "Status";
        [SerializeField] private Color fillColor = new Color(1f, 0.8f, 0.25f, 1f);
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Vector2 size = new Vector2(320f, 16f);
        [Tooltip("Distance from the bottom-right corner of the screen.")]
        [SerializeField] private Vector2 bottomRightOffset = new Vector2(28f, 28f);
        [Tooltip("How fast the displayed fill catches up with the real value.")]
        [SerializeField, Min(0f)] private float smoothing = 12f;

        private IStatusPool pool;
        private GameObject root;
        private RectTransform fill;
        private float displayed;

        public void Bind(IStatusPool source, string barLabel = null)
        {
            pool = source;
            if (!string.IsNullOrEmpty(barLabel))
                label = barLabel;

            if (root == null)
                CreateOverlay();

            root.name = $"{label} Bar";
            displayed = pool != null ? pool.Normalized : 0f;
            ApplyFill();
        }

        private void Update()
        {
            if (pool == null || fill == null)
                return;

            displayed = Mathf.Lerp(displayed, pool.Normalized, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            ApplyFill();
        }

        private void OnEnable()
        {
            if (root != null)
                root.SetActive(true);
        }

        private void OnDisable()
        {
            if (root != null)
                root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (root != null)
                Destroy(root);
        }

        private void ApplyFill()
        {
            if (fill != null)
                fill.anchorMax = new Vector2(Mathf.Clamp01(displayed), 1f);
        }

        private void CreateOverlay()
        {
            Canvas existingCanvas = FindAnyObjectByType<Canvas>();
            Canvas canvas = existingCanvas != null && existingCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? existingCanvas
                : null;

            if (canvas == null)
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

            root = new GameObject($"{label} Bar", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(canvas.transform, false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 0f);
            rootRect.anchorMax = new Vector2(1f, 0f);
            rootRect.pivot = new Vector2(1f, 0f);
            rootRect.anchoredPosition = new Vector2(-bottomRightOffset.x, bottomRightOffset.y);
            rootRect.sizeDelta = size;
            Image background = root.GetComponent<Image>();
            background.color = backgroundColor;
            background.raycastTarget = false;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(root.transform, false);
            fill = fillObject.GetComponent<RectTransform>();
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            Image fillImage = fillObject.GetComponent<Image>();
            fillImage.color = fillColor;
            fillImage.raycastTarget = false;

            root.SetActive(isActiveAndEnabled);
        }
    }
}
