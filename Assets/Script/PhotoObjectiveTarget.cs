using UnityEngine;

public enum PhotoObjectiveType
{
    Bird,
    Trash
}

public class PhotoObjectiveTarget : MonoBehaviour
{
    public PhotoObjectiveType objectiveType = PhotoObjectiveType.Bird;
    public string displayName = "Objective Target";

    public Bounds GetBounds()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        Collider[] colliders = GetComponentsInChildren<Collider>();

        if (colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;

            for (int i = 1; i < colliders.Length; i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }

            return bounds;
        }

        return new Bounds(transform.position, Vector3.one);
    }
}