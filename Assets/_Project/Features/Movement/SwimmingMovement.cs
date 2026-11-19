using Assets._Project.Features.Movement;
using Assets._Project.Features.StatusViewModels;
using Assets._Project.Features.WaterEffects;
using UnityEngine;

/// <summary>
/// Reusable actor-agnostic swimming motor. Player and NPC controllers can drive it
/// through IMovement while AI can additionally control buoyancy/drowning through
/// ISwimmingBuoyancyControl. Supports CharacterController and Rigidbody actors.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(StaminaComponent))]
public sealed class SwimmingMovement : MonoBehaviour, IMovement, IPlanarVelocityMovement, ISwimmingBuoyancyControl
{
    [Header("Actor")]
    [Tooltip("Optional root moved by this motor. Defaults to the CharacterController, Rigidbody, or this transform.")]
    public Transform MovementRoot;
    [Tooltip("Preferred for player characters. Leave empty for Rigidbody-driven NPCs.")]
    public CharacterController Controller;
    [Tooltip("Optional alternative for NPCs that use a Rigidbody instead of a CharacterController.")]
    public Rigidbody Body;
    [Tooltip("Optional collider used to measure body height and center for the buoyancy target.")]
    public Collider BodyCollider;

    [Header("Input Reference")]
    [Tooltip("Camera for player swimming. Leave empty for NPCs to move relative to their own facing direction.")]
    public Transform ReferenceTransform;
    [Min(0f)] public float Speed = 3.8f;
    [Min(0f)] public float Acceleration = 8f;
    [Min(0f)] public float Deceleration = 5f;
    [Min(0.01f)] public float TurnSmoothTime = 0.12f;

    [Header("Surface Buoyancy")]
    public SpectralOceanFFT Ocean;
    [Tooltip("Fraction of the actor's measured body height below the waterline. 0.55 keeps the actor near the surface with the upper body above water.")]
    [Range(0.1f, 0.9f)] public float TargetSubmersion = 0.55f;
    [Tooltip("Fallback body height when no CharacterController or collider is available.")]
    [Min(0.1f)] public float AssumedBodyHeight = 1.8f;
    [Tooltip("Fallback offset from actor root to body center.")]
    [Min(0f)] public float AssumedBodyCenterOffset = 0.9f;
    [Tooltip("Strength of the spring pulling the body toward its surface-floating height.")]
    [Min(0f)] public float SurfaceSpring = 6.5f;
    [Tooltip("Damps vertical motion without making the actor feel glued to the water.")]
    [Min(0f)] public float VerticalDamping = 8f;
    [Tooltip("Scales the measured vertical speed of the water. The swimmer follows both rising and falling waves.")]
    [Min(0f)] public float WaveLiftMultiplier = 0.3f;
    [Tooltip("How strongly the swimmer matches the water surface vertical velocity in both directions. 0 ignores wave motion; 1 fully follows it before WaveLiftMultiplier is applied.")]
    [Range(0f, 2f)] public float WaveVelocityFollow = 0.2f;
    [Tooltip("Limits noisy surface-height changes when estimating wave vertical velocity.")]
    [Min(0.1f)] public float MaximumWaveVerticalSpeed = 1.0f;
    [Tooltip("Extra upward acceleration available to wave buoyancy input and jump/swim-up input.")]
    [Min(0f)] public float ExtraBuoyancyAcceleration = 8f;
    [Tooltip("Downward acceleration available to drowning/submerge input.")]
    [Min(0f)] public float DrowningPullAcceleration = 16f;
    [Tooltip("Remaining surface-spring strength while fully drowned. Lower values let abilities pull actors deeper underwater.")]
    [Range(0f, 1f)] public float DrowningBuoyancyRetention = 0.18f;
    [Tooltip("Fraction of wave-follow force retained while drowning pull is active.")]
    [Range(0f, 1f)] public float DrowningWaveResponse = 0.3f;
    [Min(0f)] public float MaximumUpwardAcceleration = 8f;
    [Min(0f)] public float MaximumDownwardAcceleration = 8f;
    [Min(0.1f)] public float MaximumUpwardSpeed = 1.35f;
    [Min(0.1f)] public float MaximumDownwardSpeed = 1.35f;

    [Header("Wave Ejection Detection")]
    [Tooltip("The body centre must rise this far above the water surface before a wave ejection is recognized.")]
    [Min(0f)] public float WaveEjectionHeight = 0.2f;
    [Tooltip("Once wave-ejected, clear the state when the body centre returns this close to the water surface.")]
    [Min(0f)] public float WaveReentryMargin = 0.05f;
    [Tooltip("Brief confirmation time filters out one-frame surface-sampling noise.")]
    [Min(0f)] public float WaveEjectionConfirmationTime = 0.08f;

    [Header("Swimming Vertical Input")]
    [Tooltip("Acceleration applied while the jump/up input is held.")]
    [Min(0f)] public float VerticalSwimAcceleration = 5f;

    [Header("Wave Drift (side-to-side wave motion)")]
    [Tooltip("The swimmer is carried by the horizontal (orbital) motion of the waves while not fully submerged. It is zero-mean: after a full wave cycle the swimmer is back where it started.")]
    public bool EnableWaveDrift = true;
    [Tooltip("Fraction of the water particle's horizontal displacement the swimmer follows.")]
    [Range(0f, 1.5f)] public float WaveDriftFollow = 0.8f;
    [Tooltip("Response frequency (rad/s) of the drift filter. Waves are 0.4-1.6 rad/s.")]
    [Range(0.5f, 8f)] public float WaveDriftResponse = 3.2f;
    [Range(0.3f, 2f)] public float WaveDriftDamping = 0.9f;
    [Tooltip("Largest horizontal excursion from waves, metres.")]
    [Min(0.1f)] public float WaveDriftMaxOffset = 3f;
    [Tooltip("Fraction of body height under the surface at which wave drift starts fading (full drift below it).")]
    [Range(0f, 1f)] public float DriftFadeStart = 0.7f;
    [Tooltip("Fraction under the surface at which drift is zero (fully submerged, e.g. pulled down by a shark).")]
    [Range(0f, 1f)] public float DriftFadeEnd = 1f;

    [Header("Foam")]
    [Tooltip("Leave soft foam on the water surface while swimming and on hop splashes.")]
    public bool EmitFoam = true;
    [Tooltip("Planar speed (m/s) at which swim foam is at full intensity.")]
    [Min(0.1f)] public float FoamFullSpeed = 2.8f;
    [Tooltip("Swim foam stamp radius (m) at low / full speed. Small: a swimmer pushes little water.")]
    public Vector2 FoamRadius = new Vector2(0.26f, 0.62f);
    [Tooltip("Foam added per second at full speed.")]
    [Min(0f)] public float FoamRate = 22f;
    [Tooltip("Foam stamps per second (fixed rate, independent of physics steps).")]
    [Range(5f, 60f)] public float FoamEmitHz = 18f;
    [Tooltip("Metres ahead of the body where the chest pushes the water; foam is stamped there.")]
    [Min(0f)] public float FoamLeadOffset = 0.35f;
    [Tooltip("When driven by PlayerController, continuous player foam is emitted by PlayerWaterContactFoam so shallow-water walking and swimming share one reliable trail owner. NPC swimmers leave this disabled.")]
    [HideInInspector] public bool UseExternalPlayerFoam;
    [Tooltip("Seconds between visible circular surface ripples for NPC swimmers. Player uses PlayerWaterContactFoam instead.")]
    [Min(0.25f)] public float RippleInterval = 2.1f;

    [Header("Jump Hop (out of the water)")]
    [Tooltip("Jump becomes a small hop out of the water that costs stamina. Off = legacy hold-to-swim-up.")]
    public bool EnableJumpHop = true;
    public StaminaComponent Stamina;
    [Min(0f)] public float JumpStaminaCost = 25f;
    [Tooltip("Wind-up between pressing jump and leaving the water (the swimmer kicks down first).")]
    [Min(0f)] public float JumpDelay = 0.18f;
    [Tooltip("Seconds after a hop before another can start.")]
    [Min(0f)] public float JumpCooldown = 1.2f;
    [Tooltip("Upward speed at take-off, m/s. 3.2 m/s is roughly a 0.5 m hop.")]
    [Min(0.1f)] public float JumpVelocity = 3.4f;
    [Tooltip("Extra planar speed added in the travel direction at take-off.")]
    [Min(0f)] public float JumpSpeedBoost = 1.6f;
    [Tooltip("Fraction of horizontal control available in the air (keeps the speed boost).")]
    [Range(0f, 1f)] public float JumpAirControl = 0.25f;
    [Min(0.1f)] public float JumpGravity = 9.81f;
    [Tooltip("How far the body sinks during the wind-up, metres.")]
    [Min(0f)] public float JumpWindupDip = 0.2f;
    [Tooltip("A jump is only allowed while the body centre is within this height error of its floating height (not when pulled down by a shark).")]
    [Min(0.05f)] public float JumpMaxDepthError = 0.45f;
    [Tooltip("Seconds after landing during which the player is not switched back to grounded movement.")]
    [Min(0f)] public float JumpLandingGrace = 0.5f;

    [Header("Surface Sampling")]
    [Min(0.02f)] public float SurfaceSampleInterval = 0.10f;
    [Min(0.05f)] public float MaximumSurfaceSampleAge = 0.45f;
    [Range(0f, 1f)] public float WaveVelocitySmoothing = 0.3f;

    [Header("Stable Surface Following")]
    [Tooltip("Smooths the local water height before it becomes the swimmer's buoyancy target.")]
    [Range(0.04f, 0.5f)] public float SurfaceHeightSmoothTime = 0.16f;
    [Tooltip("Maximum rate (m/s) at which the swimmer's surface target may move. Prevents a single noisy sample from launching the character.")]
    [Min(0.1f)] public float MaximumSurfaceFollowSpeed = 2.0f;
    [Tooltip("Hold the last valid wave sample briefly if a GPU readback is delayed; then blend gently toward mean sea level.")]
    [Min(0.1f)] public float SurfaceSampleHoldTime = 0.9f;
    [Tooltip("Distance from the capsule centre to the four surrounding water probes, as a multiple of the capsule radius.")]
    [Range(0.2f, 1.2f)] public float SurfaceProbeSpread = 0.7f;
    [Tooltip("Limits how far any outer probe may differ from the centre height when averaging a steep crest or trough.")]
    [Min(0.05f)] public float MaximumProbeHeightDifference = 0.28f;

    private Vector2 inputDirection;
    private float verticalInput;
    private bool jumpHeld;
    private bool diveHeld;
    private float buoyancyInput;
    private float verticalVelocity;
    private float turnSmoothVelocity;
    private Vector3 horizontalVelocity;

    private float sampledSurfaceHeight;
    private float sampledSurfaceTime = -1000f;
    private float smoothedSurfaceHeight;
    private float surfaceHeightSmoothVelocity;
    private float filteredSurfaceVerticalVelocity;
    private bool hasSmoothedSurfaceHeight;
    private float nextSampleRequestTime;
    private bool hasSurfaceSample;
    private bool sampleRequestPending;
    private int sampleGeneration;

    private Vector2 sampledLattice;
    private Vector2 driftOffset;
    private Vector2 driftVelocity;
    private float driftExposure = 1f;
    private float jumpWindupTimer = -1f;
    private float nextJumpTime;
    private bool hopAirborne;
    private float hopDescendingDuration;
    private bool waveAirborne;
    private float waveEjectionTimer;
    private float hopLandedTime = -1000f;
    private WaterContactFoam foam;
    private Vector3 lastFoamPoint;
    private bool hasFoamPoint;
    private float rippleTimer;
    private float foamAccumulator;
    private float wakeDistanceAccumulator;

    private bool bodyGravityWasEnabled;
    private bool bodyGravityCached;

    private Transform ActorTransform
    {
        get
        {
            if (MovementRoot != null) return MovementRoot;
            if (Controller != null) return Controller.transform;
            if (Body != null) return Body.transform;
            return transform;
        }
    }

    public Vector3 PlanarVelocity => Vector3.ProjectOnPlane(horizontalVelocity, Vector3.up);
    public float VerticalVelocity => verticalVelocity;
    public float CurrentBuoyancyInput => buoyancyInput;
    /// <summary>True during the jump wind-up, the hop itself and a short landing grace.</summary>
    public bool IsHopping => jumpWindupTimer >= 0f || hopAirborne ||
                             Time.time - hopLandedTime < JumpLandingGrace;
    /// <summary>True only while the jump wind-up or airborne hop is active; excludes post-landing grace.</summary>
    public bool IsHopInProgress => jumpWindupTimer >= 0f || hopAirborne;
    /// <summary>True while performing the deliberate swimming hop.</summary>
    public bool IsAirborne => hopAirborne;
    /// <summary>True when a strong wave has lifted the body clear of the waterline.</summary>
    public bool IsWaveAirborne => waveAirborne && !hopAirborne;
    /// <summary>0 = fully submerged (no wave push), 1 = fully exposed.</summary>
    public float WaveDriftExposure => driftExposure;
    public Vector3 WaveDriftOffset => new Vector3(driftOffset.x, 0f, driftOffset.y);
    /// <summary>Raised when the hop starts leaving the water.</summary>
    public event System.Action HopStarted;
    /// <summary>Raised when the swimmer re-enters the water after a hop. Argument = impact speed.</summary>
    public event System.Action<float> HopLanded;
    /// <summary>Raised when a jump is refused (cooldown, stamina, too deep). Argument = reason.</summary>
    public event System.Action<string> HopRefused;

    private void Awake()
    {
        ResolveReferences();
        CacheRigidbodyGravity();
    }

    private void OnEnable()
    {
        sampleGeneration++;
        sampleRequestPending = false;
        if (!hasSurfaceSample)
            hasSmoothedSurfaceHeight = false;
        SetRigidbodyGravityControlled(true);
    }

    private void OnDisable()
    {
        sampleGeneration++;
        sampleRequestPending = false;
        inputDirection = Vector2.zero;
        verticalInput = 0f;
        jumpHeld = false;
        diveHeld = false;
        buoyancyInput = 0f;
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        driftOffset = Vector2.zero;
        driftVelocity = Vector2.zero;
        jumpWindupTimer = -1f;
        hopAirborne = false;
        hopDescendingDuration = 0f;
        waveAirborne = false;
        waveEjectionTimer = 0f;
        hasSmoothedSurfaceHeight = false;
        hasSurfaceSample = false;
        sampledSurfaceTime = -1000f;
        sampledLattice = Vector2.zero;
        surfaceHeightSmoothVelocity = 0f;
        filteredSurfaceVerticalVelocity = 0f;
        SetRigidbodyGravityControlled(false);
    }

    private void OnValidate()
    {
        Speed = Mathf.Max(0f, Speed);
        Acceleration = Mathf.Max(0f, Acceleration);
        Deceleration = Mathf.Max(0f, Deceleration);
        TurnSmoothTime = Mathf.Max(0.01f, TurnSmoothTime);
        TargetSubmersion = Mathf.Clamp(TargetSubmersion, 0.1f, 0.9f);
        WaveVelocityFollow = Mathf.Clamp(WaveVelocityFollow, 0f, 2f);
        WaveVelocitySmoothing = Mathf.Clamp01(WaveVelocitySmoothing);
        DrowningBuoyancyRetention = Mathf.Clamp01(DrowningBuoyancyRetention);
        DrowningWaveResponse = Mathf.Clamp01(DrowningWaveResponse);
        AssumedBodyHeight = Mathf.Max(0.1f, AssumedBodyHeight);
        AssumedBodyCenterOffset = Mathf.Max(0f, AssumedBodyCenterOffset);
        SurfaceSpring = Mathf.Max(0f, SurfaceSpring);
        VerticalDamping = Mathf.Max(0f, VerticalDamping);
        WaveLiftMultiplier = Mathf.Max(0f, WaveLiftMultiplier);
        MaximumWaveVerticalSpeed = Mathf.Max(0.1f, MaximumWaveVerticalSpeed);
        ExtraBuoyancyAcceleration = Mathf.Max(0f, ExtraBuoyancyAcceleration);
        DrowningPullAcceleration = Mathf.Max(0f, DrowningPullAcceleration);
        MaximumUpwardAcceleration = Mathf.Max(0f, MaximumUpwardAcceleration);
        MaximumDownwardAcceleration = Mathf.Max(0f, MaximumDownwardAcceleration);
        MaximumUpwardSpeed = Mathf.Max(0.1f, MaximumUpwardSpeed);
        MaximumDownwardSpeed = Mathf.Max(0.1f, MaximumDownwardSpeed);
        VerticalSwimAcceleration = Mathf.Max(0f, VerticalSwimAcceleration);
        SurfaceSampleInterval = Mathf.Max(0.02f, SurfaceSampleInterval);
        MaximumSurfaceSampleAge = Mathf.Max(0.05f, MaximumSurfaceSampleAge);
        SurfaceHeightSmoothTime = Mathf.Clamp(SurfaceHeightSmoothTime, 0.04f, 0.5f);
        MaximumSurfaceFollowSpeed = Mathf.Max(0.1f, MaximumSurfaceFollowSpeed);
        SurfaceSampleHoldTime = Mathf.Max(MaximumSurfaceSampleAge, SurfaceSampleHoldTime);
        SurfaceProbeSpread = Mathf.Clamp(SurfaceProbeSpread, 0.2f, 1.2f);
        MaximumProbeHeightDifference = Mathf.Max(0.05f, MaximumProbeHeightDifference);
        DriftFadeEnd = Mathf.Max(DriftFadeStart + 0.01f, DriftFadeEnd);
        JumpStaminaCost = Mathf.Max(0f, JumpStaminaCost);
        WaveEjectionHeight = Mathf.Max(0f, WaveEjectionHeight);
        WaveReentryMargin = Mathf.Max(0f, WaveReentryMargin);
        WaveEjectionConfirmationTime = Mathf.Max(0f, WaveEjectionConfirmationTime);
    }

    public void Move(Vector2 direction)
    {
        inputDirection = Vector2.ClampMagnitude(direction, 1f);
    }

    /// <summary>Jump/up swims upward while held (PlayerController-compatible).</summary>
    public void Jump(bool released)
    {
        if (EnableJumpHop)
        {
            if (!released)
                RequestJumpHop();
            return;
        }

        jumpHeld = !released;
        RefreshVerticalInput();
    }

    /// <summary>Set -1 to pull down, 0 for neutral, or +1 to swim upward.</summary>
    public void SetVerticalInput(float value)
    {
        jumpHeld = value > 0f;
        diveHeld = value < 0f;
        RefreshVerticalInput();
    }

    /// <summary>Optional downward control. Positive held input pulls the swimmer down.</summary>
    public void SetDiveInput(bool held)
    {
        diveHeld = held;
        RefreshVerticalInput();
    }

    /// <summary>Starts a hop if off cooldown, near the surface, not drowning and stamina allows.</summary>
    public bool RequestJumpHop()
    {
        ResolveReferences();
        if (!isActiveAndEnabled || jumpWindupTimer >= 0f || hopAirborne)
            return false;
        if (Time.time < nextJumpTime) { HopRefused?.Invoke("cooldown"); return false; }
        if (buoyancyInput < -0.01f) { HopRefused?.Invoke("drowning"); return false; }

        Transform actor = ActorTransform;
        float targetCenterY = GetCurrentSurfaceHeight(actor) + (0.5f - TargetSubmersion) * GetBodyHeight();
        if (GetBodyCenterY(actor) < targetCenterY - JumpMaxDepthError)
        {
            HopRefused?.Invoke("too deep");
            return false;
        }
        if (Stamina != null && !Stamina.TryConsume(JumpStaminaCost))
        {
            HopRefused?.Invoke("stamina");
            return false;
        }

        jumpWindupTimer = 0f;
        nextJumpTime = Time.time + JumpDelay + JumpCooldown;
        return true;
    }

    private void RefreshVerticalInput()
    {
        verticalInput = diveHeld ? -1f : jumpHeld ? 1f : 0f;
    }

    /// <summary>
    /// Sets buoyancy independently of swimming input: -1 actively pulls down,
    /// 0 uses natural buoyancy, and +1 adds lift. Designed for NPC/ability control.
    /// </summary>
    public void SetBuoyancyInput(float value)
    {
        buoyancyInput = Mathf.Clamp(value, -1f, 1f);
    }

    /// <summary>Convenience API for drowning/grab effects that pull an actor underwater.</summary>
    public void SetDrowning(bool drowning)
    {
        buoyancyInput = drowning ? -1f : 0f;
    }

    public void SetTargetSubmersion(float fraction)
    {
        TargetSubmersion = Mathf.Clamp(fraction, 0.1f, 0.9f);
    }

    public void ApplyMovement()
    {
        ResolveReferences();
        if (Controller != null && !Controller.enabled)
            return;
        if (Controller == null && Body == null && ActorTransform == null)
            return;

        RequestSurfaceSampleIfNeeded();

        float dt = Time.fixedDeltaTime;
        if (dt <= 0f)
            return;

        UpdateFilteredSurfaceTarget(dt);
        Transform actor = ActorTransform;
        Vector3 desiredDirection = GetCameraRelativeDirection(inputDirection, actor);
        Vector3 targetHorizontalVelocity = desiredDirection * Speed;
        float horizontalRate = desiredDirection.sqrMagnitude > 0.0001f ? Acceleration : Deceleration;
        if (hopAirborne)
            horizontalRate *= JumpAirControl;
        horizontalVelocity = Vector3.MoveTowards(
            Vector3.ProjectOnPlane(horizontalVelocity, Vector3.up),
            targetHorizontalVelocity,
            horizontalRate * dt);

        if (desiredDirection.sqrMagnitude > 0.0001f)
        {
            float targetAngle = Mathf.Atan2(desiredDirection.x, desiredDirection.z) * Mathf.Rad2Deg;
            float angle = Mathf.SmoothDampAngle(
                actor.eulerAngles.y, targetAngle, ref turnSmoothVelocity, TurnSmoothTime);
            actor.rotation = Quaternion.Euler(0f, angle, 0f);
        }

        float surfaceHeight = GetCurrentSurfaceHeight(actor);
        float bodyHeight = GetBodyHeight();
        float targetCenterY = surfaceHeight + (0.5f - TargetSubmersion) * bodyHeight;

        UpdateWaveAirborneState(surfaceHeight, actor, dt);

        if (jumpWindupTimer >= 0f)
        {
            jumpWindupTimer += dt;
            float progress = JumpDelay > 0.0001f ? Mathf.Clamp01(jumpWindupTimer / JumpDelay) : 1f;
            targetCenterY -= JumpWindupDip * Mathf.Sin(progress * Mathf.PI * 0.5f);
            if (progress >= 1f)
                LaunchHop(desiredDirection, actor);
        }

        float heightError = targetCenterY - GetBodyCenterY(actor);

        if (hopAirborne)
        {
            verticalVelocity = Mathf.Max(verticalVelocity - JumpGravity * dt, -MaximumDownwardSpeed * 3f);
            if (verticalVelocity <= 0f)
                hopDescendingDuration += dt;

            // A hop can finish by returning to the water's floating target OR by
            // landing on solid terrain above that target. Treat confirmed downward
            // ground contact as a valid landing so hopAirborne cannot remain stuck
            // true on a high shoreline.
            bool descending = verticalVelocity <= 0f;
            bool touchedSolidGround = descending && Controller != null &&
                                      Controller.enabled && Controller.isGrounded;
            bool returnedToWater = descending && heightError >= 0f;
            if (touchedSolidGround || returnedToWater)
            {
                hopAirborne = false;
                hopLandedTime = Time.time;
                float impact = Mathf.Max(0f, -verticalVelocity);
                float fallDuration = hopDescendingDuration;
                hopDescendingDuration = 0f;
                verticalVelocity = touchedSolidGround ? 0f : Mathf.Max(verticalVelocity, -MaximumDownwardSpeed);

                // Only spawn the splash when the surface intersects the capsule.
                // Landing on dry ground above the waterline is a land impact, not a
                // water-entry event.
                float bodyBottom = GetBodyCenterY(actor) - bodyHeight * 0.5f;
                bool waterTouchesBody = surfaceHeight >= bodyBottom;
                if (waterTouchesBody)
                    EmitHopSplash(Mathf.Clamp01(0.35f + impact / 8f), fallDuration, true);
                HopLanded?.Invoke(impact);
            }
        }

        if (!hopAirborne)
        {
        float drowningAmount = Mathf.Max(0f, -buoyancyInput);
        float springRetention = Mathf.Lerp(1f, DrowningBuoyancyRetention, drowningAmount);
        float springAcceleration = heightError * SurfaceSpring * springRetention;

        // Treat the ocean surface as a moving reference frame. Damping is applied
        // against the sampled wave's vertical velocity, not against world-zero.
        // This makes the swimmer travel DOWN with a receding trough just as readily
        // as it rises with an approaching crest, preventing the body being left
        // suspended above a falling wave.
        float waveResponse = Mathf.Lerp(1f, DrowningWaveResponse, drowningAmount);
        float freshWaveVelocity = GetCurrentSurfaceVerticalVelocity();
        float targetWaveVelocity = freshWaveVelocity * WaveVelocityFollow
                                   * WaveLiftMultiplier * waveResponse;
        float dampingAcceleration = (targetWaveVelocity - verticalVelocity) * VerticalDamping;
        float swimAcceleration = verticalInput * VerticalSwimAcceleration;
        float buoyancyControlAcceleration = buoyancyInput >= 0f
            ? buoyancyInput * ExtraBuoyancyAcceleration
            : buoyancyInput * DrowningPullAcceleration;

        float verticalAcceleration = springAcceleration + dampingAcceleration
                                     + swimAcceleration + buoyancyControlAcceleration;
        verticalAcceleration = Mathf.Clamp(
            verticalAcceleration, -MaximumDownwardAcceleration, MaximumUpwardAcceleration);
        verticalVelocity = Mathf.Clamp(
            verticalVelocity + verticalAcceleration * dt,
            -MaximumDownwardSpeed,
            MaximumUpwardSpeed);
        }

        UpdateContactFoam(dt, actor, surfaceHeight);

        Vector2 driftDelta = UpdateWaveDrift(dt, surfaceHeight, bodyHeight, actor);
        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity
                           + new Vector3(driftDelta.x, 0f, driftDelta.y) / dt;
        if (Controller != null)
        {
            Controller.Move(velocity * dt);
            // CharacterController.isGrounded is only valid after Move. Clamp the
            // motor's downward velocity and clear wave-ejection state on real solid
            // contact so the player controller can reliably hand off to grounded mode.
            if (Controller.isGrounded)
            {
                if (verticalVelocity < 0f)
                    verticalVelocity = 0f;
                waveAirborne = false;
                waveEjectionTimer = 0f;
            }
        }
        else if (Body != null)
        {
            if (Body.isKinematic)
                Body.MovePosition(Body.position + velocity * dt);
            else
                Body.linearVelocity = velocity;
        }
        else
        {
            actor.position += velocity * dt;
        }
    }

    private void UpdateContactFoam(float dt, Transform actor, float surfaceHeight)
    {
        if (!EmitFoam || actor == null)
            return;
        if (foam == null)
        {
            foam = GetComponent<WaterContactFoam>();
            if (foam == null)
                foam = gameObject.AddComponent<WaterContactFoam>();
        }

        Vector3 point = actor.position;
        point.y = surfaceHeight;

        // Contact foam belongs to the visible waterline, not merely to the fact that
        // the swimming movement motor is active. Suppress it when the swimmer is
        // completely clear of the surface or deeply submerged.
        float bodyHeight = GetBodyHeight();
        float bodyBottom = GetBodyCenterY(actor) - bodyHeight * 0.5f;
        float submergedFraction = Mathf.Clamp01((surfaceHeight - bodyBottom) / Mathf.Max(0.1f, bodyHeight));
        float waterlineContact = Smooth01Between(0.02f, 0.14f, submergedFraction)
                               * (1f - Smooth01Between(0.78f, 0.94f, submergedFraction));
        if (waterlineContact <= 0.001f)
        {
            hasFoamPoint = false;
            foamAccumulator = 0f;
            rippleTimer = 0f;
            return;
        }

        float speed = horizontalVelocity.magnitude;
        float speed01 = hopAirborne ? 0f : Mathf.Clamp01(speed / Mathf.Max(0.1f, FoamFullSpeed));
        if (!UseExternalPlayerFoam)
        {
            // NPC swimmers own their own trail. PlayerController enables the external
            // player-level emitter so swimming and wading share the same tracked trail.
            foamAccumulator += dt;
            float emitInterval = 1f / Mathf.Max(5f, FoamEmitHz);
            if (foamAccumulator >= emitInterval)
            {
                float step = foamAccumulator;
                foamAccumulator = 0f;
                float curve = hopAirborne ? 0f :
                    Mathf.Lerp(0.18f, 1f, Smooth01Between(0.04f, 0.82f, speed01)) * waterlineContact;
                if (curve > 0.001f)
                {
                    Vector3 dir = horizontalVelocity.sqrMagnitude > 0.01f ? horizontalVelocity.normalized : actor.forward;
                    Vector3 lead = point + dir * FoamLeadOffset;
                    foam.EmitTrail(hasFoamPoint ? lastFoamPoint : lead, lead, curve, step,
                        FoamRadius.x, FoamRadius.y, FoamRate);
                    lastFoamPoint = lead;
                    hasFoamPoint = true;
                }
                else
                {
                    hasFoamPoint = false;
                }
            }
        }
        else
        {
            hasFoamPoint = false;
            foamAccumulator = 0f;
        }

        // NPC swimmers retain a local directional wake here. PlayerController sets
        // UseExternalPlayerFoam so PlayerWaterContactFoam emits the player's unified
        // multi-point foam + Kelvin wake for both swimming and wading.
        if (!UseExternalPlayerFoam && !hopAirborne && speed > 0.35f)
        {
            wakeDistanceAccumulator += speed * dt;
            float spacing = Mathf.Clamp(speed * 0.65f, 1.5f, 3.8f);
            Vector3 direction = horizontalVelocity.normalized;
            Vector3 wakePoint = point - direction * Mathf.Max(0.12f, FoamLeadOffset * 0.55f);
            float wakeRadius = Mathf.Lerp(0.38f, 0.58f, speed01);
            float wakeStrength = Mathf.Lerp(0.18f, 0.34f, speed01);
            if (wakeDistanceAccumulator >= spacing)
            {
                float overflow = wakeDistanceAccumulator % spacing;
                wakeDistanceAccumulator = overflow;
                WaterWakeField.EmitKelvinWake(
                    wakePoint - direction * overflow,
                    direction,
                    wakeRadius,
                    wakeStrength,
                    8f,
                    19.47f,
                    speed);
            }
        }
        else
        {
            wakeDistanceAccumulator = 0f;
        }

        // Restore visible analytic ripple sources for NPC swimmers. Their circular
        // packets overlap along the movement path; the shared heightfield remains an
        // extra vertex-displacement layer, not the only visible wake representation.
        if (!UseExternalPlayerFoam && EmitFoam && waterlineContact > 0.05f && !hopAirborne)
        {
            rippleTimer += dt;
            if (rippleTimer >= Mathf.Max(0.25f, RippleInterval))
            {
                rippleTimer = 0f;
                float rippleStrength = Mathf.Lerp(0.28f, 0.55f, speed01) * waterlineContact;
                WaterFoamField.EmitRipple(
                    point,
                    0.08f,
                    Mathf.Lerp(2.2f, 4.0f, speed01),
                    rippleStrength,
                    0f,
                    Mathf.Lerp(2.4f, 3.2f, speed01),
                    Vector2.zero,
                    0f,
                    1.25f);
            }
        }
        else
        {
            rippleTimer = 0f;
        }

        if (hopAirborne)
            hasFoamPoint = false;
    }

    private static float Smooth01Between(float edge0, float edge1, float value)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, value));
    }

    private void UpdateWaveAirborneState(float surfaceHeight, Transform actor, float dt)
    {
        // A deliberate hop has its own rising/falling animation path.
        if (hopAirborne || jumpWindupTimer >= 0f)
        {
            waveAirborne = false;
            waveEjectionTimer = 0f;
            return;
        }

        float bodyCenterY = GetBodyCenterY(actor);
        if (bodyCenterY > surfaceHeight + WaveEjectionHeight)
        {
            waveEjectionTimer += dt;
            if (waveEjectionTimer >= WaveEjectionConfirmationTime)
                waveAirborne = true;
        }
        else if (bodyCenterY <= surfaceHeight + WaveReentryMargin)
        {
            // Use a separate lower threshold to prevent rapid state toggling as
            // the body skims the waterline during an oscillating wave.
            waveEjectionTimer = 0f;
            waveAirborne = false;
        }
    }

    private void EmitHopSplash(float intensity, float fallDuration = 0f, bool landing = false)
    {
        if (!EmitFoam)
            return;
        if (foam == null)
        {
            foam = GetComponent<WaterContactFoam>();
            if (foam == null)
                foam = gameObject.AddComponent<WaterContactFoam>();
        }
        Vector3 point = ActorTransform != null ? ActorTransform.position : transform.position;
        point.y = GetCurrentSurfaceHeight(ActorTransform != null ? ActorTransform : transform);
        if (landing)
            foam.EmitImpactRipplePair(point, fallDuration, intensity);
        else
            foam.EmitRipple(point, 0.3f, 0.2f + 0.3f * intensity, 0.6f + 0.4f * intensity,
                Vector3.zero, 0f, 0.3f, 0.25f);
        foam.EmitBurst(point, intensity, 0.55f);
    }

    private void LaunchHop(Vector3 desiredDirection, Transform actor)
    {
        jumpWindupTimer = -1f;
        hopAirborne = true;
        hopDescendingDuration = 0f;
        waveAirborne = false;
        waveEjectionTimer = 0f;
        verticalVelocity = JumpVelocity;

        Vector3 direction = desiredDirection;
        if (direction.sqrMagnitude < 0.0001f)
            direction = horizontalVelocity.sqrMagnitude > 0.04f ? horizontalVelocity : Vector3.zero;
        if (direction.sqrMagnitude > 0.0001f)
            horizontalVelocity += direction.normalized * JumpSpeedBoost;
        EmitHopSplash(0.55f);
        HopStarted?.Invoke();
    }

    /// <summary>
    /// Virtual damped oscillator chasing the horizontal displacement of the water particle
    /// under the swimmer. It is a linear filter of a zero-mean signal, so the swimmer is
    /// carried side to side and returns to the same place every wave cycle. Returns this
    /// step's displacement.
    /// </summary>
    private Vector2 UpdateWaveDrift(float dt, float surfaceHeight, float bodyHeight, Transform actor)
    {
        if (!EnableWaveDrift && driftOffset.sqrMagnitude < 0.000001f && driftVelocity.sqrMagnitude < 0.000001f)
            return Vector2.zero;

        float exposure = 0f;
        if (EnableWaveDrift && !hopAirborne && Ocean != null)
        {
            float bottom = GetBodyCenterY(actor) - bodyHeight * 0.5f;
            float submerged = Mathf.Clamp01((surfaceHeight - bottom) / Mathf.Max(0.1f, bodyHeight));
            exposure = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DriftFadeStart, DriftFadeEnd, submerged));
        }
        driftExposure = exposure;

        Vector2 lattice = HasFreshSurfaceSample() ? sampledLattice : Vector2.zero;
        Vector2 target = Vector2.ClampMagnitude(lattice * (WaveDriftFollow * exposure), WaveDriftMaxOffset);

        float omega = WaveDriftResponse;
        Vector2 acceleration = (target - driftOffset) * (omega * omega)
                               - driftVelocity * (2f * WaveDriftDamping * omega);
        driftVelocity += acceleration * dt;
        Vector2 previous = driftOffset;
        driftOffset += driftVelocity * dt;
        return driftOffset - previous;
    }

    public void SetPlanarVelocity(Vector3 velocity)
    {
        horizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
    }

    public void ResetVerticalVelocity()
    {
        verticalVelocity = 0f;
    }

    /// <summary>
    /// Receives vertical motion when an airborne grounded jump/fall enters the water.
    /// The buoyancy solver takes over from this initial velocity on its next physics step.
    /// </summary>
    public void SetVerticalVelocity(float value)
    {
        verticalVelocity = value;
    }

    private Vector3 GetCameraRelativeDirection(Vector2 input, Transform actor)
    {
        Transform reference = ReferenceTransform != null ? ReferenceTransform : actor;
        Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized;
        Vector3 direction = right * input.x + forward * input.y;
        return direction.sqrMagnitude > 1f ? direction.normalized : direction;
    }

    private bool HasFreshSurfaceSample()
    {
        return hasSurfaceSample &&
               Time.realtimeSinceStartup - sampledSurfaceTime <= MaximumSurfaceSampleAge;
    }

    private void UpdateFilteredSurfaceTarget(float dt)
    {
        if (Ocean == null)
            return;

        if (!hasSmoothedSurfaceHeight)
        {
            smoothedSurfaceHeight = hasSurfaceSample ? sampledSurfaceHeight : Ocean.WaterLevelY;
            surfaceHeightSmoothVelocity = 0f;
            filteredSurfaceVerticalVelocity = 0f;
            hasSmoothedSurfaceHeight = true;
        }

        float sampleAge = hasSurfaceSample
            ? Time.realtimeSinceStartup - sampledSurfaceTime
            : float.PositiveInfinity;

        // Do not jump from a valid displaced wave sample to flat WaterLevelY just
        // because one asynchronous readback missed its deadline. Hold briefly, then
        // converge slowly toward the mean level if sampling really stops.
        float targetHeight = hasSurfaceSample && sampleAge <= SurfaceSampleHoldTime
            ? sampledSurfaceHeight
            : Ocean.WaterLevelY;

        smoothedSurfaceHeight = Mathf.SmoothDamp(
            smoothedSurfaceHeight, targetHeight, ref surfaceHeightSmoothVelocity,
            SurfaceHeightSmoothTime, MaximumSurfaceFollowSpeed, dt);
        surfaceHeightSmoothVelocity = Mathf.Clamp(
            surfaceHeightSmoothVelocity, -MaximumWaveVerticalSpeed, MaximumWaveVerticalSpeed);
        filteredSurfaceVerticalVelocity = Mathf.Lerp(
            filteredSurfaceVerticalVelocity, surfaceHeightSmoothVelocity,
            Mathf.Clamp01(WaveVelocitySmoothing));
    }

    private float GetCurrentSurfaceHeight(Transform actor)
    {
        if (Ocean == null)
            return GetBodyCenterY(actor) - (0.5f - TargetSubmersion) * GetBodyHeight();

        return hasSmoothedSurfaceHeight ? smoothedSurfaceHeight
            : hasSurfaceSample ? sampledSurfaceHeight : Ocean.WaterLevelY;
    }

    private float GetCurrentSurfaceVerticalVelocity()
    {
        if (!hasSmoothedSurfaceHeight ||
            (!HasFreshSurfaceSample() && (!hasSurfaceSample ||
             Time.realtimeSinceStartup - sampledSurfaceTime > SurfaceSampleHoldTime)))
            return 0f;
        return filteredSurfaceVerticalVelocity;
    }

    private float GetBodyHeight()
    {
        if (Controller != null)
            return Mathf.Max(0.1f, Controller.height * Mathf.Abs(Controller.transform.lossyScale.y));
        if (BodyCollider != null)
            return Mathf.Max(0.1f, BodyCollider.bounds.size.y);
        return AssumedBodyHeight;
    }

    private float GetBodyCenterY(Transform actor)
    {
        if (Controller != null)
            return Controller.transform.TransformPoint(Controller.center).y;
        if (BodyCollider != null)
            return BodyCollider.bounds.center.y;
        if (Body != null)
            return Body.worldCenterOfMass.y;
        return actor.position.y + AssumedBodyCenterOffset;
    }

    private void RequestSurfaceSampleIfNeeded()
    {
        if (Ocean == null || !Ocean.CanSampleSurface || sampleRequestPending ||
            Time.time < nextSampleRequestTime)
            return;

        Transform actor = ActorTransform;
        if (actor == null)
            return;

        nextSampleRequestTime = Time.time + SurfaceSampleInterval;
        sampleRequestPending = true;
        int generation = sampleGeneration;

        float radius = Controller != null
            ? Controller.radius * Mathf.Max(Mathf.Abs(Controller.transform.lossyScale.x),
                Mathf.Abs(Controller.transform.lossyScale.z))
            : 0.3f;
        float spread = Mathf.Max(0.08f, radius * SurfaceProbeSpread);
        Vector3 right = Vector3.ProjectOnPlane(actor.right, Vector3.up).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(actor.forward, Vector3.up).normalized;
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;

        Vector3 center = actor.position;
        Vector3[] positions =
        {
            center,
            center + right * spread,
            center - right * spread,
            center + forward * spread,
            center - forward * spread
        };

        bool queued = Ocean.RequestSurfaceSamples(positions, samples =>
        {
            if (generation != sampleGeneration)
                return;

            sampleRequestPending = false;
            if (!isActiveAndEnabled || samples == null || samples.Length == 0)
                return;

            // The boat's buoyancy uses several hull probes. Use the same principle
            // for a swimmer: a weighted five-point average tracks the local patch,
            // while limiting an isolated sharp crest/trough at one probe.
            bool centerValid = samples.Length > 0 && samples[0].IsValid;
            float centerHeight = centerValid ? samples[0].Height : 0f;
            float heightSum = 0f;
            float latticeX = 0f;
            float latticeY = 0f;
            float weightSum = 0f;
            float sampleTime = 0f;

            for (int i = 0; i < samples.Length && i < 5; i++)
            {
                SpectralOceanFFT.SurfaceSample sample = samples[i];
                if (!sample.IsValid || float.IsNaN(sample.Height) || float.IsInfinity(sample.Height))
                    continue;

                float weight = i == 0 ? 0.4f : 0.15f;
                float height = sample.Height;
                if (centerValid && i > 0)
                    height = Mathf.Clamp(height, centerHeight - MaximumProbeHeightDifference,
                        centerHeight + MaximumProbeHeightDifference);

                heightSum += height * weight;
                latticeX += sample.LatticeDisplacementXZ.x * weight;
                latticeY += sample.LatticeDisplacementXZ.y * weight;
                weightSum += weight;
                sampleTime = Mathf.Max(sampleTime, sample.SampleTime);
            }

            if (weightSum <= 0.001f)
                return;

            float averagedHeight = heightSum / weightSum;
            sampledLattice = new Vector2(latticeX, latticeY) / weightSum;
            sampledSurfaceHeight = averagedHeight;
            sampledSurfaceTime = sampleTime > 0f ? sampleTime : Time.realtimeSinceStartup;

            if (!hasSmoothedSurfaceHeight)
            {
                smoothedSurfaceHeight = averagedHeight;
                surfaceHeightSmoothVelocity = 0f;
                filteredSurfaceVerticalVelocity = 0f;
                hasSmoothedSurfaceHeight = true;
            }
            hasSurfaceSample = true;
        });

        if (!queued)
            sampleRequestPending = false;
    }

    private void ResolveReferences()
    {
        if (Controller == null)
            Controller = GetComponent<CharacterController>();
        if (Body == null)
            Body = GetComponent<Rigidbody>();
        if (BodyCollider == null)
            BodyCollider = GetComponent<Collider>();
        if (Ocean == null)
            Ocean = Object.FindAnyObjectByType<SpectralOceanFFT>();
        if (Stamina == null)
            Stamina = StaminaComponent.Resolve(gameObject);
    }

    private void CacheRigidbodyGravity()
    {
        if (Body == null || Controller != null || bodyGravityCached)
            return;

        bodyGravityWasEnabled = Body.useGravity;
        bodyGravityCached = true;
    }

    private void SetRigidbodyGravityControlled(bool controlled)
    {
        if (Body == null || Controller != null)
            return;

        CacheRigidbodyGravity();
        if (!bodyGravityCached)
            return;

        Body.useGravity = controlled ? false : bodyGravityWasEnabled;
    }
}
