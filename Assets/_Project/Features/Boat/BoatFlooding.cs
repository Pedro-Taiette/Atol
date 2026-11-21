using Assets._Project.Features.StatusViewModels;
using System;
using UnityEngine;

namespace Assets._Project.Features.Boat
{
    // Água acumulada no barco (ex: tubarão preso no casco). Deixa o barco lento e, quando enche, vira ele
    // pro lado de onde a água está entrando. Depois o capsize do BoatController cuida de desvirar.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoatController))]
    public class BoatFlooding : MonoBehaviour, IStatusPool
    {
        public float Capacity = 100f;
        public float DrainRate = 3f;
        [Tooltip("Freio aplicado no barco quando ele está cheio.")]
        public float MaxSlowdown = 1.5f;
        public float CapsizeTorque = 6f;
        public float CapsizeTimeout = 4f;

        public float Water { get; private set; }
        public float Normalized => Mathf.Clamp01(Water / Capacity);
        public bool IsCapsizing { get; private set; }

        public event Action Flooded;

        private BoatController _boat;
        private WorldStatusBarUI _bar;
        private float _floodSide = 1f;
        private float _capsizeStartTime;
        private float _lastFillTime;

        private void Awake()
        {
            _boat = GetComponent<BoatController>();

            _bar = gameObject.AddComponent<WorldStatusBarUI>();
            _bar.FillColor = new Color(0.25f, 0.55f, 1f, 1f);
            _bar.Offset = new Vector3(0f, 3f, 0f);
            _bar.Size = new Vector2(2f, 0.18f);
            _bar.Bind(this);
            _bar.enabled = false;
        }

        public void AddWater(float amount, Vector3 fromPoint)
        {
            if (IsCapsizing || amount <= 0f)
                return;

            _floodSide = Mathf.Sign(transform.InverseTransformPoint(fromPoint).x);
            _lastFillTime = Time.time;

            Water = Mathf.Min(Water + amount, Capacity);

            if (Water >= Capacity)
            {
                IsCapsizing = true;
                _capsizeStartTime = Time.time;
                Flooded?.Invoke();
            }
        }

        private void FixedUpdate()
        {
            Rigidbody body = _boat.Body;

            if (body == null)
                return;

            if (IsCapsizing)
                UpdateCapsize(body);
            else if (Water > 0f && Time.time - _lastFillTime > 1f)
                Water = Mathf.Max(0f, Water - DrainRate * Time.fixedDeltaTime);

            if (Water > 0f)
            {
                Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
                body.AddForce(-planarVelocity * MaxSlowdown * Normalized, ForceMode.Acceleration);
            }

            _bar.enabled = Water > 0f;
        }

        private void UpdateCapsize(Rigidbody body)
        {
            if (_boat.IsRecoveringFromCapsize || Time.time - _capsizeStartTime > CapsizeTimeout)
            {
                IsCapsizing = false;
                Water = 0f;
                return;
            }

            // Mesmo torque que um peso puxando a lateral pra baixo daria.
            Vector3 side = transform.right * _floodSide;
            body.AddTorque(Vector3.Cross(side, Vector3.down) * CapsizeTorque, ForceMode.Acceleration);
        }
    }
}
