using UnityEngine;

namespace Assets._Project.Features.StatusViewModels
{
    // HUD do player (PlayerHUD.prefab). O PlayerController instancia e liga as barras.
    public sealed class PlayerHud : MonoBehaviour
    {
        public StatusBarUI Health;
        public StatusBarUI Stamina;
        [Tooltip("Some com o HUD inteiro durante as cutscenes.")]
        public CanvasGroup Group;
        [Tooltip("Estrela ao lado das barras. Dá um tranco ao levar dano e pulsa com pouca vida.")]
        public RectTransform Emblem;
        public float DamagePunch = 0.35f;

        private HealthComponent _health;
        private StaminaComponent _stamina;
        private float _punch;

        public void Bind(HealthComponent health, StaminaComponent stamina)
        {
            _health = health;
            _stamina = stamina;

            Health.Bind(health);
            Stamina.Bind(stamina);

            _health.Damaged += OnDamaged;
            _stamina.ConsumeFailed += OnStaminaRefused;
        }

        private void Awake()
        {
            if (Group)
                Group.alpha = 0f;
        }

        private void OnDestroy()
        {
            if (_health)
                _health.Damaged -= OnDamaged;

            if (_stamina)
                _stamina.ConsumeFailed -= OnStaminaRefused;
        }

        private void Update()
        {
            if (Group)
                Group.alpha = Mathf.MoveTowards(Group.alpha, CutsceneComponent.AnyPlaying ? 0f : 1f, 2f * Time.unscaledDeltaTime);

            if (Emblem == null)
                return;

            float scale = 1f + _punch;

            if (_health && !_health.IsDead && _health.Normalized < Health.LowThreshold)
                scale += 0.12f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));

            _punch = Mathf.MoveTowards(_punch, 0f, 2f * Time.unscaledDeltaTime);
            Emblem.localScale = Vector3.one * scale;
        }

        private void OnDamaged(float amount)
        {
            _punch = DamagePunch;
        }

        private void OnStaminaRefused(float requested, float available)
        {
            Stamina.Flash();
        }
    }
}
