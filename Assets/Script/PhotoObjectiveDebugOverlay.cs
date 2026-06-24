using UnityEngine;

public class PhotoObjectiveDebugOverlay : MonoBehaviour
{
    [Header("Camera")]
    public Camera photoCamera;

    [Header("Input")]
    public KeyCode toggleDebugKey = KeyCode.O;
    public bool debugVisible = false;

    [Header("Display")]
    public bool showOnlyVisibleOnScreen = true;
    public float maxDisplayDistance = 120f;
    public int labelFontSize = 18;
    public int lineThickness = 2;

    [Header("Colors")]
    public Color birdColor = Color.green;
    public Color trashColor = new Color(1f, 0.65f, 0f, 1f);
    public Color hiddenColor = Color.red;

    private Texture2D whiteTexture;

    private void Awake()
    {
        if (photoCamera == null)
        {
            photoCamera = Camera.main;
        }

        whiteTexture = new Texture2D(1, 1);
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleDebugKey))
        {
            debugVisible = !debugVisible;
            Debug.Log(debugVisible
                ? "[PhotoObjectiveDebugOverlay] Debug objectifs photo activé."
                : "[PhotoObjectiveDebugOverlay] Debug objectifs photo désactivé.");
        }
    }

    private void OnGUI()
    {
        if (!debugVisible || photoCamera == null)
            return;

        PhotoObjectiveTarget[] targets = FindObjectsByType<PhotoObjectiveTarget>(FindObjectsInactive.Exclude);

        foreach (PhotoObjectiveTarget target in targets)
        {
            if (target == null)
                continue;

            DrawTarget(target);
        }
    }

    private void DrawTarget(PhotoObjectiveTarget target)
    {
        Bounds bounds = target.GetBounds();

        float distance = Vector3.Distance(photoCamera.transform.position, bounds.center);

        if (distance > maxDisplayDistance)
            return;

        Vector3 centerViewport = photoCamera.WorldToViewportPoint(bounds.center);

        if (centerViewport.z <= 0f)
            return;

        Rect screenRect = GetScreenRectFromBounds(bounds, out bool visibleOnScreen);

        if (showOnlyVisibleOnScreen && !visibleOnScreen)
            return;

        Color color = target.objectiveType == PhotoObjectiveType.Bird ? birdColor : trashColor;

        string typeName = target.objectiveType == PhotoObjectiveType.Bird ? "OISEAU" : "DECHET";
        string label = $"{typeName} - {target.displayName} - {distance:0.0}m";

        DrawRect(screenRect, color, lineThickness);

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = labelFontSize;
        style.normal.textColor = color;
        style.fontStyle = FontStyle.Bold;

        Rect labelRect = new Rect(screenRect.x, screenRect.y - 24f, 400f, 24f);
        GUI.Label(labelRect, label, style);
    }

    private Rect GetScreenRectFromBounds(Bounds bounds, out bool visibleOnScreen)
    {
        Vector3[] corners = GetBoundsCorners(bounds);

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        visibleOnScreen = false;

        foreach (Vector3 corner in corners)
        {
            Vector3 screenPoint = photoCamera.WorldToScreenPoint(corner);

            if (screenPoint.z <= 0f)
                continue;

            float x = screenPoint.x;
            float y = Screen.height - screenPoint.y;

            minX = Mathf.Min(minX, x);
            minY = Mathf.Min(minY, y);
            maxX = Mathf.Max(maxX, x);
            maxY = Mathf.Max(maxY, y);

            if (x >= 0f && x <= Screen.width && y >= 0f && y <= Screen.height)
            {
                visibleOnScreen = true;
            }
        }

        if (minX == float.MaxValue)
        {
            return new Rect(0f, 0f, 0f, 0f);
        }

        minX = Mathf.Clamp(minX, 0f, Screen.width);
        maxX = Mathf.Clamp(maxX, 0f, Screen.width);
        minY = Mathf.Clamp(minY, 0f, Screen.height);
        maxY = Mathf.Clamp(maxY, 0f, Screen.height);

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private Vector3[] GetBoundsCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        return new Vector3[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z)
        };
    }

    private void DrawRect(Rect rect, Color color, int thickness)
    {
        DrawLine(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
        DrawLine(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
        DrawLine(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
        DrawLine(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
    }

    private void DrawLine(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, whiteTexture);
        GUI.color = previousColor;
    }
}