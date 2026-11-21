using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets._Project.Features.Boat
{
    // Seta na parte de baixo da tela mostrando pra onde o vento sopra (relativo à câmera) e a força dele.
    // Aparece só com o player no barco, igual o BoatControlHintUI.
    [DisallowMultipleComponent]
    public sealed class WindIndicatorUI : MonoBehaviour
    {
        public SpectralOceanFFT Ocean;
        public Color ArrowColor = new Color(0.8f, 0.95f, 1f, 0.9f);
        public float ArrowSize = 36f;
        public Vector2 BottomCenterOffset = new Vector2(0f, 36f);

        private RectTransform _arrow;
        private TMP_Text _label;
        private bool _visible;

        private void Awake()
        {
            if (Ocean == null)
                Ocean = FindAnyObjectByType<SpectralOceanFFT>();

            CreateOverlay();
            SetVisible(false);
        }

        public void SetVisible(bool value)
        {
            _visible = value;

            if (_arrow)
                _arrow.gameObject.SetActive(value);

            if (_label)
                _label.enabled = value;
        }

        private void LateUpdate()
        {
            if (!_visible || Ocean == null || Camera.main == null)
                return;

            Vector2 wind = Ocean.CurrentWindDirectionXZ;

            if (wind.sqrMagnitude < 0.0001f)
                return;

            // Seta pra cima = vento soprando pra frente da tela.
            float windAngle = Mathf.Atan2(wind.x, wind.y) * Mathf.Rad2Deg;
            float relative = windAngle - Camera.main.transform.eulerAngles.y;

            _arrow.localRotation = Quaternion.Euler(0f, 0f, -relative);
            _label.text = $"Vento {Mathf.RoundToInt(Ocean.CurrentWindStrength * 100f)}%";
        }

        private void CreateOverlay()
        {
            Canvas canvas = FindAnyObjectByType<Canvas>();

            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                GameObject canvasObject = new GameObject("Wind Indicator Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 80;

                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            GameObject root = new GameObject("Wind Indicator", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0f);
            rootRect.anchorMax = new Vector2(0.5f, 0f);
            rootRect.pivot = new Vector2(0.5f, 0f);
            rootRect.anchoredPosition = BottomCenterOffset;
            rootRect.sizeDelta = new Vector2(200f, ArrowSize + 30f);

            GameObject arrowObject = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            arrowObject.transform.SetParent(root.transform, false);

            _arrow = arrowObject.GetComponent<RectTransform>();
            _arrow.anchorMin = new Vector2(0.5f, 1f);
            _arrow.anchorMax = new Vector2(0.5f, 1f);
            _arrow.pivot = new Vector2(0.5f, 0.5f);
            _arrow.anchoredPosition = new Vector2(0f, -ArrowSize * 0.5f);
            _arrow.sizeDelta = new Vector2(ArrowSize, ArrowSize);

            Image arrowImage = arrowObject.GetComponent<Image>();
            arrowImage.sprite = CreateArrowSprite();
            arrowImage.color = ArrowColor;
            arrowImage.raycastTarget = false;

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(root.transform, false);

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(0f, 28f);

            _label = labelObject.GetComponent<TextMeshProUGUI>();
            _label.fontSize = 17;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = new Color(1f, 1f, 1f, 0.94f);
            _label.outlineWidth = 0.18f;
            _label.outlineColor = new Color(0f, 0f, 0f, 0.8f);
            _label.raycastTarget = false;
        }

        // Triângulo apontando pra cima, desenhado na mão pra não depender de sprite no projeto.
        private static Sprite CreateArrowSprite()
        {
            const int size = 64;

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                float halfWidth = (1f - (float)y / size) * size * 0.42f;

                for (int x = 0; x < size; x++)
                {
                    bool inside = Mathf.Abs(x - size * 0.5f) <= halfWidth && y > size * 0.08f;
                    pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
