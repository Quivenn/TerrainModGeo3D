using UnityEngine;

[CreateAssetMenu(menuName = "Birds/Bird Species Profile", fileName = "BirdSpeciesProfile")]
public class BirdSpeciesProfile : ScriptableObject
{
    [Header("Identity")]
    public string speciesId = "robin";
    public string displayName = "Robin";

    [Header("Prefab")]
    public GameObject birdPrefab;

    [Header("Default Zone Settings")]
    [Min(1)] public int defaultBirdCount = 3;
    [Min(0.1f)] public float minimumSpawnDistanceBetweenBirds = 1.5f;
    [Min(0f)] public float minimumSpawnDistanceFromPlayer = 8f;

    [Header("Notes")]
    [TextArea] public string notes;
}
