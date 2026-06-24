using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public class BirdAI : MonoBehaviour
{
    private enum BirdState
    {
        Idle,
        ForageMove,
        Peck,
        Sing,
        Alert,
        FleeMove,
        Landing
    }

    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform player;
    [SerializeField] private Transform visionOrigin;
    [SerializeField] private bool autoFindPlayer = true;
    [SerializeField] private bool disableAnimationTesterOnStart = true;

    [Header("Zone")]
    [SerializeField] private BirdZone assignedZone;

    [Header("Behaviour Probabilities")]
    [Range(0f, 1f)][SerializeField] private float forageChance = 0.55f;
    [Range(0f, 1f)][SerializeField] private float singChance = 0.18f;

    [Header("Behaviour Timing")]
    [SerializeField] private Vector2 idleDurationRange = new Vector2(1.2f, 3.5f);
    [SerializeField] private Vector2 peckDurationRange = new Vector2(1.0f, 2.2f);
    [SerializeField] private Vector2 singDurationRange = new Vector2(1.8f, 3.5f);
    [SerializeField] private Vector2 alertDurationRange = new Vector2(0.8f, 1.6f);
    [SerializeField] private float landingDuration = 0.75f;

    [Header("Ground Movement")]
    [SerializeField] private float forageRadius = 4f;
    [SerializeField] private Vector2 hopDistanceRange = new Vector2(0.35f, 1.1f);
    [SerializeField] private float hopDuration = 0.55f;
    [SerializeField] private float hopArcHeight = 0.10f;
    [SerializeField] private float rotationSpeed = 420f;
    [Tooltip("Use 180 if the bird visually moves backwards.")]
    [SerializeField] private float modelForwardOffsetDegrees = 0f;

    [Header("Local Separation")]
    [SerializeField] private float birdSeparationRadius = 0.75f;
    [SerializeField] private float birdSeparationStrength = 1.25f;

    [Header("Ground Projection / Raycast")]
    [SerializeField] private bool projectMovesToGround = true;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundRayHeight = 10f;
    [SerializeField] private float groundRayDistance = 50f;
    [SerializeField] private float groundOffsetY = 0f;
    [SerializeField] private bool drawGroundRayDebug = false;

    [Header("Player Detection")]
    [SerializeField] private float alertDistance = 6f;
    [SerializeField] private float fleeDistance = 3f;
    [SerializeField] private float calmDistance = 8f;
    [SerializeField] private bool requireLineOfSightForFear = false;
    [SerializeField] private float postFleeIgnoreFearDuration = 2.5f;
    [SerializeField] private LayerMask lineOfSightMask = ~0;

    [Header("Flight / Flee")]
    [SerializeField] private float fleeTravelDistance = 6f;
    [SerializeField] private float fleeDuration = 1.2f;
    [SerializeField] private float flightSpeed = 3.0f;
    [SerializeField] private float minFlightDuration = 1.2f;
    [SerializeField] private float maxFlightDuration = 4.0f;
    [SerializeField] private float fleeArcHeight = 1.5f;
    [SerializeField] private float fleeSideRandomness = 0.35f;
    [SerializeField] private bool updateHomeAfterFlee = true;

    [Header("Animation Clips - Calm")]
    [SerializeField] private AnimationClip[] idleClips;
    [SerializeField] private AnimationClip[] hopClips;
    [SerializeField] private AnimationClip[] peckClips;
    [SerializeField] private AnimationClip[] singClips;

    [Header("Animation Clips - Fear / Flight")]
    [SerializeField] private AnimationClip[] alertClips;
    [SerializeField] private AnimationClip[] flyClips;
    [SerializeField] private AnimationClip[] landingClips;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] songClips;
    [SerializeField] private AudioClip[] flyAwayClips;
    [SerializeField, Range(0f, 1f)] private float songVolume = 0.55f;
    [SerializeField, Range(0f, 1f)] private float flyAwayVolume = 0.65f;
    [SerializeField] private float songEventCooldown = 0.75f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;
    [SerializeField] private bool drawGizmos = true;

    private BirdState state;
    private float stateTimer;
    private Vector3 homePosition;
    private Vector3 moveStart;
    private Vector3 moveEnd;
    private float moveTimer;
    private float moveDuration;
    private float moveArcHeight;

    private PlayableGraph playableGraph;
    private AnimationPlayableOutput animationOutput;
    private AnimationClipPlayable currentPlayable;
    private AnimationClip currentClip;
    private bool currentClipLoops;
    private float ignoreFearUntilTime;
    private float nextAllowedSongTime;

    private void Reset()
    {
        animator = GetComponent<Animator>();
        visionOrigin = transform;
    }

    private void Awake()
    {
        DisableAnimationTesterIfPresent();

        if (animator == null)
            animator = GetComponent<Animator>();

        if (visionOrigin == null)
            visionOrigin = transform;

        homePosition = ProjectToGroundIfPossible(transform.position);
        transform.position = homePosition;

        if (animator != null)
            animator.applyRootMotion = false;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 1f;
        audioSource.dopplerLevel = 0f;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = 25f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    private void Start()
    {
        TryAutoFindPlayer();
        EnterIdle();
    }

    private void Update()
    {
        TryAutoFindPlayer();
        UpdateAnimationLoop();
        UpdateFearState();

        switch (state)
        {
            case BirdState.Idle:
                UpdateIdle();
                break;

            case BirdState.ForageMove:
                UpdateMoveState(BirdState.Peck);
                break;

            case BirdState.Peck:
                UpdateTimedState(EnterIdle);
                break;

            case BirdState.Sing:
                UpdateTimedState(EnterIdle);
                break;

            case BirdState.Alert:
                UpdateAlert();
                break;

            case BirdState.FleeMove:
                UpdateMoveState(BirdState.Landing);
                break;

            case BirdState.Landing:
                UpdateTimedState(EnterIdle);
                break;
        }
    }

    private void OnDestroy()
    {
        if (currentPlayable.IsValid())
            currentPlayable.Destroy();

        if (playableGraph.IsValid())
            playableGraph.Destroy();
    }

    public void AssignZone(BirdZone zone)
    {
        assignedZone = zone;
    }

    public void SetPlayer(Transform playerTransform)
    {
        player = playerTransform;
    }

    private void DisableAnimationTesterIfPresent()
    {
        if (!disableAnimationTesterOnStart)
            return;

        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null || behaviour == this)
                continue;

            if (behaviour.GetType().Name != "BirdAnimationTester")
                continue;

            if (!behaviour.enabled)
                continue;

            behaviour.enabled = false;

            if (showDebugLogs)
                Debug.Log("[BirdAI] BirdAnimationTester disabled automatically to avoid animation conflicts.");
        }
    }

    private void TryAutoFindPlayer()
    {
        if (!autoFindPlayer || player != null)
            return;

        Camera mainCamera = Camera.main;

        if (mainCamera != null)
        {
            player = mainCamera.transform;
            return;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

        if (playerObject != null)
            player = playerObject.transform;
    }

    private void UpdateIdle()
    {
        stateTimer -= Time.deltaTime;

        if (stateTimer > 0f)
            return;

        ChooseNextNaturalBehaviour();
    }

    private void UpdateTimedState(Action nextState)
    {
        stateTimer -= Time.deltaTime;

        if (stateTimer <= 0f)
            nextState?.Invoke();
    }

    private void UpdateMoveState(BirdState nextState)
    {
        moveTimer += Time.deltaTime;
        float t = Mathf.Clamp01(moveTimer / Mathf.Max(0.001f, moveDuration));
        float smoothT = t * t * (3f - 2f * t);

        Vector3 flatPosition = Vector3.Lerp(moveStart, moveEnd, smoothT);

        if (state == BirdState.ForageMove && assignedZone != null)
            flatPosition += assignedZone.GetSeparationOffset(this, birdSeparationRadius, birdSeparationStrength) * Time.deltaTime;

        float arc = Mathf.Sin(t * Mathf.PI) * moveArcHeight;
        transform.position = flatPosition + Vector3.up * arc;

        RotateTowards(moveEnd);

        if (t < 1f)
            return;

        transform.position = moveEnd;

        if (nextState == BirdState.Peck)
            EnterPeck();
        else if (nextState == BirdState.Landing)
            EnterLanding();
    }

    private void UpdateAlert()
    {
        if (player != null)
            RotateTowards(player.position);

        if (player != null && Vector3.Distance(transform.position, player.position) > calmDistance)
        {
            EnterIdle();
            return;
        }

        stateTimer -= Time.deltaTime;

        if (stateTimer <= 0f)
            stateTimer = UnityEngine.Random.Range(alertDurationRange.x, alertDurationRange.y);
    }

    private void UpdateFearState()
    {
        if (player == null)
            return;

        if (Time.time < ignoreFearUntilTime)
            return;

        if (state == BirdState.FleeMove || state == BirdState.Landing)
            return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer <= fleeDistance && CanReactToPlayer())
        {
            EnterFlee();
            return;
        }

        if (distanceToPlayer <= alertDistance && state != BirdState.Alert && CanReactToPlayer())
        {
            EnterAlert();
        }
    }

    private bool CanReactToPlayer()
    {
        if (!requireLineOfSightForFear || player == null)
            return true;

        Vector3 origin = visionOrigin != null ? visionOrigin.position : transform.position + Vector3.up * 0.2f;
        Vector3 target = player.position;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        if (distance <= 0.001f)
            return true;

        direction /= distance;

        if (!Physics.Raycast(origin, direction, out RaycastHit hit, distance, lineOfSightMask, QueryTriggerInteraction.Ignore))
            return true;

        return hit.transform == player || hit.transform.IsChildOf(player);
    }

    private void ChooseNextNaturalBehaviour()
    {
        float roll = UnityEngine.Random.value;

        if (roll < singChance && HasAnyClip(singClips))
        {
            EnterSing();
            return;
        }

        if (roll < singChance + forageChance && HasAnyClip(hopClips))
        {
            EnterForageMove();
            return;
        }

        EnterIdle();
    }

    private void EnterIdle()
    {
        state = BirdState.Idle;
        stateTimer = UnityEngine.Random.Range(idleDurationRange.x, idleDurationRange.y);
        PlayRandomClip(idleClips, true);
        LogState("Idle");
    }

    private void EnterForageMove()
    {
        state = BirdState.ForageMove;

        Vector3 candidate;

        if (assignedZone != null && assignedZone.TryGetActivityPosition(BirdHabitatAreaType.Food, this, out candidate))
        {
            // Destination provided by the ecological zone.
        }
        else
        {
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(hopDistanceRange.x, hopDistanceRange.y);
            candidate = transform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);
            Vector3 offsetFromHome = candidate - homePosition;

            if (offsetFromHome.magnitude > forageRadius)
                candidate = homePosition + offsetFromHome.normalized * forageRadius;

            candidate = ProjectToGroundIfPossible(candidate);
        }

        BeginMove(candidate, hopDuration, hopArcHeight, hopClips, true);
        LogState("ForageMove");
    }

    private void EnterPeck()
    {
        state = BirdState.Peck;
        stateTimer = UnityEngine.Random.Range(peckDurationRange.x, peckDurationRange.y);
        PlayRandomClip(peckClips, true);
        LogState("Peck");
    }

    private void EnterSing()
    {
        state = BirdState.Sing;
        stateTimer = UnityEngine.Random.Range(singDurationRange.x, singDurationRange.y);
        PlayRandomClip(singClips, true);
        LogState("Sing");
    }

    private void EnterAlert()
    {
        if (state == BirdState.Alert)
            return;

        state = BirdState.Alert;
        stateTimer = UnityEngine.Random.Range(alertDurationRange.x, alertDurationRange.y);
        PlayRandomClip(HasAnyClip(alertClips) ? alertClips : idleClips, true);
        LogState("Alert");
    }

    private void EnterFlee()
    {
        state = BirdState.FleeMove;
        ignoreFearUntilTime = Time.time + fleeDuration + landingDuration + postFleeIgnoreFearDuration;
        PlayRandomOneShot(flyAwayClips, flyAwayVolume);

        Vector3 awayDirection = transform.position - player.position;
        awayDirection.y = 0f;

        if (awayDirection.sqrMagnitude < 0.001f)
            awayDirection = -transform.forward;

        awayDirection.Normalize();

        Vector3 sideDirection = Vector3.Cross(Vector3.up, awayDirection).normalized;
        awayDirection = (awayDirection + sideDirection * UnityEngine.Random.Range(-fleeSideRandomness, fleeSideRandomness)).normalized;

        Vector3 destination;

        if (assignedZone != null && assignedZone.TryGetFleePosition(this, player.position, out destination))
        {
            // Destination provided by the ecological zone.
        }
        else
        {
            destination = transform.position + awayDirection * fleeTravelDistance;
            destination = ProjectToGroundIfPossible(destination);
        }

        if (updateHomeAfterFlee)
            homePosition = destination;

        float flightDistance = Vector3.Distance(transform.position, destination);
        float dynamicFleeDuration = Mathf.Clamp(
            flightDistance / Mathf.Max(0.1f, flightSpeed),
            minFlightDuration,
            maxFlightDuration
        );

        BeginMove(destination, dynamicFleeDuration, fleeArcHeight, flyClips, true);
        LogState("Flee");
    }

    private void EnterLanding()
    {
        state = BirdState.Landing;
        stateTimer = landingDuration;
        PlayRandomClip(landingClips, false);
        LogState("Landing");
    }

    private void BeginMove(Vector3 destination, float duration, float arcHeight, AnimationClip[] animationClips, bool loopAnimation)
    {
        moveStart = transform.position;
        moveEnd = destination;
        moveTimer = 0f;
        moveDuration = Mathf.Max(0.001f, duration);
        moveArcHeight = Mathf.Max(0f, arcHeight);

        RotateTowards(destination, true);
        PlayRandomClip(animationClips, loopAnimation);
    }

    private Vector3 ProjectToGroundIfPossible(Vector3 point)
    {
        if (!projectMovesToGround)
            return point;

        Vector3 origin = new Vector3(point.x, point.y + groundRayHeight, point.z);
        float maxDistance = groundRayHeight + groundRayDistance;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxDistance, groundMask, QueryTriggerInteraction.Ignore);

        if (drawGroundRayDebug)
            Debug.DrawRay(origin, Vector3.down * maxDistance, hits.Length > 0 ? Color.green : Color.red, 1f);

        if (hits == null || hits.Length == 0)
            return new Vector3(point.x, homePosition.y, point.z);

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;

            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;

            return hits[i].point + Vector3.up * groundOffsetY;
        }

        return new Vector3(point.x, homePosition.y, point.z);
    }

    private void RotateTowards(Vector3 targetPosition, bool instant = false)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

        if (Mathf.Abs(modelForwardOffsetDegrees) > 0.001f)
            targetRotation *= Quaternion.Euler(0f, modelForwardOffsetDegrees, 0f);

        if (instant)
            transform.rotation = targetRotation;
        else
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    private void PlayRandomClip(AnimationClip[] clips, bool loop)
    {
        PlayClip(GetRandomClip(clips), loop);
    }

    private AnimationClip GetRandomClip(AnimationClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return null;

        AnimationClip[] validClips = Array.FindAll(clips, clip => clip != null);

        if (validClips.Length == 0)
            return null;

        return validClips[UnityEngine.Random.Range(0, validClips.Length)];
    }

    private bool HasAnyClip(AnimationClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return false;

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                return true;
        }

        return false;
    }

    private void PlayClip(AnimationClip clip, bool loop)
    {
        if (animator == null || clip == null)
            return;

        EnsurePlayableGraph();

        if (currentPlayable.IsValid())
            currentPlayable.Destroy();

        currentClip = clip;
        currentClipLoops = loop;
        currentPlayable = AnimationClipPlayable.Create(playableGraph, clip);
        currentPlayable.SetTime(0d);
        currentPlayable.SetSpeed(1d);
        currentPlayable.SetApplyFootIK(false);

        animationOutput.SetSourcePlayable(currentPlayable);

        if (!playableGraph.IsPlaying())
            playableGraph.Play();
    }

    private void EnsurePlayableGraph()
    {
        if (playableGraph.IsValid())
            return;

        playableGraph = PlayableGraph.Create($"{name}_BirdAI_PlayableGraph");
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        animationOutput = AnimationPlayableOutput.Create(playableGraph, "BirdAIAnimationOutput", animator);
    }

    private void UpdateAnimationLoop()
    {
        if (!currentClipLoops || currentClip == null || !currentPlayable.IsValid())
            return;

        if (currentClip.length <= 0.001f)
            return;

        if (currentPlayable.GetTime() >= currentClip.length)
            currentPlayable.SetTime(0d);
    }

    private void LogState(string stateName)
    {
        if (showDebugLogs)
            Debug.Log($"[BirdAI] {name} -> {stateName}");
    }


    private void PlayRandomOneShot(AudioClip[] clips, float volume)
    {
        if (audioSource == null || clips == null || clips.Length == 0)
            return;

        AudioClip[] validClips = Array.FindAll(clips, clip => clip != null);

        if (validClips.Length == 0)
            return;

        AudioClip clipToPlay = validClips[UnityEngine.Random.Range(0, validClips.Length)];

        if (clipToPlay != null)
            audioSource.PlayOneShot(clipToPlay, volume);
    }

    // Animation Events called directly by imported bird animation clips.
    // Keeping them here avoids needing a separate receiver component during the prototype.
    public void PlaySong()
    {
        if (Time.time < nextAllowedSongTime)
            return;

        nextAllowedSongTime = Time.time + songEventCooldown;
        PlayRandomOneShot(songClips, songVolume);
    }

    public void ResetHopInt()
    {
        // Optional later: reset hop animation state here.
    }

    // Safety receivers for possible event names in other imported clips.
    public void Footstep() { }
    public void FootStep() { }
    public void WingFlap() { }
    public void PlayWingFlap() { }

    private void OnValidate()
    {
        idleDurationRange.x = Mathf.Max(0.1f, idleDurationRange.x);
        idleDurationRange.y = Mathf.Max(idleDurationRange.x, idleDurationRange.y);

        peckDurationRange.x = Mathf.Max(0.1f, peckDurationRange.x);
        peckDurationRange.y = Mathf.Max(peckDurationRange.x, peckDurationRange.y);

        singDurationRange.x = Mathf.Max(0.1f, singDurationRange.x);
        singDurationRange.y = Mathf.Max(singDurationRange.x, singDurationRange.y);

        alertDurationRange.x = Mathf.Max(0.1f, alertDurationRange.x);
        alertDurationRange.y = Mathf.Max(alertDurationRange.x, alertDurationRange.y);

        fleeDistance = Mathf.Max(0.1f, fleeDistance);
        alertDistance = Mathf.Max(fleeDistance + 0.1f, alertDistance);
        calmDistance = Mathf.Max(alertDistance + 0.1f, calmDistance);

        forageRadius = Mathf.Max(0.1f, forageRadius);
        hopDuration = Mathf.Max(0.05f, hopDuration);
        fleeDuration = Mathf.Max(0.05f, fleeDuration);
        flightSpeed = Mathf.Max(0.1f, flightSpeed);
        minFlightDuration = Mathf.Max(0.05f, minFlightDuration);
        maxFlightDuration = Mathf.Max(minFlightDuration, maxFlightDuration);
        birdSeparationRadius = Mathf.Max(0f, birdSeparationRadius);
        birdSeparationStrength = Mathf.Max(0f, birdSeparationStrength);
        postFleeIgnoreFearDuration = Mathf.Max(0f, postFleeIgnoreFearDuration);
        groundRayHeight = Mathf.Max(0.1f, groundRayHeight);
        groundRayDistance = Mathf.Max(0.1f, groundRayDistance);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Vector3 center = Application.isPlaying ? homePosition : transform.position;

        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(center, forageRadius);

        Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, alertDistance);

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, fleeDistance);
    }
}
