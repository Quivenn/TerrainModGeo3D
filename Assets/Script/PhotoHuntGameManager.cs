using UnityEngine;
using UnityEngine.UI;

public class PhotoHuntGameManager : MonoBehaviour
{
    [Header("Game Start")]
    public KeyCode startGameKey = KeyCode.G;
    public KeyCode photoKey = KeyCode.F;

    [Header("Camera")]
    public Camera photoCamera;
    public float maxPhotoDistance = 100f;
    public float minScreenSize = 0.004f;
    public float maxDistanceFromScreenCenter = 0.65f;

    [Header("Spawners")]
    public BirdZone[] birdZones;
    public TrashPileSpawner[] trashSpawners;
    public bool forceDisableTrashAutoStart = true;

    [Header("UI")]
    public bool autoCreateUI = true;
    public GameObject uiRoot;
    public Text birdStatusText;
    public Text trashStatusText;
    public Text timerText;
    public Text messageText;

    [Header("Debug")]
    public bool showDebugLogs = true;

    private bool gameStarted;
    private bool gameFinished;
    private bool birdFound;
    private bool trashFound;
    private float timer;
    private int photoCount;

    private void Awake()
    {
        if (photoCamera == null)
        {
            photoCamera = Camera.main;
        }

        if (forceDisableTrashAutoStart && trashSpawners != null)
        {
            foreach (TrashPileSpawner trashSpawner in trashSpawners)
            {
                if (trashSpawner != null)
                {
                    trashSpawner.spawnOnStart = false;
                }
            }
        }

        if (autoCreateUI && uiRoot == null)
        {
            CreateRuntimeUI();
        }

        if (uiRoot != null)
        {
            uiRoot.SetActive(false);
        }

        UpdateUI("Appuyez sur G pour commencer.");
    }

    private void Update()
    {
        if (!gameStarted)
        {
            if (Input.GetKeyDown(startGameKey))
            {
                StartPhotoHunt();
            }

            return;
        }

        if (gameFinished)
            return;

        timer += Time.deltaTime;
        UpdateUI();

        if (Input.GetKeyDown(photoKey))
        {
            EvaluateGameplayPhoto();
        }
    }

    [ContextMenu("Start Photo Hunt")]
    public void StartPhotoHunt()
    {
        gameStarted = true;
        gameFinished = false;
        birdFound = false;
        trashFound = false;
        timer = 0f;
        photoCount = 0;

        if (uiRoot != null)
        {
            uiRoot.SetActive(true);
        }

        SpawnGameObjects();
        EnsureObjectiveTargetsAfterSpawn();

        UpdateUI("Objectif : photographier un oiseau et un déchet.");

        if (showDebugLogs)
        {
            Debug.Log("[PhotoHuntGameManager] Photo hunt started.", this);
        }
    }

    public void ValidatePhotoFromCamera()
    {
        if (!gameStarted)
        {
            if (showDebugLogs)
                Debug.Log("[PhotoHuntGameManager] Photo ignorée : la mission n'est pas lancée.", this);

            return;
        }

        if (gameFinished)
            return;

        EvaluateGameplayPhoto();
    }

    private void SpawnGameObjects()
    {
        if (birdZones != null)
        {
            foreach (BirdZone birdZone in birdZones)
            {
                if (birdZone == null)
                    continue;

                birdZone.CollectChildAreas();
                birdZone.SpawnPopulation();
            }
        }

        if (trashSpawners != null)
        {
            foreach (TrashPileSpawner trashSpawner in trashSpawners)
            {
                if (trashSpawner == null)
                    continue;

                trashSpawner.SpawnTrashPiles();
            }
        }
    }

    private void EnsureObjectiveTargetsAfterSpawn()
    {
        BirdAI[] birds = FindObjectsByType<BirdAI>(FindObjectsInactive.Exclude);

        foreach (BirdAI bird in birds)
        {
            if (bird == null)
                continue;

            PhotoObjectiveTarget target = bird.GetComponent<PhotoObjectiveTarget>();

            if (target == null)
            {
                target = bird.gameObject.AddComponent<PhotoObjectiveTarget>();
            }

            target.objectiveType = PhotoObjectiveType.Bird;
            target.displayName = "Robin";
        }

        PhotoObjectiveTarget[] allTargets = FindObjectsByType<PhotoObjectiveTarget>(FindObjectsInactive.Exclude);

        if (showDebugLogs)
        {
            int birdCount = 0;
            int trashCount = 0;

            foreach (PhotoObjectiveTarget target in allTargets)
            {
                if (target.objectiveType == PhotoObjectiveType.Bird)
                    birdCount++;
                else if (target.objectiveType == PhotoObjectiveType.Trash)
                    trashCount++;
            }

            Debug.Log($"[PhotoHuntGameManager] Objectives ready. Birds={birdCount}, Trash={trashCount}", this);
        }
    }

    private void EvaluateGameplayPhoto()
    {
        photoCount++;

        if (photoCamera == null)
        {
            UpdateUI("Aucune caméra photo assignée.");
            return;
        }

        PhotoObjectiveTarget[] targets = FindObjectsByType<PhotoObjectiveTarget>(FindObjectsInactive.Exclude);

        bool birdSeenThisPhoto = false;
        bool trashSeenThisPhoto = false;

        foreach (PhotoObjectiveTarget target in targets)
        {
            if (target == null)
                continue;

            if (!IsTargetVisibleInPhoto(target))
                continue;

            if (target.objectiveType == PhotoObjectiveType.Bird)
            {
                birdSeenThisPhoto = true;
            }
            else if (target.objectiveType == PhotoObjectiveType.Trash)
            {
                trashSeenThisPhoto = true;
            }
        }

        string feedback = $"Photo {photoCount} : ";

        if (birdSeenThisPhoto)
        {
            birdFound = true;
            feedback += "oiseau validé ";
        }

        if (trashSeenThisPhoto)
        {
            trashFound = true;
            feedback += "déchet validé ";
        }

        if (!birdSeenThisPhoto && !trashSeenThisPhoto)
        {
            feedback += "aucun objectif visible.";
        }

        if (birdFound && trashFound)
        {
            FinishGame();
            return;
        }

        UpdateUI(feedback);

        if (showDebugLogs)
        {
            Debug.Log($"[PhotoHuntGameManager] {feedback}", this);
        }
    }

    private bool IsTargetVisibleInPhoto(PhotoObjectiveTarget target)
    {
        Bounds bounds = target.GetBounds();
        Vector3 center = bounds.center;

        float distance = Vector3.Distance(photoCamera.transform.position, center);

        if (distance > maxPhotoDistance)
            return false;

        Vector3 viewportCenter = photoCamera.WorldToViewportPoint(center);

        if (viewportCenter.z <= 0f)
            return false;

        if (viewportCenter.x < 0f || viewportCenter.x > 1f || viewportCenter.y < 0f || viewportCenter.y > 1f)
            return false;

        float distanceFromCenter = Vector2.Distance(
            new Vector2(viewportCenter.x, viewportCenter.y),
            new Vector2(0.5f, 0.5f)
        );

        if (distanceFromCenter > maxDistanceFromScreenCenter)
            return false;

        Vector3[] corners = GetBoundsCorners(bounds);

        float minX = 1f;
        float minY = 1f;
        float maxX = 0f;
        float maxY = 0f;
        bool atLeastOneCornerVisible = false;

        foreach (Vector3 corner in corners)
        {
            Vector3 viewport = photoCamera.WorldToViewportPoint(corner);

            if (viewport.z <= 0f)
                continue;

            minX = Mathf.Min(minX, viewport.x);
            minY = Mathf.Min(minY, viewport.y);
            maxX = Mathf.Max(maxX, viewport.x);
            maxY = Mathf.Max(maxY, viewport.y);

            if (viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f)
            {
                atLeastOneCornerVisible = true;
            }
        }

        if (!atLeastOneCornerVisible)
            return false;

        float screenWidth = maxX - minX;
        float screenHeight = maxY - minY;
        float screenSize = Mathf.Max(screenWidth, screenHeight);

        return screenSize >= minScreenSize;
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

    private void FinishGame()
    {
        gameFinished = true;
        UpdateUI("Objectifs terminés !");

        if (showDebugLogs)
        {
            Debug.Log($"[PhotoHuntGameManager] Game finished in {FormatTime(timer)}.", this);
        }
    }

    private void UpdateUI(string message = null)
    {
        if (birdStatusText != null)
        {
            birdStatusText.text = birdFound ? "X Oiseau photographié" : "○ Oiseau à photographier";
            birdStatusText.color = birdFound ? new Color(0.55f, 1f, 0.55f) : new Color(0.9f, 0.9f, 0.9f);
            birdStatusText.fontStyle = birdFound ? FontStyle.Bold : FontStyle.Normal;
        }

        if (trashStatusText != null)
        {
            trashStatusText.text = trashFound ? "X Déchet photographié" : "○ Déchet à photographier";
            trashStatusText.color = trashFound ? new Color(0.55f, 1f, 0.55f) : new Color(0.9f, 0.9f, 0.9f);
            trashStatusText.fontStyle = trashFound ? FontStyle.Bold : FontStyle.Normal;
        }

        if (timerText != null)
        {
            timerText.text = "Temps : " + FormatTime(timer);
            timerText.color = Color.white;
            timerText.fontStyle = FontStyle.Bold;
        }

        if (messageText != null)
        {
            if (!string.IsNullOrEmpty(message))
            {
                messageText.text = message;
            }
            else if (!gameStarted)
            {
                messageText.text = "Appuyez sur G pour commencer";
            }
            else if (gameFinished)
            {
                messageText.text = "Mission terminée";
            }
            else
            {
                messageText.text = "F : photo  |  O : debug objectifs";
            }

            messageText.color = gameFinished ? new Color(0.55f, 1f, 0.55f) : new Color(0.8f, 0.8f, 0.8f);
        }
    }

    private string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        return $"{minutes:00}:{seconds:00}";
    }

    private void CreateRuntimeUI()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        if (font == null)
        {
            font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        }

        GameObject canvasObject = new GameObject("PhotoHuntUI");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler canvasScaler = canvasObject.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject panelObject = new GameObject("MissionPanel");
        panelObject.transform.SetParent(canvasObject.transform, false);

        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.02f, 0.025f, 0.025f, 0.78f);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-80f, -35f);
        panelRect.sizeDelta = new Vector2(390f, 215f);

        Text titleText = CreateText("TitleText", panelObject.transform, font, new Vector2(22f, -16f), 19, TextAnchor.UpperLeft);
        titleText.text = "OBJECTIFS";
        titleText.fontStyle = FontStyle.Bold;
        titleText.color = new Color(0.82f, 0.92f, 0.78f);

        CreateSeparator("SeparatorTop", panelObject.transform, new Vector2(22f, -45f), new Vector2(345f, 1f), new Color(1f, 1f, 1f, 0.18f));

        birdStatusText = CreateText("BirdStatusText", panelObject.transform, font, new Vector2(22f, -60f), 19, TextAnchor.UpperLeft);
        trashStatusText = CreateText("TrashStatusText", panelObject.transform, font, new Vector2(22f, -90f), 19, TextAnchor.UpperLeft);

        CreateSeparator("SeparatorMiddle", panelObject.transform, new Vector2(22f, -123f), new Vector2(345f, 1f), new Color(1f, 1f, 1f, 0.12f));

        timerText = CreateText("TimerText", panelObject.transform, font, new Vector2(22f, -138f), 18, TextAnchor.UpperLeft);
        timerText.text = "Temps : 00:00";

        messageText = CreateText("MessageText", panelObject.transform, font, new Vector2(22f, -168f), 15, TextAnchor.UpperLeft);
        uiRoot = canvasObject;
    }

    private Text CreateText(string objectName, Transform parent, Font font, Vector2 anchoredPosition, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        Text text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform rect = text.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(386f, 28f);

        return text;
    }

    private void CreateSeparator(string objectName, Transform parent, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        GameObject separatorObject = new GameObject(objectName);
        separatorObject.transform.SetParent(parent, false);

        Image image = separatorObject.AddComponent<Image>();
        image.color = color;

        RectTransform rect = separatorObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }
}