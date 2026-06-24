using System.Collections.Generic;
using UnityEngine;

public class RandomAssetSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject[] assetPrefabs;
    [SerializeField] private int spawnCount = 20;
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private bool parentSpawnedAssetsToSpawner = true;

    [Header("Spawn Area")]
    [SerializeField] private float spawnRadius = 25f;
    [SerializeField] private int maxAttemptsPerAsset = 40;

    [Header("Ground Validation")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private LayerMask waterMask = 0;
    [SerializeField] private LayerMask blockingMask = 0;
    [SerializeField] private float groundRayHeight = 30f;
    [SerializeField] private float groundRayDistance = 80f;
    [SerializeField] private float groundOffsetY = 0f;
    [SerializeField] private float maxGroundSlopeAngle = 35f;
    [SerializeField] private float waterAvoidanceRadius = 0.25f;
    [SerializeField] private float obstacleClearanceRadius = 0.25f;

    [Header("Distribution")]
    [SerializeField] private float minimumDistanceBetweenAssets = 1.5f;
    [SerializeField] private bool randomYaw = true;
    [SerializeField] private bool alignToGroundNormal = false;
    [SerializeField] private Vector2 randomScaleRange = new Vector2(0.85f, 1.15f);

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;
    [SerializeField] private bool drawGizmos = true;

    private readonly List<GameObject> spawnedAssets = new List<GameObject>();
    private readonly List<Vector3> spawnedPositions = new List<Vector3>();

    private void Start()
    {
        if (spawnOnStart)
            SpawnAssets();
    }

    [ContextMenu("Spawn Assets")]
    public void SpawnAssets()
    {
        if (assetPrefabs == null || assetPrefabs.Length == 0)
        {
            Debug.LogWarning($"[RandomAssetSpawner] {name} has no asset prefabs assigned.");
            return;
        }

        ClearSpawnedAssets();

        for (int i = 0; i < spawnCount; i++)
        {
            if (!TryFindSpawnTransform(out Vector3 position, out Quaternion rotation))
            {
                if (showDebugLogs)
                    Debug.LogWarning($"[RandomAssetSpawner] {name} failed to place asset {i + 1}.");

                continue;
            }

            GameObject prefab = GetRandomPrefab();

            if (prefab == null)
                continue;

            Transform parent = parentSpawnedAssetsToSpawner ? transform : null;
            GameObject instance = Instantiate(prefab, position, rotation, parent);
            instance.name = $"{prefab.name}_{spawnedAssets.Count + 1:00}";

            float scale = Random.Range(randomScaleRange.x, randomScaleRange.y);
            instance.transform.localScale *= scale;

            spawnedAssets.Add(instance);
            spawnedPositions.Add(position);
        }

        if (showDebugLogs)
            Debug.Log($"[RandomAssetSpawner] {name} spawned {spawnedAssets.Count}/{spawnCount} assets.");
    }

    [ContextMenu("Clear Spawned Assets")]
    public void ClearSpawnedAssets()
    {
        for (int i = spawnedAssets.Count - 1; i >= 0; i--)
        {
            GameObject asset = spawnedAssets[i];

            if (asset == null)
                continue;

            if (Application.isPlaying)
                Destroy(asset);
            else
                DestroyImmediate(asset);
        }

        spawnedAssets.Clear();
        spawnedPositions.Clear();
    }

    private GameObject GetRandomPrefab()
    {
        if (assetPrefabs == null || assetPrefabs.Length == 0)
            return null;

        for (int safety = 0; safety < 20; safety++)
        {
            GameObject prefab = assetPrefabs[Random.Range(0, assetPrefabs.Length)];

            if (prefab != null)
                return prefab;
        }

        return null;
    }

    private bool TryFindSpawnTransform(out Vector3 position, out Quaternion rotation)
    {
        for (int attempt = 0; attempt < maxAttemptsPerAsset; attempt++)
        {
            Vector2 circle = Random.insideUnitCircle * spawnRadius;
            Vector3 candidate = transform.position + new Vector3(circle.x, 0f, circle.y);

            if (!TryProjectToGround(candidate, out RaycastHit hit))
                continue;

            position = hit.point + Vector3.up * groundOffsetY;

            if (!IsPositionValid(position))
                continue;

            if (alignToGroundNormal)
                rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
            else
                rotation = Quaternion.identity;

            if (randomYaw)
                rotation *= Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            return true;
        }

        position = default;
        rotation = Quaternion.identity;
        return false;
    }

    private bool TryProjectToGround(Vector3 point, out RaycastHit bestHit)
    {
        Vector3 origin = new Vector3(point.x, point.y + groundRayHeight, point.z);
        float maxDistance = groundRayHeight + groundRayDistance;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, maxDistance, groundMask, QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
        {
            bestHit = default;
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            float slope = Vector3.Angle(hit.normal, Vector3.up);

            if (slope > maxGroundSlopeAngle)
                continue;

            bestHit = hit;
            return true;
        }

        bestHit = default;
        return false;
    }

    private bool IsPositionValid(Vector3 position)
    {
        if (waterMask.value != 0 && Physics.CheckSphere(position, waterAvoidanceRadius, waterMask, QueryTriggerInteraction.Ignore))
            return false;

        if (blockingMask.value != 0 && Physics.CheckSphere(position, obstacleClearanceRadius, blockingMask, QueryTriggerInteraction.Ignore))
            return false;

        foreach (Vector3 otherPosition in spawnedPositions)
        {
            if (Vector3.Distance(position, otherPosition) < minimumDistanceBetweenAssets)
                return false;
        }

        return true;
    }

    private void OnValidate()
    {
        spawnCount = Mathf.Max(0, spawnCount);
        spawnRadius = Mathf.Max(0.1f, spawnRadius);
        maxAttemptsPerAsset = Mathf.Max(1, maxAttemptsPerAsset);
        groundRayHeight = Mathf.Max(0.1f, groundRayHeight);
        groundRayDistance = Mathf.Max(0.1f, groundRayDistance);
        maxGroundSlopeAngle = Mathf.Clamp(maxGroundSlopeAngle, 0f, 89f);
        minimumDistanceBetweenAssets = Mathf.Max(0f, minimumDistanceBetweenAssets);
        randomScaleRange.x = Mathf.Max(0.01f, randomScaleRange.x);
        randomScaleRange.y = Mathf.Max(randomScaleRange.x, randomScaleRange.y);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = new Color(0.35f, 1f, 0.55f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
    }
}
