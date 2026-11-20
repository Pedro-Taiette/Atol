using Assets._Project.Framework.Logging;
using UnityEngine;

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

            _pool = Source as IStatusPool;

            if (_pool == null)
            {
                _log.Error("No IStatusPool source found, add a HealthComponent");
                enabled = false;
                return;
            }

            CreateCanvas();

            _displayed = _pool.Normalized;
        }

        private void LateUpdate()
        {
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

            StatusBarUI.BuildBar(_canvas, pixelSize, BackgroundColor, FillColor, out _fill);
        }
    }
}
