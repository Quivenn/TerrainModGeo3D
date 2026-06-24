using UnityEngine;

public enum BirdHabitatAreaType
{
    Spawn,
    Food,
    Nest,
    Perch,
    Rest
}

public class BirdHabitatArea : MonoBehaviour
{
    [Header("Habitat Area")]
    public BirdHabitatAreaType areaType = BirdHabitatAreaType.Food;
    [Min(0.1f)] public float radius = 4f;
    [Min(0f)] public float weight = 1f;
    public bool isEnabled = true;

    [Header("Debug")]
    public bool drawGizmo = true;

    public Vector3 GetRandomPosition()
    {
        Vector2 circle = Random.insideUnitCircle * radius;
        return transform.position + new Vector3(circle.x, 0f, circle.y);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmo)
            return;

        Gizmos.color = GetColor(areaType);
        Gizmos.DrawWireSphere(transform.position, radius);
    }

    public static Color GetColor(BirdHabitatAreaType type)
    {
        switch (type)
        {
            case BirdHabitatAreaType.Spawn:
                return new Color(0.2f, 1f, 1f, 0.75f);
            case BirdHabitatAreaType.Food:
                return new Color(0.2f, 1f, 0.2f, 0.75f);
            case BirdHabitatAreaType.Nest:
                return new Color(0.55f, 0.3f, 0.15f, 0.75f);
            case BirdHabitatAreaType.Perch:
                return new Color(1f, 0.8f, 0.25f, 0.75f);
            case BirdHabitatAreaType.Rest:
                return new Color(0.7f, 0.7f, 1f, 0.75f);
            default:
                return Color.white;
        }
    }
}
