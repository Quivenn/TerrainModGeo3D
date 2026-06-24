using UnityEngine;

public class Target : MonoBehaviour
{
    [Header("Identity")]
    public string targetName = "Test Bird";
    public float maxPoints = 100f;

    [Header("References")]
    public Renderer targetRenderer;
    public Collider targetCollider;
    public Transform orientationRoot;

    public Transform OrientationRoot => orientationRoot != null ? orientationRoot : transform;

    private void Reset()
    {
        targetRenderer = GetComponentInChildren<Renderer>();
        targetCollider = GetComponentInChildren<Collider>();
        orientationRoot = transform;
    }

    private void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<Renderer>();

        if (targetCollider == null)
            targetCollider = GetComponentInChildren<Collider>();

        if (orientationRoot == null)
            orientationRoot = transform;
    }

    public Bounds GetBounds()
    {
        if (targetCollider != null)
            return targetCollider.bounds;

        if (targetRenderer != null)
            return targetRenderer.bounds;

        return new Bounds(transform.position, Vector3.one);
    }
}
