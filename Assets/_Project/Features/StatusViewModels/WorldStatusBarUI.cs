using Assets._Project.Framework.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace Assets._Project.Features.StatusViewModels
{
    // Barra acima da cabeça (ex: vida dos animais), sempre virada pra câmera.
    [DisallowMultipleComponent]
    public sealed class WorldStatusBarUI : MonoBehaviour
    {
        [Tooltip("Componente que implementa IStatusPool. Se vazio, usa o HealthComponent do objeto.")]
        public MonoBehaviour Source;
        public Vector3 Offset = new Vector3(0f, 2f, 0f);
        public Color FillColor = new Color(0.85f, 0.2f, 0.2f, 1f);
        public Color BackgroundColor = new Color(0f, 0f, 0f, 0.55f);
        public Vector2 Size = new Vector2(1.2f, 0.14f);
        public float Smoothing = 12f;

        private const float PixelsPerUnit = 100f;

        private IStatusPool _pool;
        private Transform _canvas;
        private RectTransform _fill;
        private float _displayed;
        private FrameworkLogger _log;

        private void Awake()
        {
            _log = new(this);

            if (Source == null)
                Source = GetComponent<HealthComponent>();

            if (Source is IStatusPool pool)
                Bind(pool);
        }

        private void Start()
        {
            if (_pool == null)
            {
                _log.Error("No IStatusPool source found, add a HealthComponent");
                enabled = false;
            }
        }

        // Pra quando a barra é criada por código (ex: água no barco).
        public void Bind(IStatusPool pool)
        {
            _pool = pool;

            if (_canvas == null)
                CreateCanvas();

            _canvas.gameObject.SetActive(enabled);
            _displayed = _pool.Normalized;
        }

        private void LateUpdate()
        {
            if (_pool == null)
                return;

            _displayed = Mathf.Lerp(_displayed, _pool.Normalized, Smoothing * Time.deltaTime);
            _fill.anchorMax = new Vector2(Mathf.Clamp01(_displayed), 1f);

            _canvas.position = transform.position + Offset;

            if (Camera.main)
                _canvas.rotation = Camera.main.transform.rotation;
        }

        private void OnEnable()
        {
            if (_canvas)
                _canvas.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (_canvas)
                _canvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_canvas)
                Destroy(_canvas.gameObject);
        }

        private void CreateCanvas()
        {
            // Fica fora da hierarquia do objeto pra escala dele não distorcer a barra.
            GameObject canvasObject = new GameObject($"{name} Status Bar", typeof(RectTransform), typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            Vector2 pixelSize = Size * PixelsPerUnit;
            canvasObject.GetComponent<RectTransform>().sizeDelta = pixelSize;

            _canvas = canvasObject.transform;
            _canvas.localScale = Vector3.one / PixelsPerUnit;
            _canvas.position = transform.position + Offset;

            GameObject barObject = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            barObject.transform.SetParent(_canvas, false);
            barObject.GetComponent<RectTransform>().sizeDelta = pixelSize;

            Image background = barObject.GetComponent<Image>();
            background.color = BackgroundColor;
            background.raycastTarget = false;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(barObject.transform, false);

            _fill = fillObject.GetComponent<RectTransform>();
            _fill.anchorMin = Vector2.zero;
            _fill.anchorMax = Vector2.one;
            _fill.offsetMin = Vector2.zero;
            _fill.offsetMax = Vector2.zero;

            Image fillImage = fillObject.GetComponent<Image>();
            fillImage.color = FillColor;
            fillImage.raycastTarget = false;
        }
    }
}
