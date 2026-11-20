using Assets._Project.Features.Combat;
using Assets._Project.Features.StatusViewModels;
using Assets._Project.Framework.Logging;
using UnityEngine;

namespace Assets._Project.Features.Shark
{
    // IA do tubarão: patrulha em volta do ponto inicial, persegue o player quando ele está nadando
    // e dá um bote com a hitbox de mordida. O movimento é todo feito pelo SwimmingMovement.
    [RequireComponent(typeof(SwimmingMovement))]
    [RequireComponent(typeof(HealthComponent))]
    public class SharkController : MonoBehaviour
    {
        public enum State { Patrol, Chase, Bite, Dead }

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

        public bool ShowLogs = false;

        public State CurrentState { get; private set; }

        private SwimmingMovement _swimming;
        private HealthComponent _health;
        private FrameworkLogger _log;

        private Vector3 _home;
        private Vector3 _patrolPoint;
        private float _stateStartTime;
        private float _nextBiteTime;

        private void Awake()
        {
            _log = new(this, showLogs: ShowLogs, prefixColor: Color.red);

            _swimming = GetComponent<SwimmingMovement>();
            _health = GetComponent<HealthComponent>();
            _health.Died += OnDied;

            if (Bite == null)
                CreateBite();

            Bite.Damage = BiteDamage;
            Bite.RepeatInterval = 0f;
            Bite.gameObject.SetActive(false);

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

        private void SetState(State state)
        {
            CurrentState = state;
            _stateStartTime = Time.time;

            Bite.gameObject.SetActive(state == State.Bite);

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
