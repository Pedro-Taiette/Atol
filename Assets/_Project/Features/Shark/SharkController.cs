using Assets._Project.Features.Boat;
using Assets._Project.Features.Combat;
using Assets._Project.Features.StatusViewModels;
using Assets._Project.Framework.Logging;
using System.Collections.Generic;
using UnityEngine;

namespace Assets._Project.Features.Shark
{
    // IA do tubarão: patrulha em volta do ponto inicial, persegue o player quando ele está nadando
    // e dá um bote com a hitbox de mordida. Se o player está no barco, circula o barco e salta nele.
    // O movimento é todo feito pelo SwimmingMovement.
    [RequireComponent(typeof(SwimmingMovement))]
    [RequireComponent(typeof(HealthComponent))]
    public class SharkController : MonoBehaviour
    {
        public enum State { Patrol, Chase, Bite, Circle, Leap, Dead }

        [Header("Target")]
        public PlayerController Target;
        public float DetectionRadius = 25f;
        public float GiveUpRadius = 40f;

        [Header("Patrol")]
        public float PatrolRadius = 20f;
        public float PatrolSpeed = 2.5f;
        public float PatrolPointTime = 10f;

        [Header("Chase")]
        public float ChaseSpeed = 5f;

        [Header("Bite")]
        public DamageOnTouch Bite;
        public Vector3 BiteOffset = new Vector3(0f, 0f, 1.5f);
        public float BiteRadius = 0.8f;
        public float BiteDamage = 25f;
        public float BiteRange = 3f;
        public float BiteLungeSpeed = 9f;
        public float BiteDuration = 0.35f;
        public float BiteCooldown = 2f;

        [Header("Boat Circle")]
        public float CircleRadius = 7f;
        public float CircleSpeed = 4f;
        [Tooltip("Mais raso que o normal pra barbatana aparecer enquanto circula.")]
        public float CircleSubmersion = 0.7f;
        public Vector2 CircleTime = new Vector2(3f, 6f);

        [Header("Boat Leap")]
        public float LeapVelocity = 7f;
        public float LeapWindup = 0.6f;
        public float LeapWindupDip = 0.8f;
        public float LeapMaxSpeed = 10f;
        public float LeapHitRadius = 1.3f;
        public float LeapDamage = 20f;
        public float KnockOffSpeed = 5f;
        public float LeapCooldown = 4f;

        public bool ShowLogs = false;

        public State CurrentState { get; private set; }

        private SwimmingMovement _swimming;
        private HealthComponent _health;
        private FrameworkLogger _log;

        private Vector3 _home;
        private Vector3 _patrolPoint;
        private float _stateStartTime;
        private float _nextBiteTime;

        private float _defaultSubmersion;
        private float _circleDuration;
        private float _nextLeapTime;
        private BoatController _leapBoat;
        private bool _leapLaunched;
        private bool _leapHit;
        private readonly List<Collider> _sharkColliders = new();
        private readonly List<Collider> _boatColliders = new();

        private void Awake()
        {
            _log = new(this, showLogs: ShowLogs, prefixColor: Color.red);

            _swimming = GetComponent<SwimmingMovement>();
            _health = GetComponent<HealthComponent>();
            _health.Died += OnDied;

            _swimming.HopStarted += OnLeapLaunched;
            _swimming.HopLanded += OnLeapLanded;
            _defaultSubmersion = _swimming.TargetSubmersion;

            // O salto usa o pulo do SwimmingMovement, com os valores do tubarão.
            _swimming.EnableJumpHop = true;
            _swimming.JumpVelocity = LeapVelocity;
            _swimming.JumpDelay = LeapWindup;
            _swimming.JumpWindupDip = LeapWindupDip;
            _swimming.JumpStaminaCost = 0f;
            _swimming.JumpCooldown = 0f;
            _swimming.JumpMaxDepthError = 5f;

            if (Bite == null)
                CreateBite();

            Bite.Damage = BiteDamage;
            Bite.RepeatInterval = 0f;
            Bite.gameObject.SetActive(false);

            GetComponentsInChildren(_sharkColliders);

            _home = transform.position;
        }

        private void Start()
        {
            if (Target == null)
                Target = FindAnyObjectByType<PlayerController>();

            if (Target == null)
                _log.Warning("No PlayerController found, shark will only patrol");

            SetState(State.Patrol);
        }

        private void OnDestroy()
        {
            if (_health)
                _health.Died -= OnDied;

            if (_swimming)
            {
                _swimming.HopStarted -= OnLeapLaunched;
                _swimming.HopLanded -= OnLeapLanded;
            }
        }

        private void FixedUpdate()
        {
            switch (CurrentState)
            {
                case State.Patrol:
                    UpdatePatrol();
                    break;
                case State.Chase:
                    UpdateChase();
                    break;
                case State.Bite:
                    UpdateBite();
                    break;
                case State.Circle:
                    UpdateCircle();
                    break;
                case State.Leap:
                    UpdateLeap();
                    break;
                case State.Dead:
                    _swimming.Move(Vector2.zero);
                    break;
            }

            _swimming.ApplyMovement();
        }

        private void UpdatePatrol()
        {
            if (CanSeeTarget(DetectionRadius))
            {
                SetState(State.Chase);
                return;
            }

            if (CanSeeBoat(DetectionRadius))
            {
                SetState(State.Circle);
                return;
            }

            bool reachedPoint = FlatDistance(transform.position, _patrolPoint) < 3f;

            if (reachedPoint || Time.time - _stateStartTime > PatrolPointTime)
                PickPatrolPoint();

            SwimTowards(_patrolPoint);
        }

        private void UpdateChase()
        {
            if (!CanSeeTarget(GiveUpRadius))
            {
                SetState(State.Patrol);
                return;
            }

            Vector3 targetPosition = Target.transform.position;

            if (FlatDistance(transform.position, targetPosition) < BiteRange && Time.time >= _nextBiteTime)
            {
                SetState(State.Bite);
                return;
            }

            SwimTowards(targetPosition);
        }

        private void UpdateBite()
        {
            if (Target)
                SwimTowards(Target.transform.position);

            if (Time.time - _stateStartTime < BiteDuration)
                return;

            _nextBiteTime = Time.time + BiteCooldown;
            SetState(State.Chase);
        }

        private void UpdateCircle()
        {
            if (!CanSeeBoat(GiveUpRadius))
            {
                SetState(State.Patrol);
                return;
            }

            Vector3 boatPosition = Target.CurrentBoat.transform.position;

            // Mira um ponto um pouco à frente no círculo, assim ele gira em volta do barco.
            Vector3 fromBoat = transform.position - boatPosition;
            float angle = Mathf.Atan2(fromBoat.z, fromBoat.x) + 0.6f;
            Vector3 circlePoint = boatPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * CircleRadius;

            SwimTowards(circlePoint);

            bool closeEnough = FlatDistance(transform.position, boatPosition) < CircleRadius * 1.5f;

            if (closeEnough && Time.time - _stateStartTime > _circleDuration && Time.time >= _nextLeapTime)
                SetState(State.Leap);
        }

        private void UpdateLeap()
        {
            if (Target)
                SwimTowards(Target.transform.position);

            if (!_leapLaunched)
            {
                // Se o pulo não sair (preso ou sem água embaixo), desiste e volta a circular.
                if (Time.time - _stateStartTime > LeapWindup + 1f)
                    EndLeap();

                return;
            }

            if (!_leapHit && Target && Target.IsAboardBoat)
            {
                Vector3 bitePoint = transform.TransformPoint(BiteOffset);
                Vector3 playerCenter = Target.transform.position + Vector3.up * 0.9f;

                if (Vector3.Distance(bitePoint, playerCenter) < LeapHitRadius)
                    HitPlayerOnBoat();
            }

            if (Time.time - _stateStartTime > 5f)
                EndLeap();
        }

        private void HitPlayerOnBoat()
        {
            _leapHit = true;

            if (Target.Health)
                Target.Health.TakeDamage(LeapDamage);

            Target.KnockOffBoat(FlatDirectionTo(Target.transform.position), KnockOffSpeed);

            _log.Log("Leap hit, player knocked off the boat");
        }

        private void OnLeapLaunched()
        {
            if (CurrentState != State.Leap || Target == null)
                return;

            _leapLaunched = true;

            // Velocidade horizontal pra cair em cima do player, considerando o tempo no ar.
            float airTime = 2f * _swimming.JumpVelocity / _swimming.JumpGravity;
            float distance = FlatDistance(transform.position, Target.transform.position);
            float speed = Mathf.Min(distance / airTime, LeapMaxSpeed);

            _swimming.Speed = speed;
            _swimming.SetPlanarVelocity(FlatDirectionTo(Target.transform.position) * speed);

            SetBoatCollision(false);
        }

        private void OnLeapLanded(float impact)
        {
            if (CurrentState == State.Leap && _leapLaunched)
                EndLeap();
        }

        private void EndLeap()
        {
            SetBoatCollision(true);

            _nextLeapTime = Time.time + LeapCooldown;
            SetState(State.Patrol);
        }

        // O tubarão atravessa o barco durante o salto. Quando errar, quem cuida é o próximo passo (se prender no casco).
        private void SetBoatCollision(bool enabled)
        {
            if (!enabled)
            {
                _boatColliders.Clear();

                if (_leapBoat)
                    _leapBoat.GetComponentsInChildren(_boatColliders);
            }

            foreach (Collider sharkCollider in _sharkColliders)
            {
                foreach (Collider boatCollider in _boatColliders)
                {
                    if (sharkCollider && boatCollider)
                        Physics.IgnoreCollision(sharkCollider, boatCollider, !enabled);
                }
            }

            if (enabled)
                _boatColliders.Clear();
        }

        private void SetState(State state)
        {
            CurrentState = state;
            _stateStartTime = Time.time;

            Bite.gameObject.SetActive(state == State.Bite);
            _swimming.TargetSubmersion = state == State.Circle ? CircleSubmersion : _defaultSubmersion;

            switch (state)
            {
                case State.Patrol:
                    _swimming.Speed = PatrolSpeed;
                    PickPatrolPoint();
                    break;
                case State.Chase:
                    _swimming.Speed = ChaseSpeed;
                    break;
                case State.Bite:
                    _swimming.Speed = BiteLungeSpeed;
                    _swimming.SetPlanarVelocity(FlatDirectionTo(Target.transform.position) * BiteLungeSpeed);
                    break;
                case State.Circle:
                    _swimming.Speed = CircleSpeed;
                    _circleDuration = Random.Range(CircleTime.x, CircleTime.y);
                    break;
                case State.Leap:
                    _leapBoat = Target.CurrentBoat;
                    _leapLaunched = false;
                    _leapHit = false;
                    _swimming.RequestJumpHop();
                    break;
            }

            _log.Log($"State: {state}");
        }

        private bool CanSeeTarget(float radius)
        {
            if (Target == null || Target.IsAboardBoat)
                return false;

            if (Target.Health && Target.Health.IsDead)
                return false;

            if (Target.ActiveMovement is not SwimmingMovement)
                return false;

            return FlatDistance(transform.position, Target.transform.position) < radius;
        }

        private bool CanSeeBoat(float radius)
        {
            if (Target == null || !Target.IsAboardBoat || Target.CurrentBoat == null)
                return false;

            if (Target.Health && Target.Health.IsDead)
                return false;

            return FlatDistance(transform.position, Target.CurrentBoat.transform.position) < radius;
        }

        private void PickPatrolPoint()
        {
            Vector2 offset = Random.insideUnitCircle * PatrolRadius;

            _patrolPoint = _home + new Vector3(offset.x, 0f, offset.y);
            _stateStartTime = Time.time;
        }

        // O SwimmingMovement sem ReferenceTransform recebe o input relativo à direção do próprio tubarão.
        private void SwimTowards(Vector3 point)
        {
            Vector3 direction = FlatDirectionTo(point);

            if (direction == Vector3.zero)
            {
                _swimming.Move(Vector2.zero);
                return;
            }

            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y, 0f)) * direction;

            _swimming.Move(new Vector2(local.x, local.z));
        }

        private Vector3 FlatDirectionTo(Vector3 point)
        {
            Vector3 direction = point - transform.position;
            direction.y = 0f;

            return direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(a, b);
        }

        private void CreateBite()
        {
            GameObject biteObject = new GameObject("Bite Hitbox", typeof(SphereCollider));
            biteObject.transform.SetParent(transform, false);
            biteObject.transform.localPosition = BiteOffset;

            SphereCollider biteCollider = biteObject.GetComponent<SphereCollider>();
            biteCollider.isTrigger = true;
            biteCollider.radius = BiteRadius;

            Bite = biteObject.AddComponent<DamageOnTouch>();
        }

        private void OnDied()
        {
            if (CurrentState == State.Leap)
                SetBoatCollision(true);

            SetState(State.Dead);

            _swimming.SetDrowning(true);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 home = Application.isPlaying ? _home : transform.position;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(home, PatrolRadius);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, DetectionRadius);

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.TransformPoint(BiteOffset), BiteRadius);
        }
    }
}
