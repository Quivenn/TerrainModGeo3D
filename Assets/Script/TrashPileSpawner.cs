using System.Collections.Generic;
using UnityEngine;

public class TrashPileSpawner : MonoBehaviour
{
    [System.Serializable]
    public class TrashPrefab
    {
        public string displayName;
        public GameObject prefab;

        [Header("Transform")]
        public Vector3 exactScale = Vector3.one;
        public Vector3 exactRotationEuler = Vector3.zero;

        [Header("Random Rotation")]
        public bool addRandomYaw = true;
        public Vector2 randomYawRange = new Vector2(0f, 360f);

        [Header("Placement")]
        [Min(0.01f)] public float footprintRadius = 0.35f;
        public float extraHeightOffset = 0.02f;

        [Header("Selection")]
        [Min(0f)] public float weight = 1f;
    }

    [System.Serializable]
    public class TrashSpawnPoint
    {
        public string displayName;
        public Transform point;

        [Header("Pile")]
        [Min(1)] public int minObjects = 3;
        [Min(1)] public int maxObjects = 6;

        [Header("Area around point")]
        [Min(0f)] public float pileRadius = 2f;
    }

    private struct PlacedObject
    {
        public Vector3 position;
        public float radius;

        public PlacedObject(Vector3 position, float radius)
        {
            this.position = position;
            this.radius = radius;
        }
    }

    [Header("Trash Prefabs")]
    public List<TrashPrefab> trashPrefabs = new List<TrashPrefab>();

    [Header("Possible Spawn Points")]
    public List<TrashSpawnPoint> spawnPoints = new List<TrashSpawnPoint>();

    [Header("Spawn Rules")]
    [Min(0)] public int numberOfPointsToUse = 2;
    public bool spawnOnStart = true;
    public bool useUniquePoints = true;

    [Header("Ground Projection")]
    public LayerMask groundMask;
    public float raycastStartHeight = 10f;
    public float raycastDistance = 30f;
    public bool requireGroundHit = true;
    public bool alignObjectBottomToGround = true;

    [Header("Separation")]
    public bool avoidOverlappingObjects = true;
    [Min(1)] public int placementAttemptsPerObject = 120;
    [Min(0f)] public float extraSeparation = 0.08f;

    [Header("Parenting")]
    public bool parentSpawnedObjectsToSpawner = true;

    [Header("Debug")]
    public bool showDebugLogs = true;
    public bool drawGizmos = true;
    public float gizmoRadius = 0.25f;

    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private readonly List<PlacedObject> placedObjects = new List<PlacedObject>();

    private void Start()
    {
        if (spawnOnStart)
        {
            SpawnTrashPiles();
        }
    }

    [ContextMenu("Spawn Trash Piles")]
    public void SpawnTrashPiles()
    {
        ClearSpawnedObjects();

        if (trashPrefabs == null || trashPrefabs.Count == 0)
        {
            Debug.LogWarning("[TrashPileSpawner] No trash prefabs assigned.", this);
            return;
        }

        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            Debug.LogWarning("[TrashPileSpawner] No spawn points assigned.", this);
            return;
        }

        List<TrashSpawnPoint> availablePoints = new List<TrashSpawnPoint>();

        foreach (TrashSpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint != null && spawnPoint.point != null)
            {
                availablePoints.Add(spawnPoint);
            }
        }

        if (availablePoints.Count == 0)
        {
            Debug.LogWarning("[TrashPileSpawner] All spawn points are empty.", this);
            return;
        }

        int finalPointCount = numberOfPointsToUse;

        if (useUniquePoints)
        {
            finalPointCount = Mathf.Min(numberOfPointsToUse, availablePoints.Count);
        }

        int totalSpawned = 0;

        for (int i = 0; i < finalPointCount; i++)
        {
            TrashSpawnPoint chosenPoint = ChooseSpawnPoint(availablePoints);

            if (chosenPoint == null || chosenPoint.point == null)
                continue;

            int minObjects = Mathf.Max(1, chosenPoint.minObjects);
            int maxObjects = Mathf.Max(minObjects, chosenPoint.maxObjects);
            int objectsToSpawn = Random.Range(minObjects, maxObjects + 1);

            for (int j = 0; j < objectsToSpawn; j++)
            {
                TrashPrefab chosenTrash = ChooseRandomTrashPrefab();

                if (chosenTrash == null || chosenTrash.prefab == null)
                    continue;

                if (!TryFindValidGroundPosition(chosenPoint, chosenTrash, out Vector3 groundPosition))
                {
                    if (showDebugLogs)
                    {
                        Debug.LogWarning(
                            $"[TrashPileSpawner] No valid position found for {chosenTrash.displayName}. Increase Pile Radius or reduce Footprint Radius.",
                            this
                        );
                    }

                    continue;
                }

                Quaternion spawnRotation = BuildRotation(chosenTrash);
                Transform parent = parentSpawnedObjectsToSpawner ? transform : null;

                GameObject spawnedObject = Instantiate(chosenTrash.prefab, groundPosition, spawnRotation, parent);
                spawnedObject.transform.localScale = chosenTrash.exactScale;

                if (alignObjectBottomToGround)
                {
                    AlignBottomToGround(spawnedObject, groundPosition.y + chosenTrash.extraHeightOffset);
                }
                else
                {
                    spawnedObject.transform.position += Vector3.up * chosenTrash.extraHeightOffset;
                }

                spawnedObject.name = $"{chosenTrash.prefab.name}_Spawned_{totalSpawned + 1:00}";

                PhotoObjectiveTarget objectiveTarget = spawnedObject.GetComponent<PhotoObjectiveTarget>();

                if (objectiveTarget == null)
                {
                    objectiveTarget = spawnedObject.AddComponent<PhotoObjectiveTarget>();
                }

                objectiveTarget.objectiveType = PhotoObjectiveType.Trash;
                objectiveTarget.displayName = chosenTrash.displayName;

                spawnedObjects.Add(spawnedObject);
                placedObjects.Add(new PlacedObject(spawnedObject.transform.position, chosenTrash.footprintRadius));

                totalSpawned++;
            }
        }

        if (showDebugLogs)
        {
            Debug.Log($"[TrashPileSpawner] Spawned {totalSpawned} trash object(s) on {finalPointCount} point(s).", this);
        }
    }

    private TrashSpawnPoint ChooseSpawnPoint(List<TrashSpawnPoint> availablePoints)
    {
        if (availablePoints == null || availablePoints.Count == 0)
            return null;

        int index = Random.Range(0, availablePoints.Count);
        TrashSpawnPoint chosenPoint = availablePoints[index];

        if (useUniquePoints)
        {
            availablePoints.RemoveAt(index);
        }

        return chosenPoint;
    }

    private TrashPrefab ChooseRandomTrashPrefab()
    {
        float totalWeight = 0f;

        foreach (TrashPrefab trashPrefab in trashPrefabs)
        {
            if (trashPrefab != null && trashPrefab.prefab != null && trashPrefab.weight > 0f)
            {
                totalWeight += trashPrefab.weight;
            }
        }

        if (totalWeight <= 0f)
            return null;

        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;

        foreach (TrashPrefab trashPrefab in trashPrefabs)
        {
            if (trashPrefab == null || trashPrefab.prefab == null || trashPrefab.weight <= 0f)
                continue;

            currentWeight += trashPrefab.weight;

            if (randomValue <= currentWeight)
            {
                return trashPrefab;
            }
        }

        return null;
    }

    private bool TryFindValidGroundPosition(TrashSpawnPoint spawnPoint, TrashPrefab trashPrefab, out Vector3 validPosition)
    {
        validPosition = Vector3.zero;

        for (int attempt = 0; attempt < placementAttemptsPerObject; attempt++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * spawnPoint.pileRadius;

            Vector3 candidatePosition = spawnPoint.point.position;
            candidatePosition += new Vector3(randomCircle.x, 0f, randomCircle.y);

            if (!TryProjectToGround(candidatePosition, out Vector3 groundPosition))
            {
                continue;
            }

            if (avoidOverlappingObjects && IsTooCloseToOtherObjects(groundPosition, trashPrefab.footprintRadius))
            {
                continue;
            }

            validPosition = groundPosition;
            return true;
        }

        return false;
    }

    private bool TryProjectToGround(Vector3 position, out Vector3 groundPosition)
    {
        Vector3 rayStart = position + Vector3.up * raycastStartHeight;

        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, raycastDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            groundPosition = hit.point;
            return true;
        }

        if (!requireGroundHit)
        {
            groundPosition = position;
            return true;
        }

        groundPosition = Vector3.zero;
        return false;
    }

    private bool IsTooCloseToOtherObjects(Vector3 position, float radius)
    {
        foreach (PlacedObject placedObject in placedObjects)
        {
            float requiredDistance = radius + placedObject.radius + extraSeparation;

            Vector2 a = new Vector2(position.x, position.z);
            Vector2 b = new Vector2(placedObject.position.x, placedObject.position.z);

            if (Vector2.Distance(a, b) < requiredDistance)
            {
                return true;
            }
        }

        return false;
    }

    private Quaternion BuildRotation(TrashPrefab trashPrefab)
    {
        Quaternion baseRotation = Quaternion.Euler(trashPrefab.exactRotationEuler);

        if (!trashPrefab.addRandomYaw)
        {
            return baseRotation;
        }

        float randomYaw = Random.Range(trashPrefab.randomYawRange.x, trashPrefab.randomYawRange.y);
        return Quaternion.Euler(0f, randomYaw, 0f) * baseRotation;
    }

    private void AlignBottomToGround(GameObject obj, float targetGroundY)
    {
        if (!TryGetObjectBounds(obj, out Bounds bounds))
        {
            obj.transform.position = new Vector3(obj.transform.position.x, targetGroundY, obj.transform.position.z);
            return;
        }

        float deltaY = targetGroundY - bounds.min.y;
        obj.transform.position += Vector3.up * deltaY;
    }

    private bool TryGetObjectBounds(GameObject obj, out Bounds bounds)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        Collider[] colliders = obj.GetComponentsInChildren<Collider>();

        if (colliders.Length > 0)
        {
            bounds = colliders[0].bounds;

            for (int i = 1; i < colliders.Length; i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }

            return true;
        }

        bounds = new Bounds(obj.transform.position, Vector3.one);
        return false;
    }

    [ContextMenu("Clear Spawned Objects")]
    public void ClearSpawnedObjects()
    {
        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
        {
            if (spawnedObjects[i] == null)
                continue;

            if (Application.isPlaying)
            {
                Destroy(spawnedObjects[i]);
            }
            else
            {
                DestroyImmediate(spawnedObjects[i]);
            }
        }

        spawnedObjects.Clear();
        placedObjects.Clear();
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || spawnPoints == null)
            return;

        foreach (TrashSpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint == null || spawnPoint.point == null)
                continue;

            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(spawnPoint.point.position, gizmoRadius);
            Gizmos.DrawLine(spawnPoint.point.position, spawnPoint.point.position + Vector3.up * 1.5f);

            Gizmos.color = new Color(1f, 0.8f, 0f, 0.25f);
            Gizmos.DrawWireSphere(spawnPoint.point.position, spawnPoint.pileRadius);
        }
    }
}