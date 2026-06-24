using System.Collections.Generic;
using UnityEngine;

public class BirdZone : MonoBehaviour
{
    [Header("Species / Population")]
    [SerializeField] private BirdSpeciesProfile speciesProfile;
    [SerializeField] private Transform player;
    [SerializeField] private bool autoFindPlayer = true;
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private int birdCountOverride = -1;

    [Header("Zone")]
    [SerializeField] private float zoneRadius = 25f;
    [SerializeField] private bool collectChildAreasOnStart = true;
    [SerializeField] private List<BirdHabitatArea> habitatAreas = new List<BirdHabitatArea>();

    [Header("Ground / Surface Validation")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private LayerMask waterMask = 0;
    [SerializeField] private LayerMask blockingMask = 0;
    [SerializeField] private float groundRayHeight = 30f;
    [SerializeField] private float groundRayDistance = 80f;
    [SerializeField] private float groundOffsetY = 0f;
    [SerializeField] private float maxGroundSlopeAngle = 35f;
    [SerializeField] private float waterAvoidanceRadius = 0.35f;
    [SerializeField] private float obstacleClearanceRadius = 0.25f;

    [Header("Spawn Rules")]
    [SerializeField] private int maxSpawnAttemptsPerBird = 40;
    [SerializeField] private bool parentSpawnedBirdsToZone = true;

    [Header("Activity / Reservation")]
    [SerializeField] private int maxSampleAttempts = 35;
    [SerializeField] private float activityReservationRadius = 0.65f;
    [SerializeField] private float reservationDuration = 4f;

    [Header("Dynamic Flee")]
    [SerializeField] private float fleeMinDistance = 4f;
    [SerializeField] private float fleeMaxDistance = 8f;
    [SerializeField] private float fleeConeAngle = 70f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;
    [SerializeField] private bool drawGizmos = true;

    private readonly List<BirdAI> ownedBirds = new List<BirdAI>();
    private readonly List<PositionReservation> reservations = new List<PositionReservation>();

    private struct PositionReservation
    {
        public BirdAI owner;
        public Vector3 position;
        public float radius;
        public float untilTime;
    }

    private void Start()
    {
        TryAutoFindPlayer();

        if (collectChildAreasOnStart)
            CollectChildAreas();

        if (spawnOnStart)
            SpawnPopulation();
    }

    private void Update()
    {
        CleanupReservations();
    }

    private void TryAutoFindPlayer()
    {
        if (!autoFindPlayer || player != null)
            return;

        if (Camera.main != null)
            player = Camera.main.transform;
    }

    [ContextMenu("Collect Child Areas")]
    public void CollectChildAreas()
    {
        habitatAreas.Clear();
        habitatAreas.AddRange(GetComponentsInChildren<BirdHabitatArea>(true));

        if (showDebugLogs)
            Debug.Log($"[BirdZone] {name} collected {habitatAreas.Count} habitat areas.");
    }

    [ContextMenu("Spawn Population")]
    public void SpawnPopulation()
    {
        if (speciesProfile == null || speciesProfile.birdPrefab == null)
        {
            Debug.LogWarning($"[BirdZone] {name} cannot spawn: missing Species Profile or bird prefab.");
            return;
        }

        int desiredCount = birdCountOverride >= 0 ? birdCountOverride : speciesProfile.defaultBirdCount;

        for (int i = ownedBirds.Count; i < desiredCount; i++)
        {
            if (!TryGetSpawnPosition(out Vector3 spawnPosition))
            {
                Debug.LogWarning($"[BirdZone] {name} failed to find a spawn position for bird {i + 1}.");
                continue;
            }

            Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            Transform parent = parentSpawnedBirdsToZone ? transform : null;
            GameObject birdObject = Instantiate(speciesProfile.birdPrefab, spawnPosition, rotation, parent);
            birdObject.name = $"{speciesProfile.displayName}_{i + 1:00}";

            BirdAI birdAI = birdObject.GetComponent<BirdAI>();

            if (birdAI != null)
            {
                birdAI.AssignZone(this);
                birdAI.SetPlayer(player);
                RegisterBird(birdAI);
                ReservePosition(birdAI, spawnPosition, activityReservationRadius, reservationDuration);
            }
            else
            {
                Debug.LogWarning($"[BirdZone] Spawned bird prefab has no BirdAI: {birdObject.name}");
            }
        }

        if (showDebugLogs)
            Debug.Log($"[BirdZone] {name} spawned/owns {ownedBirds.Count} birds.");
    }

    public void RegisterBird(BirdAI bird)
    {
        if (bird == null || ownedBirds.Contains(bird))
            return;

        ownedBirds.Add(bird);
    }

    private bool TryGetSpawnPosition(out Vector3 position)
    {
        if (TrySampleAreaPosition(BirdHabitatAreaType.Spawn, null, maxSpawnAttemptsPerBird, out position))
            return true;

        if (TrySampleAreaPosition(BirdHabitatAreaType.Nest, null, maxSpawnAttemptsPerBird, out position))
            return true;

        if (TrySampleAreaPosition(BirdHabitatAreaType.Food, null, maxSpawnAttemptsPerBird, out position))
            return true;

        for (int attempt = 0; attempt < maxSpawnAttemptsPerBird; attempt++)
        {
            Vector2 circle = Random.insideUnitCircle * zoneRadius;
            Vector3 candidate = transform.position + new Vector3(circle.x, 0f, circle.y);

            if (TryValidatePosition(candidate, null, speciesProfile.minimumSpawnDistanceBetweenBirds, out position))
                return true;
        }

        position = transform.position;
        return false;
    }

    public bool TryGetActivityPosition(BirdHabitatAreaType type, BirdAI requester, out Vector3 position)
    {
        if (TrySampleAreaPosition(type, requester, maxSampleAttempts, out position))
        {
            ReservePosition(requester, position, activityReservationRadius, reservationDuration);
            return true;
        }

        position = default;
        return false;
    }

    public bool TryGetFleePosition(BirdAI requester, Vector3 threatPosition, out Vector3 position)
    {
        if (requester == null)
        {
            position = default;
            return false;
        }

        Vector3 away = requester.transform.position - threatPosition;
        away.y = 0f;

        if (away.sqrMagnitude < 0.001f)
            away = -requester.transform.forward;

        away.Normalize();

        for (int attempt = 0; attempt < maxSampleAttempts; attempt++)
        {
            float angle = Random.Range(-fleeConeAngle, fleeConeAngle);
            float distance = Random.Range(fleeMinDistance, fleeMaxDistance);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * away;
            Vector3 candidate = requester.transform.position + direction * distance;
            candidate = ClampToZone(candidate);

            if (TryValidatePosition(candidate, requester, activityReservationRadius, out position))
            {
                ReservePosition(requester, position, activityReservationRadius, reservationDuration);
                return true;
            }
        }

        // Fallback to nest/rest zones if dynamic flee failed.
        if (TrySampleAreaPosition(BirdHabitatAreaType.Nest, requester, maxSampleAttempts, out position))
        {
            ReservePosition(requester, position, activityReservationRadius, reservationDuration);
            return true;
        }

        if (TrySampleAreaPosition(BirdHabitatAreaType.Rest, requester, maxSampleAttempts, out position))
        {
            ReservePosition(requester, position, activityReservationRadius, reservationDuration);
            return true;
        }

        position = default;
        return false;
    }

    private bool TrySampleAreaPosition(BirdHabitatAreaType type, BirdAI requester, int attempts, out Vector3 position)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            BirdHabitatArea area = GetWeightedRandomArea(type);

            if (area == null)
                break;

            Vector3 candidate = area.GetRandomPosition();

            if (TryValidatePosition(candidate, requester, activityReservationRadius, out position))
                return true;
        }

        position = default;
        return false;
    }

    private BirdHabitatArea GetWeightedRandomArea(BirdHabitatAreaType type)
    {
        float totalWeight = 0f;

        foreach (BirdHabitatArea area in habitatAreas)
        {
            if (area == null || !area.isEnabled || area.areaType != type)
                continue;

            totalWeight += Mathf.Max(0f, area.weight);
        }

        if (totalWeight <= 0f)
            return null;

        float roll = Random.Range(0f, totalWeight);

        foreach (BirdHabitatArea area in habitatAreas)
        {
            if (area == null || !area.isEnabled || area.areaType != type)
                continue;

            roll -= Mathf.Max(0f, area.weight);

            if (roll <= 0f)
                return area;
        }

        return null;
    }

    private bool TryValidatePosition(Vector3 candidate, BirdAI requester, float minBirdDistance, out Vector3 position)
    {
        candidate = ClampToZone(candidate);

        if (!TryProjectToGround(candidate, out position))
            return false;

        if (waterMask.value != 0 && Physics.CheckSphere(position, waterAvoidanceRadius, waterMask, QueryTriggerInteraction.Ignore))
            return false;

        if (blockingMask.value != 0 && Physics.CheckSphere(position, obstacleClearanceRadius, blockingMask, QueryTriggerInteraction.Ignore))
            return false;

        if (!IsFarEnoughFromPlayer(position))
            return false;

        if (!IsFarEnoughFromBirds(position, requester, minBirdDistance))
            return false;

        if (IsPositionReserved(position, requester, minBirdDistance))
            return false;

        return true;
    }

    private bool TryProjectToGround(Vector3 point, out Vector3 projected)
    {
        Vector3 origin = new Vector3(point.x, point.y + groundRayHeight, point.z);
        float maxDistance = groundRayHeight + groundRayDistance;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxDistance, groundMask, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
        {
            projected = default;
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            float slope = Vector3.Angle(hit.normal, Vector3.up);

            if (slope > maxGroundSlopeAngle)
                continue;

            projected = hit.point + Vector3.up * groundOffsetY;
            return true;
        }

        projected = default;
        return false;
    }

    private Vector3 ClampToZone(Vector3 position)
    {
        Vector3 offset = position - transform.position;
        offset.y = 0f;

        if (offset.magnitude <= zoneRadius)
            return position;

        Vector3 clamped = transform.position + offset.normalized * zoneRadius;
        clamped.y = position.y;
        return clamped;
    }

    private bool IsFarEnoughFromPlayer(Vector3 position)
    {
        if (player == null || speciesProfile == null)
            return true;

        return Vector3.Distance(position, player.position) >= speciesProfile.minimumSpawnDistanceFromPlayer;
    }

    private bool IsFarEnoughFromBirds(Vector3 position, BirdAI requester, float minDistance)
    {
        foreach (BirdAI bird in ownedBirds)
        {
            if (bird == null || bird == requester)
                continue;

            if (Vector3.Distance(position, bird.transform.position) < minDistance)
                return false;
        }

        return true;
    }

    private void ReservePosition(BirdAI owner, Vector3 position, float radius, float duration)
    {
        if (owner == null || radius <= 0f || duration <= 0f)
            return;

        reservations.Add(new PositionReservation
        {
            owner = owner,
            position = position,
            radius = radius,
            untilTime = Time.time + duration
        });
    }

    private bool IsPositionReserved(Vector3 position, BirdAI requester, float radius)
    {
        CleanupReservations();

        foreach (PositionReservation reservation in reservations)
        {
            if (reservation.owner == requester)
                continue;

            float minDistance = radius + reservation.radius;

            if (Vector3.Distance(position, reservation.position) < minDistance)
                return true;
        }

        return false;
    }

    private void CleanupReservations()
    {
        for (int i = reservations.Count - 1; i >= 0; i--)
        {
            if (reservations[i].untilTime <= Time.time || reservations[i].owner == null)
                reservations.RemoveAt(i);
        }
    }

    public Vector3 GetSeparationOffset(BirdAI requester, float separationRadius, float separationStrength)
    {
        if (requester == null || separationRadius <= 0f || separationStrength <= 0f)
            return Vector3.zero;

        Vector3 offset = Vector3.zero;
        int count = 0;

        foreach (BirdAI bird in ownedBirds)
        {
            if (bird == null || bird == requester)
                continue;

            Vector3 away = requester.transform.position - bird.transform.position;
            away.y = 0f;
            float distance = away.magnitude;

            if (distance <= 0.001f || distance > separationRadius)
                continue;

            float intensity = 1f - distance / separationRadius;
            offset += away.normalized * intensity * separationStrength;
            count++;
        }

        if (count == 0)
            return Vector3.zero;

        return offset / count;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = new Color(0.25f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, zoneRadius);
    }
}
