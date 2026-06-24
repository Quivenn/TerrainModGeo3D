using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(10000)]
public class Appareil_Photo : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private Camera photoCamera;

    [Header("First Person Photo Mode")]
    [SerializeField] private bool useFirstPersonPhotoMode = true;
    [SerializeField] private Transform firstPersonCameraAnchor;
    [SerializeField] private bool copyAnchorRotation = false;

    [Header("Player Visibility")]
    [SerializeField] private bool hidePlayerVisualsInPhotoMode = true;
    [SerializeField] private Transform playerVisualRoot;

    private Renderer[] cachedPlayerRenderers;

    [Header("Input")]
    [SerializeField] private Key toggleCameraKey = Key.P;
    [SerializeField] private Key debugKey = Key.O;
    [SerializeField] private Key takePhotoKey = Key.Space;

    [Header("Camera Mode")]
    [SerializeField] private float photoModeFov = 35f;
    [SerializeField] private int photoWidth = 1920;
    [SerializeField] private int photoHeight = 1080;

    [Header("Zoom / Comfort")]
    [SerializeField] private bool enableZoom = true;
    [SerializeField] private float minZoomFov = 15f;
    [SerializeField] private float maxZoomFov = 60f;
    [SerializeField] private float zoomStepFov = 5f;
    [SerializeField] private float keyboardZoomSpeedFov = 35f;
    [SerializeField] private float zoomSmoothSpeed = 12f;
    [SerializeField] private Key zoomInKey = Key.UpArrow;
    [SerializeField] private Key zoomOutKey = Key.DownArrow;
    [SerializeField] private Key zoomResetKey = Key.R;
    [SerializeField] private bool resetZoomWhenEnteringPhotoMode = true;
    [SerializeField] private bool showZoomInOverlay = true;
    [SerializeField] private bool showTargetNameInOverlay = false;

    [Header("Shutter Comfort")]
    [SerializeField] private float shutterCooldown = 0.45f;
    [SerializeField] private float shutterFlashDuration = 0.10f;
    [SerializeField] private float photoSavedMessageDuration = 0.85f;
    [SerializeField] private bool drawScoreOnSavedPhoto = false;

    [Header("Mission Integration")]
    [SerializeField] private PhotoHuntGameManager photoHuntGameManager;
    [SerializeField] private bool validateMissionObjectives = true;
    [SerializeField] private bool saveScreenshots = false;
    [SerializeField] private bool enableLegacyTargetScoring = false;

    [Header("Size Scoring")]
    [SerializeField] private float minUsefulScreenArea = 0.005f;
    [SerializeField] private float idealScreenArea = 0.12f;
    [SerializeField] private float tooBigScreenArea = 0.45f;

    [Header("Frame / Cropping")]
    [SerializeField] private float cropPenaltyPower = 1.5f;

    [Header("Angle Scoring")]
    [SerializeField] private float fullBackViewScore = 0.35f;
    [SerializeField] private float extremeVerticalViewScore = 0.45f;

    [Header("Occlusion")]
    [SerializeField] private bool checkOcclusion = false;
    [SerializeField] private LayerMask occlusionMask = ~0;

    [Header("Aim / Target Selection")]
    [SerializeField] private float aimSelectionPower = 2.5f;
    [SerializeField] private float minimumAimScoreForSelection = 0.10f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;
    [SerializeField] private bool debugMode;
    [SerializeField] private bool highlightDebugTarget = true;
    [SerializeField] private bool drawDebugRays = true;
    [SerializeField] private Color debugBoxColor = new Color(1f, 0.85f, 0.1f, 1f);
    [SerializeField] private Color debugVisiblePointColor = new Color(0.2f, 1f, 0.2f, 1f);
    [SerializeField] private Color debugBlockedPointColor = new Color(1f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color debugPanelColor = new Color(0f, 0f, 0f, 0.72f);
    [SerializeField] private Color debugHighlightColor = new Color(1f, 0.85f, 0.1f, 0.85f);

    [Header("Saving")]
    [SerializeField] private string photoFolderName = "Photos";

    private bool isPhotoMode;
    private float defaultFov;
    private float currentPhotoFov;
    private float targetPhotoFov;
    private float nextAllowedPhotoTime;
    private float shutterFlashTimer;
    private float photoSavedMessageTimer;
    private Texture2D whiteTexture;

    private ScoreResult currentBestResult;
    private DebugData currentDebugData;
    private Renderer[] highlightedRenderers;
    private MaterialPropertyBlock highlightBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        if (photoCamera == null)
            photoCamera = Camera.main;

        if (photoCamera != null)
            defaultFov = photoCamera.fieldOfView;

        targetPhotoFov = Mathf.Clamp(photoModeFov, minZoomFov, maxZoomFov);
        currentPhotoFov = targetPhotoFov;

        whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply();

        highlightBlock = new MaterialPropertyBlock();
        currentBestResult = ScoreResult.Invalid;

        if (photoHuntGameManager == null)
        {
            photoHuntGameManager = FindFirstObjectByType<PhotoHuntGameManager>();
        }
    }

    private void Update()
    {
        if (photoCamera == null)
            return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null && keyboard[toggleCameraKey].wasPressedThisFrame)
        {
            SetPhotoMode(!isPhotoMode);
        }

        if (!isPhotoMode)
            return;

        if (enableLegacyTargetScoring && keyboard != null && keyboard[debugKey].wasPressedThisFrame)
        {
            SetDebugMode(!debugMode);
        }

        HandleZoomInput(keyboard, mouse);
        ApplySmoothZoom();
        UpdateComfortTimers();

        if (enableLegacyTargetScoring)
        {
            RefreshPhotoEvaluation();
        }
        else
        {
            currentBestResult = ScoreResult.Invalid;
            currentDebugData = null;
            ClearDebugHighlight();
        }

        bool mousePhoto = mouse != null && mouse.leftButton.wasPressedThisFrame;
        bool keyboardPhoto = keyboard != null && keyboard[takePhotoKey].wasPressedThisFrame;

        if ((mousePhoto || keyboardPhoto) && Time.time >= nextAllowedPhotoTime)
        {
            TakeGameplayPhoto();
            nextAllowedPhotoTime = Time.time + shutterCooldown;
            shutterFlashTimer = shutterFlashDuration;
            photoSavedMessageTimer = photoSavedMessageDuration;
        }

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetPhotoMode(false);
        }
    }

    private void LateUpdate()
    {
        if (!isPhotoMode)
            return;

        if (!useFirstPersonPhotoMode)
            return;

        if (photoCamera == null || firstPersonCameraAnchor == null)
            return;

        photoCamera.transform.position = firstPersonCameraAnchor.position;

        if (copyAnchorRotation)
        {
            photoCamera.transform.rotation = firstPersonCameraAnchor.rotation;
        }
    }

    private void SetPlayerVisualsVisible(bool visible)
    {
        if (!hidePlayerVisualsInPhotoMode)
            return;

        if (playerVisualRoot == null)
            return;

        if (cachedPlayerRenderers == null || cachedPlayerRenderers.Length == 0)
        {
            cachedPlayerRenderers = playerVisualRoot.GetComponentsInChildren<Renderer>(true);
        }

        foreach (Renderer renderer in cachedPlayerRenderers)
        {
            if (renderer != null)
            {
                renderer.enabled = visible;
            }
        }
    }

    private void HandleZoomInput(Keyboard keyboard, Mouse mouse)
    {
        if (!enableZoom)
            return;

        if (mouse != null)
        {
            float scrollY = mouse.scroll.ReadValue().y;

            if (Mathf.Abs(scrollY) > 0.01f)
            {
                // Sur beaucoup de souris, un cran vaut environ 120. On garde seulement le signe
                // pour un zoom régulier et prévisible.
                targetPhotoFov -= Mathf.Sign(scrollY) * zoomStepFov;
            }
        }

        if (keyboard != null)
        {
            if (keyboard[zoomInKey].isPressed)
                targetPhotoFov -= keyboardZoomSpeedFov * Time.deltaTime;

            if (keyboard[zoomOutKey].isPressed)
                targetPhotoFov += keyboardZoomSpeedFov * Time.deltaTime;

            if (keyboard[zoomResetKey].wasPressedThisFrame)
                targetPhotoFov = Mathf.Clamp(photoModeFov, minZoomFov, maxZoomFov);
        }

        targetPhotoFov = Mathf.Clamp(targetPhotoFov, minZoomFov, maxZoomFov);
    }

    private void ApplySmoothZoom()
    {
        if (!enableZoom)
        {
            photoCamera.fieldOfView = photoModeFov;
            return;
        }

        float smoothing = 1f - Mathf.Exp(-zoomSmoothSpeed * Time.deltaTime);
        currentPhotoFov = Mathf.Lerp(currentPhotoFov, targetPhotoFov, smoothing);
        photoCamera.fieldOfView = currentPhotoFov;
    }

    private void UpdateComfortTimers()
    {
        if (shutterFlashTimer > 0f)
            shutterFlashTimer -= Time.deltaTime;

        if (photoSavedMessageTimer > 0f)
            photoSavedMessageTimer -= Time.deltaTime;
    }

    private void OnDestroy()
    {
        ClearDebugHighlight();
    }

    private void SetPhotoMode(bool enabled)
    {
        isPhotoMode = enabled;

        SetPlayerVisualsVisible(!isPhotoMode);

        if (photoCamera != null)
        {
            if (isPhotoMode)
            {
                if (resetZoomWhenEnteringPhotoMode)
                {
                    targetPhotoFov = Mathf.Clamp(photoModeFov, minZoomFov, maxZoomFov);
                    currentPhotoFov = targetPhotoFov;
                }

                photoCamera.fieldOfView = currentPhotoFov;
            }
            else
            {
                photoCamera.fieldOfView = defaultFov;
            }
        }

        if (!isPhotoMode)
        {
            currentBestResult = ScoreResult.Invalid;
            currentDebugData = null;
            shutterFlashTimer = 0f;
            photoSavedMessageTimer = 0f;
            ClearDebugHighlight();
        }

        Debug.Log(isPhotoMode ? "Mode appareil photo activé." : "Mode appareil photo désactivé.");
    }

    private void SetDebugMode(bool enabled)
    {
        debugMode = enabled;

        if (!debugMode)
            ClearDebugHighlight();

        if (showDebugLogs)
            Debug.Log(debugMode ? "Debug photo activé." : "Debug photo désactivé.");
    }

    private void RefreshPhotoEvaluation()
    {
        currentBestResult = GetBestTargetScore(true);
        currentDebugData = currentBestResult.debugData;

        if (debugMode && currentBestResult.isValid)
        {
            if (highlightDebugTarget)
                ApplyDebugHighlight(currentBestResult.target);
            else
                ClearDebugHighlight();

            if (drawDebugRays && currentDebugData != null)
                DrawOcclusionRays(currentDebugData);
        }
        else
        {
            ClearDebugHighlight();
        }
    }

    private void TakeGameplayPhoto()
    {
        if (validateMissionObjectives && photoHuntGameManager != null)
        {
            photoHuntGameManager.ValidatePhotoFromCamera();

            if (showDebugLogs)
                Debug.Log("[Appareil_Photo] Photo gameplay validée.");

            return;
        }

        if (saveScreenshots)
        {
            TakePhotoAndSave();
            return;
        }

        if (showDebugLogs)
            Debug.Log("[Appareil_Photo] Photo prise, mais aucune action configurée.");
    }

    private void TakePhotoAndSave()
    {
        ScoreResult result = currentBestResult.isValid ? currentBestResult : GetBestTargetScore(false);

        SaveCameraImage(result);

        if (!result.isValid)
        {
            Debug.Log("Photo prise : aucune cible valide dans le cadre.");
            return;
        }

        if (showDebugLogs)
        {
            Debug.Log(
                $"Photo prise : {result.targetName}\n" +
                $"Score final : {result.finalScore:F1} / {result.maxPoints:F0}\n" +
                $"Taille : {result.sizeScore:F2}\n" +
                $"Cadrage : {result.frameScore:F2}\n" +
                $"Angle horizontal : {result.horizontalAngleScore:F2}\n" +
                $"Angle vertical : {result.verticalAngleScore:F2}\n" +
                $"Visibilité : {result.visibilityScore:F2}\n" +
                $"Centrage : {result.aimScore:F2}\n" +
                $"Surface écran : {result.screenArea:P1}"
            );
        }
    }

    private ScoreResult GetBestTargetScore(bool buildDebugData)
    {
        Target[] targets = FindObjectsByType<Target>(FindObjectsInactive.Exclude);

        if (targets.Length == 0)
        {
            if (showDebugLogs && isPhotoMode)
                Debug.LogWarning("Photo : aucune cible avec le composant Target trouvée dans la scène.");

            return ScoreResult.Invalid;
        }

        ScoreResult bestResult = ScoreResult.Invalid;

        foreach (Target target in targets)
        {
            if (!target.CompareTag("Bird"))
                continue;

            ScoreResult result = EvaluateTarget(target, buildDebugData);

            if (!result.isValid)
                continue;

            // Une cible doit être suffisamment proche du viseur pour être sélectionnable.
            // Cela évite qu'un gros objet sur le côté gagne contre la cible réellement visée.
            if (result.aimScore < minimumAimScoreForSelection)
                continue;

            // Le centrage sert à choisir la cible. Le score final sert seulement à départager
            // deux cibles ayant un centrage proche.
            if (!bestResult.isValid || result.selectionScore > bestResult.selectionScore)
                bestResult = result;
        }

        return bestResult;
    }

    private void SaveCameraImage(ScoreResult result)
    {
        // On sauvegarde en dehors de Assets pour éviter que Unity importe chaque photo.
        // Dossier final : TonProjet/Photos
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string folderPath = Path.Combine(projectRoot, photoFolderName);

        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        string targetName = result.isValid ? SanitizeFileName(result.targetName) : "NoTarget";
        string scoreText = result.isValid ? Mathf.RoundToInt(result.finalScore).ToString("000") : "000";
        string dateText = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // Format : NomDeLaCible - Note - Date.png
        // Exemple : Robin Test - 085 - 2026-06-15_14-30-05.png
        string fileName = $"{targetName} - {scoreText} - {dateText}.png";
        string filePath = Path.Combine(folderPath, fileName);

        RenderTexture previousTargetTexture = photoCamera.targetTexture;
        RenderTexture previousActiveTexture = RenderTexture.active;

        RenderTexture renderTexture = new RenderTexture(photoWidth, photoHeight, 24);
        Texture2D screenshot = new Texture2D(photoWidth, photoHeight, TextureFormat.RGB24, false);

        photoCamera.targetTexture = renderTexture;
        RenderTexture.active = renderTexture;

        photoCamera.Render();

        screenshot.ReadPixels(new Rect(0, 0, photoWidth, photoHeight), 0, 0);
        screenshot.Apply();

        if (drawScoreOnSavedPhoto)
        {
            DrawPhotoScoreOnTexture(screenshot, result);
            screenshot.Apply();
        }

        byte[] bytes = screenshot.EncodeToPNG();
        File.WriteAllBytes(filePath, bytes);

        photoCamera.targetTexture = previousTargetTexture;
        RenderTexture.active = previousActiveTexture;

        Destroy(renderTexture);
        Destroy(screenshot);

        Debug.Log($"Photo enregistrée : {filePath}");
    }

    private string SanitizeFileName(string fileName)
    {
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidChar, '_');

        return fileName;
    }

    private string GetTargetDisplayName(Target target)
    {
        if (target == null)
            return "NoTarget";

        // Si un vrai nom a été renseigné dans Target Name, on l'utilise.
        // Si la valeur est encore "Test Bird", on préfère le nom du mesh/renderer.
        if (!string.IsNullOrWhiteSpace(target.targetName) && target.targetName != "Test Bird")
            return target.targetName;

        if (target.targetRenderer != null)
            return target.targetRenderer.gameObject.name;

        if (target.targetCollider != null)
            return target.targetCollider.gameObject.name;

        return target.gameObject.name;
    }

    private ScoreResult EvaluateTarget(Target target, bool buildDebugData)
    {
        if (photoCamera == null || target == null)
            return ScoreResult.Invalid;

        string displayName = GetTargetDisplayName(target);
        Bounds bounds = target.GetBounds();

        if (!TryGetViewportRect(bounds, out Rect fullViewportRect))
            return ScoreResult.Invalid;

        Rect screenRect = new Rect(0f, 0f, 1f, 1f);
        Rect visibleViewportRect = IntersectRects(fullViewportRect, screenRect);

        if (visibleViewportRect.width <= 0f || visibleViewportRect.height <= 0f)
            return ScoreResult.Invalid;

        float fullArea = Mathf.Max(0.0001f, fullViewportRect.width * fullViewportRect.height);
        float visibleArea = visibleViewportRect.width * visibleViewportRect.height;

        float screenArea = visibleArea;
        float visibleRatioInFrame = Mathf.Clamp01(visibleArea / fullArea);

        Vector3[] visibilityPoints = GetVisibilitySamplePoints(bounds);
        VisibilityData visibilityData = checkOcclusion
            ? EvaluateVisibility(target, visibilityPoints, buildDebugData)
            : BuildNoOcclusionVisibilityData(visibilityPoints, buildDebugData);

        float sizeScore = EvaluateSizeScore(screenArea);
        float frameScore = Mathf.Pow(visibleRatioInFrame, cropPenaltyPower);
        float horizontalAngleScore = EvaluateHorizontalAngle(target);
        float verticalAngleScore = EvaluateVerticalAngle(target);
        float visibilityScore = visibilityData.score;
        float aimScore = EvaluateAimScore(visibleViewportRect);

        float finalQuality =
            sizeScore *
            frameScore *
            horizontalAngleScore *
            verticalAngleScore *
            visibilityScore;

        float finalScore = Mathf.Clamp01(finalQuality) * target.maxPoints;
        float selectionScore = aimScore * 1000f + finalScore;

        DebugData debugData = null;

        if (buildDebugData)
        {
            debugData = new DebugData
            {
                target = target,
                targetName = displayName,
                viewportRect = fullViewportRect,
                visibleViewportRect = visibleViewportRect,
                visibilityPoints = visibilityPoints,
                pointViewportPositions = visibilityData.pointViewportPositions,
                pointInFrame = visibilityData.pointInFrame,
                pointVisible = visibilityData.pointVisible,
                sizeScore = sizeScore,
                frameScore = frameScore,
                horizontalAngleScore = horizontalAngleScore,
                verticalAngleScore = verticalAngleScore,
                visibilityScore = visibilityScore,
                aimScore = aimScore,
                selectionScore = selectionScore,
                screenArea = screenArea,
                finalScore = finalScore,
                maxPoints = target.maxPoints
            };
        }

        return new ScoreResult
        {
            isValid = true,
            target = target,
            targetName = displayName,
            maxPoints = target.maxPoints,
            finalScore = finalScore,
            screenArea = screenArea,
            sizeScore = sizeScore,
            frameScore = frameScore,
            horizontalAngleScore = horizontalAngleScore,
            verticalAngleScore = verticalAngleScore,
            visibilityScore = visibilityScore,
            aimScore = aimScore,
            selectionScore = selectionScore,
            debugData = debugData
        };
    }

    private bool TryGetViewportRect(Bounds bounds, out Rect viewportRect)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        Vector3[] points =
        {
            center,
            center + new Vector3(-extents.x, -extents.y, -extents.z),
            center + new Vector3(-extents.x, -extents.y,  extents.z),
            center + new Vector3(-extents.x,  extents.y, -extents.z),
            center + new Vector3(-extents.x,  extents.y,  extents.z),
            center + new Vector3( extents.x, -extents.y, -extents.z),
            center + new Vector3( extents.x, -extents.y,  extents.z),
            center + new Vector3( extents.x,  extents.y, -extents.z),
            center + new Vector3( extents.x,  extents.y,  extents.z)
        };

        bool hasPointInFront = false;

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        foreach (Vector3 point in points)
        {
            Vector3 viewportPoint = photoCamera.WorldToViewportPoint(point);

            if (viewportPoint.z <= 0f)
                continue;

            hasPointInFront = true;

            minX = Mathf.Min(minX, viewportPoint.x);
            minY = Mathf.Min(minY, viewportPoint.y);
            maxX = Mathf.Max(maxX, viewportPoint.x);
            maxY = Mathf.Max(maxY, viewportPoint.y);
        }

        if (!hasPointInFront)
        {
            viewportRect = default;
            return false;
        }

        viewportRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private Vector3[] GetVisibilitySamplePoints(Bounds bounds)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        return new[]
        {
            center,
            center + Vector3.up * extents.y * 0.75f,
            center - Vector3.up * extents.y * 0.75f,
            center + Vector3.right * extents.x * 0.75f,
            center - Vector3.right * extents.x * 0.75f,
            center + Vector3.forward * extents.z * 0.75f,
            center - Vector3.forward * extents.z * 0.75f
        };
    }

    private float EvaluateSizeScore(float screenArea)
    {
        if (screenArea <= 0f)
            return 0f;

        if (screenArea < minUsefulScreenArea)
            return Mathf.InverseLerp(0f, minUsefulScreenArea, screenArea) * 0.35f;

        if (screenArea <= idealScreenArea)
        {
            float t = Mathf.InverseLerp(minUsefulScreenArea, idealScreenArea, screenArea);
            return Mathf.Lerp(0.35f, 1f, t);
        }

        if (screenArea <= tooBigScreenArea)
        {
            float t = Mathf.InverseLerp(idealScreenArea, tooBigScreenArea, screenArea);
            return Mathf.Lerp(1f, 0.65f, t);
        }

        float tooCloseT = Mathf.InverseLerp(tooBigScreenArea, 1f, screenArea);
        return Mathf.Lerp(0.65f, 0.1f, tooCloseT);
    }

    private float EvaluateAimScore(Rect visibleViewportRect)
    {
        Vector2 targetCenter = visibleViewportRect.center;
        Vector2 screenCenter = new Vector2(0.5f, 0.5f);

        float distance = Vector2.Distance(targetCenter, screenCenter);

        // Distance approximative du centre de l'écran jusqu'à un coin en coordonnées viewport.
        float maxDistance = 0.7071f;

        float normalizedDistance = Mathf.Clamp01(distance / maxDistance);
        float aimScore = 1f - normalizedDistance;

        // Plus la puissance est élevée, plus la sélection favorise fortement le centre du viseur.
        return Mathf.Pow(aimScore, aimSelectionPower);
    }

    private float EvaluateHorizontalAngle(Target target)
    {
        Transform orientation = target.OrientationRoot;

        Vector3 directionToCamera = (photoCamera.transform.position - orientation.position).normalized;
        float frontDot = Vector3.Dot(orientation.forward, directionToCamera);

        // frontDot proche de 1  : caméra devant la cible.
        // frontDot proche de 0  : caméra sur le côté.
        // frontDot proche de -1 : caméra derrière la cible.
        // Face et côté = bon. Dos = pénalité.

        if (frontDot >= -0.15f)
            return 1f;

        float backAmount = Mathf.InverseLerp(-0.15f, -1f, frontDot);
        return Mathf.Lerp(1f, fullBackViewScore, backAmount);
    }

    private float EvaluateVerticalAngle(Target target)
    {
        Transform orientation = target.OrientationRoot;

        Vector3 directionToCamera = (photoCamera.transform.position - orientation.position).normalized;
        float verticalAmount = Mathf.Abs(Vector3.Dot(orientation.up, directionToCamera));

        if (verticalAmount <= 0.35f)
            return 1f;

        float extremeAmount = Mathf.InverseLerp(0.35f, 1f, verticalAmount);
        return Mathf.Lerp(1f, extremeVerticalViewScore, extremeAmount);
    }

    private VisibilityData BuildNoOcclusionVisibilityData(Vector3[] samplePoints, bool buildDebugData)
    {
        Vector3[] viewportPositions = buildDebugData ? new Vector3[samplePoints.Length] : null;
        bool[] pointInFrame = buildDebugData ? new bool[samplePoints.Length] : null;
        bool[] pointVisible = buildDebugData ? new bool[samplePoints.Length] : null;

        int validSamples = 0;

        for (int i = 0; i < samplePoints.Length; i++)
        {
            Vector3 viewportPoint = photoCamera.WorldToViewportPoint(samplePoints[i]);
            bool inFrame = viewportPoint.z > 0f && viewportPoint.x >= 0f && viewportPoint.x <= 1f && viewportPoint.y >= 0f && viewportPoint.y <= 1f;

            if (inFrame)
                validSamples++;

            if (buildDebugData)
            {
                viewportPositions[i] = viewportPoint;
                pointInFrame[i] = inFrame;
                pointVisible[i] = inFrame;
            }
        }

        return new VisibilityData
        {
            score = Mathf.Clamp01((float)validSamples / samplePoints.Length),
            pointViewportPositions = viewportPositions,
            pointInFrame = pointInFrame,
            pointVisible = pointVisible
        };
    }

    private VisibilityData EvaluateVisibility(Target target, Vector3[] samplePoints, bool buildDebugData)
    {
        Vector3[] viewportPositions = buildDebugData ? new Vector3[samplePoints.Length] : null;
        bool[] pointInFrame = buildDebugData ? new bool[samplePoints.Length] : null;
        bool[] pointVisible = buildDebugData ? new bool[samplePoints.Length] : null;

        if (target.targetCollider == null)
        {
            VisibilityData noOcclusion = BuildNoOcclusionVisibilityData(samplePoints, buildDebugData);
            noOcclusion.score = 1f;
            return noOcclusion;
        }

        int visibleSamples = 0;
        int validSamples = 0;

        for (int i = 0; i < samplePoints.Length; i++)
        {
            Vector3 point = samplePoints[i];
            Vector3 viewportPoint = photoCamera.WorldToViewportPoint(point);

            bool inFrame = viewportPoint.z > 0f && viewportPoint.x >= 0f && viewportPoint.x <= 1f && viewportPoint.y >= 0f && viewportPoint.y <= 1f;
            bool visible = false;

            if (inFrame)
            {
                validSamples++;

                Vector3 origin = photoCamera.transform.position;
                Vector3 direction = point - origin;
                float distance = direction.magnitude;

                if (distance > 0.001f)
                {
                    direction /= distance;

                    if (Physics.Raycast(origin, direction, out RaycastHit hit, distance + 0.1f, occlusionMask, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.transform == target.transform || hit.transform.IsChildOf(target.transform))
                        {
                            visible = true;
                        }
                        else
                        {
                            visible = false;
                        }
                    }
                    else
                    {
                        visible = true;
                    }
                }
            }

            if (visible)
                visibleSamples++;

            if (buildDebugData)
            {
                viewportPositions[i] = viewportPoint;
                pointInFrame[i] = inFrame;
                pointVisible[i] = visible;
            }
        }

        return new VisibilityData
        {
            score = validSamples == 0 ? 0f : Mathf.Clamp01((float)visibleSamples / validSamples),
            pointViewportPositions = viewportPositions,
            pointInFrame = pointInFrame,
            pointVisible = pointVisible
        };
    }

    private Rect IntersectRects(Rect a, Rect b)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMax = Mathf.Min(a.yMax, b.yMax);

        if (xMax <= xMin || yMax <= yMin)
            return new Rect(0f, 0f, 0f, 0f);

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private void ApplyDebugHighlight(Target target)
    {
        if (target == null)
        {
            ClearDebugHighlight();
            return;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();

        if (highlightedRenderers == renderers)
            return;

        ClearDebugHighlight();
        highlightedRenderers = renderers;

        foreach (Renderer renderer in highlightedRenderers)
        {
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(highlightBlock);
            highlightBlock.SetColor(BaseColorId, debugHighlightColor);
            highlightBlock.SetColor(ColorId, debugHighlightColor);
            highlightBlock.SetColor(EmissionColorId, debugHighlightColor * 0.4f);
            renderer.SetPropertyBlock(highlightBlock);
        }
    }

    private void ClearDebugHighlight()
    {
        if (highlightedRenderers == null)
            return;

        foreach (Renderer renderer in highlightedRenderers)
        {
            if (renderer != null)
                renderer.SetPropertyBlock(null);
        }

        highlightedRenderers = null;
    }

    private void DrawOcclusionRays(DebugData data)
    {
        if (data == null || data.visibilityPoints == null)
            return;

        Vector3 origin = photoCamera.transform.position;

        for (int i = 0; i < data.visibilityPoints.Length; i++)
        {
            bool visible = data.pointVisible != null && i < data.pointVisible.Length && data.pointVisible[i];
            Color color = visible ? debugVisiblePointColor : debugBlockedPointColor;
            Debug.DrawLine(origin, data.visibilityPoints[i], color);
        }
    }

    private void OnGUI()
    {
        if (!isPhotoMode)
            return;

        DrawCameraOverlay();

        if (!enableLegacyTargetScoring || !debugMode)
            return;

        DrawDebugOverlay();
    }

    private void DrawCameraOverlay()
    {
        float border = 35f;
        float lineThickness = 2f;
        float cornerSize = 45f;

        GUI.color = new Color(0f, 0f, 0f, 0.35f);

        GUI.DrawTexture(new Rect(0, 0, Screen.width, border), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0, Screen.height - border, Screen.width, border), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0, 0, border, Screen.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(Screen.width - border, 0, border, Screen.height), Texture2D.whiteTexture);

        GUI.color = Color.white;

        DrawCorner(border, border, cornerSize, lineThickness, true, true);
        DrawCorner(Screen.width - border, border, cornerSize, lineThickness, false, true);
        DrawCorner(border, Screen.height - border, cornerSize, lineThickness, true, false);
        DrawCorner(Screen.width - border, Screen.height - border, cornerSize, lineThickness, false, false);

        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;

        GUI.DrawTexture(new Rect(centerX - 12f, centerY, 24f, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX, centerY - 12f, 1f, 24f), Texture2D.whiteTexture);

        float zoomMultiplier = Mathf.Max(0.01f, photoModeFov / Mathf.Max(0.01f, currentPhotoFov));

        string topLeftText = "PHOTO";

        if (showZoomInOverlay)
            topLeftText += $"   ZOOM x{zoomMultiplier:0.0}";

        if (showTargetNameInOverlay && currentBestResult.isValid)
            topLeftText += $"   {currentBestResult.targetName}";

        GUI.Label(new Rect(55, 35, 900, 30), topLeftText);

        GUI.Label(
            new Rect(55, Screen.height - 32f, 1000f, 24f),
            $"Clic gauche / {takePhotoKey} : valider l'observation   |   Molette ou ↑/↓ : zoom   |   R : reset zoom   |   O : debug objectifs   |   P/Echap : quitter"
        );

        if (photoSavedMessageTimer > 0f)
        {
            float alpha = Mathf.Clamp01(photoSavedMessageTimer / Mathf.Max(0.001f, photoSavedMessageDuration));
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(Screen.width * 0.5f - 80f, Screen.height - 72f, 220f, 24f), "OBSERVATION VALIDÉE");
            GUI.color = Color.white;
        }

        if (shutterFlashTimer > 0f)
        {
            float alpha = Mathf.Clamp01(shutterFlashTimer / Mathf.Max(0.001f, shutterFlashDuration)) * 0.35f;
            DrawFilledGUIRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(1f, 1f, 1f, alpha));
        }
    }

    private void DrawDebugOverlay()
    {
        if (currentDebugData == null || !currentBestResult.isValid)
        {
            DrawFilledGUIRect(new Rect(55, 75, 360, 55), debugPanelColor);
            GUI.color = Color.white;
            GUI.Label(new Rect(70, 88, 330, 25), "PHOTO DEBUG : aucune cible détectée");
            return;
        }

        Rect targetRect = ViewportRectToGUIRect(currentDebugData.visibleViewportRect);
        DrawGUIRectBorder(targetRect, 3f, debugBoxColor);

        DrawDebugPoints(currentDebugData);
        DrawDebugPanel(currentDebugData);
    }

    private void DrawDebugPoints(DebugData data)
    {
        if (data.pointViewportPositions == null)
            return;

        for (int i = 0; i < data.pointViewportPositions.Length; i++)
        {
            Vector3 viewportPoint = data.pointViewportPositions[i];

            if (viewportPoint.z <= 0f)
                continue;

            Vector2 guiPoint = ViewportPointToGUI(viewportPoint);
            bool visible = data.pointVisible != null && i < data.pointVisible.Length && data.pointVisible[i];
            bool inFrame = data.pointInFrame != null && i < data.pointInFrame.Length && data.pointInFrame[i];

            Color color = visible ? debugVisiblePointColor : debugBlockedPointColor;

            if (!inFrame)
                color = new Color(1f, 0.45f, 0.05f, 1f);

            DrawFilledGUIRect(new Rect(guiPoint.x - 5f, guiPoint.y - 5f, 10f, 10f), color);
        }
    }

    private void DrawDebugPanel(DebugData data)
    {
        Rect panel = new Rect(55f, 75f, 420f, 285f);
        DrawFilledGUIRect(panel, debugPanelColor);

        GUI.color = Color.white;
        GUI.Label(new Rect(70f, 85f, 360f, 22f), $"PHOTO DEBUG - Target : {data.targetName}");
        GUI.Label(new Rect(70f, 107f, 360f, 22f), $"Final : {data.finalScore:0.0} / {data.maxPoints:0}");
        GUI.Label(new Rect(70f, 129f, 360f, 22f), $"Screen area : {data.screenArea:P1}");

        DrawScoreBar("Centrage", data.aimScore, 70f, 158f);
        DrawScoreBar("Taille", data.sizeScore, 70f, 182f);
        DrawScoreBar("Cadrage", data.frameScore, 70f, 206f);
        DrawScoreBar("Angle H", data.horizontalAngleScore, 70f, 230f);
        DrawScoreBar("Angle V", data.verticalAngleScore, 70f, 254f);
        DrawScoreBar("Visibilité", data.visibilityScore, 70f, 278f);

        GUI.color = Color.white;
        GUI.Label(new Rect(70f, 314f, 360f, 22f), "Points verts = visibles | rouges = masqués/hors cible");
    }

    private void DrawScoreBar(string label, float value, float x, float y)
    {
        value = Mathf.Clamp01(value);

        GUI.color = Color.white;
        GUI.Label(new Rect(x, y - 3f, 85f, 22f), label);

        Rect background = new Rect(x + 90f, y, 180f, 12f);
        Rect fill = new Rect(background.x, background.y, background.width * value, background.height);

        DrawFilledGUIRect(background, new Color(1f, 1f, 1f, 0.18f));
        DrawFilledGUIRect(fill, Color.Lerp(new Color(1f, 0.25f, 0.25f, 1f), new Color(0.25f, 1f, 0.25f, 1f), value));

        GUI.color = Color.white;
        GUI.Label(new Rect(x + 280f, y - 3f, 80f, 22f), value.ToString("0.00"));
    }

    private Rect ViewportRectToGUIRect(Rect viewportRect)
    {
        float xMin = viewportRect.xMin * Screen.width;
        float xMax = viewportRect.xMax * Screen.width;
        float yMin = (1f - viewportRect.yMax) * Screen.height;
        float yMax = (1f - viewportRect.yMin) * Screen.height;

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private Vector2 ViewportPointToGUI(Vector3 viewportPoint)
    {
        return new Vector2(viewportPoint.x * Screen.width, (1f - viewportPoint.y) * Screen.height);
    }

    private void DrawCorner(float x, float y, float size, float thickness, bool right, bool down)
    {
        float horizontalX = right ? x : x - size;
        float verticalY = down ? y : y - size;

        GUI.DrawTexture(new Rect(horizontalX, y, size, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(x, verticalY, thickness, size), Texture2D.whiteTexture);
    }

    private void DrawFilledGUIRect(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void DrawGUIRectBorder(Rect rect, float thickness, Color color)
    {
        DrawFilledGUIRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
        DrawFilledGUIRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
        DrawFilledGUIRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
        DrawFilledGUIRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
    }

    private void DrawPhotoScoreOnTexture(Texture2D texture, ScoreResult result)
    {
        string text = result.isValid
            ? $"SCORE {Mathf.RoundToInt(result.finalScore):000}/{Mathf.RoundToInt(result.maxPoints):000}"
            : "NO TARGET";

        int scale = Mathf.Max(3, texture.width / 640);

        int x = 30;
        int y = 30;

        int boxWidth = Mathf.Min(texture.width - 60, text.Length * 6 * scale + 40);
        int boxHeight = 7 * scale + 40;

        DrawFilledRect(texture, x - 15, y - 15, boxWidth, boxHeight, new Color(0f, 0f, 0f, 0.55f));
        DrawPixelText(texture, text, x, y, scale, Color.white);
    }

    private void DrawPixelText(Texture2D texture, string text, int x, int y, int scale, Color color)
    {
        int cursorX = x;
        string upperText = text.ToUpperInvariant();

        foreach (char c in upperText)
        {
            string[] pattern = GetCharacterPattern(c);

            if (pattern == null)
            {
                cursorX += 4 * scale;
                continue;
            }

            for (int row = 0; row < pattern.Length; row++)
            {
                for (int col = 0; col < pattern[row].Length; col++)
                {
                    if (pattern[row][col] == '1')
                    {
                        DrawFilledRect(
                            texture,
                            cursorX + col * scale,
                            y + row * scale,
                            scale,
                            scale,
                            color
                        );
                    }
                }
            }

            cursorX += (pattern[0].Length + 1) * scale;
        }
    }

    private void DrawFilledRect(Texture2D texture, int x, int y, int width, int height, Color color)
    {
        for (int px = x; px < x + width; px++)
        {
            for (int py = y; py < y + height; py++)
                SetPixelTopLeft(texture, px, py, color);
        }
    }

    private void SetPixelTopLeft(Texture2D texture, int x, int yFromTop, Color color)
    {
        int y = texture.height - 1 - yFromTop;

        if (x < 0 || x >= texture.width || y < 0 || y >= texture.height)
            return;

        Color current = texture.GetPixel(x, y);
        Color opaqueColor = new Color(color.r, color.g, color.b, 1f);
        Color blendedColor = Color.Lerp(current, opaqueColor, color.a);

        texture.SetPixel(x, y, blendedColor);
    }

    private string[] GetCharacterPattern(char c)
    {
        switch (c)
        {
            case '0':
                return new[]
                {
                    "11111",
                    "10001",
                    "10011",
                    "10101",
                    "11001",
                    "10001",
                    "11111"
                };

            case '1':
                return new[]
                {
                    "00100",
                    "01100",
                    "00100",
                    "00100",
                    "00100",
                    "00100",
                    "11111"
                };

            case '2':
                return new[]
                {
                    "11111",
                    "00001",
                    "00001",
                    "11111",
                    "10000",
                    "10000",
                    "11111"
                };

            case '3':
                return new[]
                {
                    "11111",
                    "00001",
                    "00001",
                    "11111",
                    "00001",
                    "00001",
                    "11111"
                };

            case '4':
                return new[]
                {
                    "10001",
                    "10001",
                    "10001",
                    "11111",
                    "00001",
                    "00001",
                    "00001"
                };

            case '5':
                return new[]
                {
                    "11111",
                    "10000",
                    "10000",
                    "11111",
                    "00001",
                    "00001",
                    "11111"
                };

            case '6':
                return new[]
                {
                    "11111",
                    "10000",
                    "10000",
                    "11111",
                    "10001",
                    "10001",
                    "11111"
                };

            case '7':
                return new[]
                {
                    "11111",
                    "00001",
                    "00010",
                    "00100",
                    "01000",
                    "01000",
                    "01000"
                };

            case '8':
                return new[]
                {
                    "11111",
                    "10001",
                    "10001",
                    "11111",
                    "10001",
                    "10001",
                    "11111"
                };

            case '9':
                return new[]
                {
                    "11111",
                    "10001",
                    "10001",
                    "11111",
                    "00001",
                    "00001",
                    "11111"
                };

            case 'S':
                return new[]
                {
                    "11111",
                    "10000",
                    "10000",
                    "11111",
                    "00001",
                    "00001",
                    "11111"
                };

            case 'C':
                return new[]
                {
                    "11111",
                    "10000",
                    "10000",
                    "10000",
                    "10000",
                    "10000",
                    "11111"
                };

            case 'O':
                return new[]
                {
                    "11111",
                    "10001",
                    "10001",
                    "10001",
                    "10001",
                    "10001",
                    "11111"
                };

            case 'R':
                return new[]
                {
                    "11110",
                    "10001",
                    "10001",
                    "11110",
                    "10100",
                    "10010",
                    "10001"
                };

            case 'E':
                return new[]
                {
                    "11111",
                    "10000",
                    "10000",
                    "11110",
                    "10000",
                    "10000",
                    "11111"
                };

            case 'N':
                return new[]
                {
                    "10001",
                    "11001",
                    "10101",
                    "10011",
                    "10001",
                    "10001",
                    "10001"
                };

            case 'T':
                return new[]
                {
                    "11111",
                    "00100",
                    "00100",
                    "00100",
                    "00100",
                    "00100",
                    "00100"
                };

            case 'A':
                return new[]
                {
                    "01110",
                    "10001",
                    "10001",
                    "11111",
                    "10001",
                    "10001",
                    "10001"
                };

            case 'G':
                return new[]
                {
                    "11111",
                    "10000",
                    "10000",
                    "10111",
                    "10001",
                    "10001",
                    "11111"
                };

            case '/':
                return new[]
                {
                    "00001",
                    "00010",
                    "00010",
                    "00100",
                    "01000",
                    "01000",
                    "10000"
                };

            case ' ':
                return new[]
                {
                    "000",
                    "000",
                    "000",
                    "000",
                    "000",
                    "000",
                    "000"
                };

            default:
                return null;
        }
    }

    private struct VisibilityData
    {
        public float score;
        public Vector3[] pointViewportPositions;
        public bool[] pointInFrame;
        public bool[] pointVisible;
    }

    private class DebugData
    {
        public Target target;
        public string targetName;
        public Rect viewportRect;
        public Rect visibleViewportRect;
        public Vector3[] visibilityPoints;
        public Vector3[] pointViewportPositions;
        public bool[] pointInFrame;
        public bool[] pointVisible;
        public float screenArea;
        public float sizeScore;
        public float frameScore;
        public float horizontalAngleScore;
        public float verticalAngleScore;
        public float visibilityScore;
        public float aimScore;
        public float selectionScore;
        public float finalScore;
        public float maxPoints;
    }

    private struct ScoreResult
    {
        public bool isValid;
        public Target target;
        public string targetName;
        public float maxPoints;
        public float finalScore;

        public float screenArea;
        public float sizeScore;
        public float frameScore;
        public float horizontalAngleScore;
        public float verticalAngleScore;
        public float visibilityScore;
        public float aimScore;
        public float selectionScore;

        public DebugData debugData;

        public static ScoreResult Invalid => new ScoreResult
        {
            isValid = false,
            finalScore = -1f,
            debugData = null
        };
    }
}
