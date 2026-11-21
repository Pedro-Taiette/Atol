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
    // Se errar o salto, se prende no casco e vai enchendo o barco de água até virar.
    // O movimento é todo feito pelo SwimmingMovement.
    [RequireComponent(typeof(SwimmingMovement))]
    [RequireComponent(typeof(HealthComponent))]
    public class SharkController : MonoBehaviour
    {
        public enum State { Patrol, Chase, Bite, Circle, Leap, Latch, Dead }

        [Header("Target")]
        public PlayerController Target;
        public float DetectionRadius = 35f;
        public float GiveUpRadius = 60f;

        [Header("Patrol")]
        public float PatrolRadius = 20f;
        public float PatrolSpeed = 2.5f;
        public float PatrolPointTime = 10f;

        [Header("Chase")]
        public float ChaseSpeed = 6f;

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
        [Tooltip("Velocidade pra alcançar o barco antes de começar a circular. Barco com bom vento escapa.")]
        public float BoatChaseSpeed = 7f;
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

        [Header("Boat Latch")]
        [Tooltip("Se errar o salto e cair até essa distância do barco, se prende no casco.")]
        public float LatchRange = 5f;
        public float LatchPull = 1.5f;
        public float LatchFloodRate = 10f;
        public float LatchMaxTime = 20f;

        [Header("Shake Off")]
        [Tooltip("Clicar no tubarão preso dá um golpe (usa o SelectableComponent).")]
        public SelectableComponent Selectable;
        public float StrikeDamage = 15f;
        public float StrikeStaminaCost = 15f;
        public int StrikesToRelease = 3;
        [Tooltip("Velejando acima dessa velocidade por ShakeOffTime segundos, ele se solta.")]
        public float ShakeOffSpeed = 4f;
        public float ShakeOffTime = 2f;

        [Header("Depth")]
        [Tooltip("Centro do corpo abaixo da superfície da onda, em metros.")]
        public float SwimDepth = 0.5f;
        [Tooltip("Mais raso enquanto circula o barco, pra barbatana aparecer.")]
        public float CircleDepth = 0.2f;
        [Tooltip("0 desliga. Cola o tubarão na altura da onda; só ligar se ele ficar saindo da água.")]
        public float DepthFollow = 0f;
        [Tooltip("Limite de subida/descida pra ele não ser catapultado por uma leitura ruim da onda.")]
        public float MaxVerticalSpeed = 4f;

        [Header("Stuck")]
        [Tooltip("Parado por esse tempo tentando nadar, ele desiste e procura outro ponto.")]
        public float StuckTime = 2f;

        public bool ShowLogs = false;

        public State CurrentState { get; private set; }

        private SwimmingMovement _swimming;
        private HealthComponent _health;
        private Rigidbody _rigidbody;
        private FrameworkLogger _log;

        private Vector3 _home;
        private Vector3 _patrolPoint;
        private float _stateStartTime;
        private float _nextBiteTime;

        private float _depth;
        private float _targetDepth;
        private float _stuckTimer;
        private float _circleDuration;
        private float _nextLeapTime;
        private BoatController _leapBoat;
        private bool _leapLaunched;
        private bool _leapHit;
        private readonly List<Collider> _sharkColliders = new();
        private readonly List<Collider> _boatColliders = new();

        private BoatFlooding _flooding;
        private Vector3 _latchLocalPosition;
        private Quaternion _latchLocalRotation;
        private int _strikeCount;
        private float _fastSailTime;

        private void Awake()
        {
            _log = new(this, showLogs: ShowLogs, prefixColor: Color.red);

            _home = transform.position;
            GetComponentsInChildren(_sharkColliders);

            _swimming = GetComponent<SwimmingMovement>();
            _health = GetComponent<HealthComponent>();
            _health.Died += OnDied;
            _rigidbody = GetComponent<Rigidbody>();

            _swimming.HopStarted += OnLeapLaunched;
            _swimming.HopLanded += OnLeapLanded;
            _depth = SwimDepth;
            _targetDepth = SwimDepth;

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

            try
            {
                if (Selectable == null && !TryGetComponent(out Selectable))
                    Selectable = gameObject.AddComponent<SelectableComponent>();

                Selectable.OnClickPressed.AddListener(OnStruck);
                Selectable.enabled = false;
            }
            catch (System.Exception e)
            {
                _log.Error($"Could not set up SelectableComponent, strikes disabled: {e.Message}");
                Selectable = null;
            }

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

            if (Selectable)
                Selectable.OnClickPressed.RemoveListener(OnStruck);
        }

        private void FixedUpdate()
        {
            UpdateStuck();

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
                case State.Latch:
                    UpdateLatch();
                    return;
                case State.Dead:
                    _swimming.Move(Vector2.zero);
                    break;
            }

            _swimming.ApplyMovement();

            if (CurrentState is not (State.Leap or State.Dead) && !_swimming.IsAirborne)
                FollowSurface();
        }

        // O SwimmingMovement limita a submersão e a velocidade vertical, então o tubarão ficava pra fora
        // quando a onda passava. Aqui ele é colado na altura da onda que o próprio motor já mede.
        private void FollowSurface()
        {
            if (DepthFollow <= 0f || _swimming.SurfaceSampleAge > 1f)
                return;

            _depth = Mathf.MoveTowards(_depth, _targetDepth, 0.5f * Time.fixedDeltaTime);

            float targetY = _swimming.TrackedSurfaceHeight - _depth;

            float correction = (targetY - _rigidbody.position.y) * DepthFollow;
            float waveVelocity = Mathf.Clamp(_swimming.TrackedSurfaceVelocity, -MaxVerticalSpeed, MaxVerticalSpeed);

            Vector3 velocity = _rigidbody.linearVelocity;
            velocity.y = Mathf.Clamp(correction + waveVelocity, -MaxVerticalSpeed, MaxVerticalSpeed);
            _rigidbody.linearVelocity = velocity;
        }

        // Encalhou na ilha ou travou em algo: muda de ideia em vez de ficar parado empurrando.
        private void UpdateStuck()
        {
            bool swimmingState = CurrentState is State.Patrol or State.Chase or State.Circle;

            if (!swimmingState || _rigidbody == null)
            {
                _stuckTimer = 0f;
                return;
            }

            Vector3 goal = _patrolPoint;

            if (CurrentState == State.Chase && Target)
                goal = Target.transform.position;
            else if (CurrentState == State.Circle && Target && Target.CurrentBoat)
                goal = Target.CurrentBoat.transform.position;

            if (FlatDistance(transform.position, goal) < BiteRange * 1.5f)
            {
                _stuckTimer = 0f;
                return;
            }

            Vector3 planarVelocity = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, Vector3.up);
            _stuckTimer = planarVelocity.magnitude < 0.3f ? _stuckTimer + Time.fixedDeltaTime : 0f;

            if (_stuckTimer < StuckTime)
                return;

            _stuckTimer = 0f;
            _log.Log($"Stuck while {CurrentState}, picking another point");

            SetState(State.Patrol);
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

            bool closeEnough = FlatDistance(transform.position, boatPosition) < CircleRadius * 1.5f;

            _swimming.Speed = closeEnough ? CircleSpeed : BoatChaseSpeed;
            SwimTowards(closeEnough ? circlePoint : boatPosition);

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
            if (CurrentState != State.Leap || !_leapLaunched)
                return;

            bool nearBoat = _leapBoat && FlatDistance(transform.position, _leapBoat.transform.position) < LatchRange;

            if (!_leapHit && nearBoat && _rigidbody)
                SetState(State.Latch);
            else
                EndLeap();
        }

        private void StartLatch()
        {
            _flooding = _leapBoat.GetComponent<BoatFlooding>();

            if (_flooding == null)
                _flooding = _leapBoat.gameObject.AddComponent<BoatFlooding>();

            // Gruda na lateral do casco mais próxima, virado pro barco.
            Vector3 hullPoint = _leapBoat.Body.ClosestPointOnBounds(transform.position);
            Vector3 outward = FlatDirectionFrom(_leapBoat.transform.position, hullPoint);
            Vector3 position = hullPoint + outward * 0.3f;
            position.y = Mathf.Min(transform.position.y, hullPoint.y);

            _latchLocalPosition = _leapBoat.transform.InverseTransformPoint(position);
            _latchLocalRotation = Quaternion.Inverse(_leapBoat.transform.rotation) * Quaternion.LookRotation(-outward);

            _rigidbody.isKinematic = true;

            _strikeCount = 0;
            _fastSailTime = 0f;

            if (Selectable)
                Selectable.enabled = true;
        }

        private void UpdateLatch()
        {
            bool playerLeft = Target == null || !Target.IsAboardBoat;

            if (_leapBoat == null || playerLeft || _flooding.IsCapsizing || Time.time - _stateStartTime > LatchMaxTime)
            {
                ReleaseLatch();
                return;
            }

            _fastSailTime = _leapBoat.PlanarSpeed > ShakeOffSpeed ? _fastSailTime + Time.fixedDeltaTime : 0f;

            if (_fastSailTime >= ShakeOffTime)
            {
                _log.Log("Shaken off by sailing fast");
                ReleaseLatch();
                return;
            }

            Transform boat = _leapBoat.transform;

            _rigidbody.MovePosition(boat.TransformPoint(_latchLocalPosition));
            _rigidbody.MoveRotation(boat.rotation * _latchLocalRotation);

            _leapBoat.Body.AddForceAtPosition(Vector3.down * LatchPull, transform.position, ForceMode.Acceleration);
            _flooding.AddWater(LatchFloodRate * Time.fixedDeltaTime, transform.position);
        }

        private void OnStruck()
        {
            if (CurrentState != State.Latch)
                return;

            if (Target && Target.Stamina && !Target.Stamina.TryConsume(StrikeStaminaCost))
                return;

            _strikeCount++;
            _health.TakeDamage(StrikeDamage);

            _log.Log($"Struck {_strikeCount}/{StrikesToRelease}");

            if (_strikeCount >= StrikesToRelease && CurrentState == State.Latch)
                ReleaseLatch();
        }

        private void ReleaseLatch()
        {
            if (Selectable)
                Selectable.enabled = false;
            _rigidbody.isKinematic = false;
            _rigidbody.linearVelocity = Vector3.zero;
            _swimming.SetPlanarVelocity(Vector3.zero);
            _swimming.ResetVerticalVelocity();

            if (_leapBoat)
                transform.position += FlatDirectionFrom(_leapBoat.transform.position, transform.position) * 1.5f;

            EndLeap();
        }

        private void EndLeap()
        {
            SetBoatCollision(true);

            // Se caiu em terra firme o pulo nunca "aterrissa" na água; religar o motor zera esse estado.
            if (_swimming.IsAirborne)
            {
                _swimming.enabled = false;
                _swimming.enabled = true;
            }

            _nextLeapTime = Time.time + LeapCooldown;
            SetState(State.Patrol);
        }

        // O tubarão atravessa o barco durante o salto e enquanto está preso no casco.
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
            _targetDepth = state == State.Circle ? CircleDepth : SwimDepth;

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
                case State.Latch:
                    StartLatch();
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
            // Tenta algumas vezes achar um ponto que não seja em cima da ilha. Se não achar, vai assim mesmo.
            for (int i = 0; i < 8; i++)
            {
                Vector2 offset = Random.insideUnitCircle * PatrolRadius;
                _patrolPoint = _home + new Vector3(offset.x, 0f, offset.y);

                if (IsOverWater(_patrolPoint))
                    break;
            }

            _stateStartTime = Time.time;
        }

        private bool IsOverWater(Vector3 point)
        {
            Vector3 origin = point + Vector3.up * 15f;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                return true;

            if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<SpectralOceanFFT>())
                return true;

            // Chão perto do nível do mar = praia ou ilha.
            return hit.point.y < _home.y - 2f;
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

        private static Vector3 FlatDirectionFrom(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            direction.y = 0f;

            return direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.forward;
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
            if (CurrentState == State.Latch)
                ReleaseLatch();
            else if (CurrentState == State.Leap)
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
