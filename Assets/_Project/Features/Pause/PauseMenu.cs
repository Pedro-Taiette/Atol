using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Assets._Project.Features.Pause
{
    // Esc (ou Start no controle) pausa o jogo e abre o menu. Monta a própria UI, igual às barras de status.
    [DisallowMultipleComponent]
    public sealed class PauseMenu : MonoBehaviour
    {
        public static bool IsPaused { get; private set; }

        public Color DimColor = new Color(0f, 0f, 0f, 0.6f);
        public Color ButtonColor = new Color(1f, 1f, 1f, 0.12f);
        public Vector2 ButtonSize = new Vector2(320f, 56f);

        private GameObject _root;
        private GameObject _resumeButton;
        private bool _selectorWasPaused;

        private void Update()
        {
            bool pressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            pressed |= Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;

            if (pressed)
                SetPaused(!IsPaused);
        }

        private void OnDisable()
        {
            SetPaused(false);
        }

        private void OnDestroy()
        {
            if (_root)
                Destroy(_root);
        }

        public void Resume() => SetPaused(false);

        public void Quit()
        {
            SetPaused(false);

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused)
                return;

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = paused;

            // Sem isso o pulo e a câmera continuam respondendo com o jogo parado.
            InputActionMap playerActions = InputSystem.actions != null ? InputSystem.actions.FindActionMap("Player") : null;

            if (playerActions != null)
            {
                if (paused)
                    playerActions.Disable();
                else
                    playerActions.Enable();
            }

            // Uma cutscene pode ter pausado o seletor antes, então volta pro estado em que estava.
            ObjectSelector selector = ObjectSelector.Instance;

            if (selector != null)
            {
                if (paused)
                    _selectorWasPaused = selector.IsPaused;

                selector.IsPaused = paused || _selectorWasPaused;
            }

            if (_root == null)
            {
                if (!paused)
                    return;

                CreateMenu();
            }

            _root.SetActive(paused);

            if (paused && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(_resumeButton);
        }

        private void CreateMenu()
        {
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            _root = new GameObject("Pause Menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject dimObject = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            dimObject.transform.SetParent(_root.transform, false);

            RectTransform dim = dimObject.GetComponent<RectTransform>();
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.offsetMin = Vector2.zero;
            dim.offsetMax = Vector2.zero;

            dimObject.GetComponent<Image>().color = DimColor;

            TextMeshProUGUI title = CreateLabel(_root.transform, "Paused", 54);
            title.rectTransform.anchoredPosition = new Vector2(0f, ButtonSize.y * 2f);
            title.rectTransform.sizeDelta = new Vector2(600f, 80f);

            _resumeButton = CreateButton("Resume", 0f).gameObject;
            _resumeButton.GetComponent<Button>().onClick.AddListener(Resume);

            CreateButton("Quit", -(ButtonSize.y + 16f)).onClick.AddListener(Quit);
        }

        private Button CreateButton(string text, float y)
        {
            GameObject buttonObject = new GameObject($"{text} Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(_root.transform, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = ButtonSize;

            Color highlighted = ButtonColor;
            highlighted.a = Mathf.Clamp01(ButtonColor.a * 2.5f);

            Color pressed = ButtonColor;
            pressed.a = Mathf.Clamp01(ButtonColor.a * 4f);

            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = highlighted;
            colors.selectedColor = highlighted;
            colors.pressedColor = pressed;
            button.colors = colors;

            TextMeshProUGUI label = CreateLabel(buttonObject.transform, text, 26);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            return button;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize)
        {
            GameObject labelObject = new GameObject($"{text} Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 1f, 1f, 0.94f);
            label.raycastTarget = false;

            return label;
        }
    }
}
