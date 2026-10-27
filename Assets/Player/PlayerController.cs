using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private CharacterController characterController;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float rotationSpeed = 720f;
    [SerializeField] private float forwardMarkerLength = 0.65f;
    [SerializeField] private float forwardMarkerRadius = 0.12f;
    [SerializeField] private Color forwardMarkerColor = new Color(1f, 0.25f, 0.05f);
    [SerializeField] private float clickStopDistance = 0.2f;
    [SerializeField] private GameObject moveTargetPrefab;

    private Camera playerCamera;
    private Terrain _terrain;
    private float _verticalVelocity;
    private Vector3 _targetPosition;
    private bool _hasMoveTarget;
    private GameObject _moveTargetInstance;

    private GameServerLoginUI _gameServerLogin;
    private ThirdPersonCameraController _cameraController;
    private float _nextNetworkSyncTime;
    private float _nextNetworkHeartbeatTime;
    private Vector3 _lastSentNetworkPosition;
    private Quaternion _lastSentNetworkRotation;
    private bool _hasSentNetworkState;

    private const float NetworkStateSyncInterval = 0.1f;
    private const float NetworkStateHeartbeatInterval = 1f;
    private const float NetworkPositionThreshold = 0.05f;
    private const float NetworkRotationThresholdDegrees = 1f;

    private void Awake()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }
        ConfigureCharacterCollider();

        // CinemachineCamera is virtual. Camera.main is the real camera that renders the view.
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            playerCamera = FindFirstObjectByType<Camera>();
        }

        _cameraController = FindFirstObjectByType<ThirdPersonCameraController>();

        if (playerCamera == null)
        {
            Debug.LogError("PlayerController: no rendering Camera found. Ensure the scene has a Camera (normally tagged MainCamera).");
        }

        if (moveTargetPrefab != null)
        {
            _moveTargetInstance = Instantiate(moveTargetPrefab);
            _moveTargetInstance.name = "MoveTargetMarker";
            _moveTargetInstance.SetActive(false);
        }
        else
        {
            Debug.LogWarning(
                "MoveTarget prefab не назначен в PlayerController!"
            );
        }
        
        EnsureTerrainCollider();
        SnapCharacterAboveTerrainIfBuried();
        CreateForwardMarker();
    }

    private void ConfigureCharacterCollider()
    {
        if (characterController == null)
        {
            return;
        }

        Vector3 scale = transform.lossyScale;
        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(scale.y));
        float scaleZ = Mathf.Max(0.0001f, Mathf.Abs(scale.z));

        // Keep the world-space collider at height 2 m and diameter 0.5 m even if
        // the imported character root uses a non-unit scale. Its bottom is at transform.position.
        characterController.height = 2f / scaleY;
        characterController.radius = 0.25f / Mathf.Max(scaleX, scaleZ);
        characterController.center = new Vector3(0f, 1f / scaleY, 0f);
        characterController.skinWidth = Mathf.Min(0.025f, characterController.radius * 0.1f);
        characterController.minMoveDistance = 0f;
        characterController.stepOffset = Mathf.Min(0.3f / scaleY, characterController.height * 0.4f);

        Debug.Log(
            "PlayerController: collider configured; worldHeight=2, worldDiameter=0.5, " +
            "localHeight=" + characterController.height +
            ", localRadius=" + characterController.radius +
            ", localCenter=" + characterController.center);
    }

    private void EnsureTerrainCollider()
    {
        _terrain = Terrain.activeTerrain;
        if (_terrain == null)
        {
            Terrain[] terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            if (terrains.Length > 0)
            {
                _terrain = terrains[0];
            }
        }

        if (_terrain == null)
        {
            Debug.LogError("PlayerController: no Terrain found in the loaded scene.");
            return;
        }

        if (_terrain.terrainData == null)
        {
            Debug.LogError("PlayerController: Terrain '" + _terrain.name + "' has no TerrainData assigned.");
            return;
        }

        // Keep rendering and physics enabled in Play Mode. This also repairs a
        // missing or stale TerrainCollider reference without editing the scene asset.
        _terrain.enabled = true;
        TerrainCollider terrainCollider = _terrain.GetComponent<TerrainCollider>();
        if (terrainCollider == null)
        {
            terrainCollider = _terrain.gameObject.AddComponent<TerrainCollider>();
        }

        if (terrainCollider.terrainData != _terrain.terrainData)
        {
            terrainCollider.terrainData = _terrain.terrainData;
        }

        terrainCollider.enabled = true;
        Physics.SyncTransforms();

        Debug.Log(
            "PlayerController: Terrain ready; name=" + _terrain.name +
            ", data=" + _terrain.terrainData.name +
            ", collider=" + terrainCollider.enabled +
            ", size=" + _terrain.terrainData.size);
    }


    private void SnapCharacterAboveTerrainIfBuried()
    {
        if (_terrain == null || _terrain.terrainData == null || characterController == null)
        {
            return;
        }

        Vector3 terrainOrigin = _terrain.transform.position;
        Vector3 terrainScale = _terrain.transform.lossyScale;
        float terrainWidth = _terrain.terrainData.size.x * Mathf.Abs(terrainScale.x);
        float terrainLength = _terrain.terrainData.size.z * Mathf.Abs(terrainScale.z);
        if (terrainWidth <= 0f || terrainLength <= 0f)
        {
            return;
        }

        Vector3 position = transform.position;
        float normalizedX = (position.x - terrainOrigin.x) / terrainWidth;
        float normalizedZ = (position.z - terrainOrigin.z) / terrainLength;
        if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f)
        {
            return;
        }

        float surfaceY = terrainOrigin.y +
            _terrain.terrainData.GetInterpolatedHeight(normalizedX, normalizedZ) * Mathf.Abs(terrainScale.y);
        float scaleY = Mathf.Abs(transform.lossyScale.y);
        float capsuleBottomY = position.y +
            (characterController.center.y - characterController.height * 0.5f) * scaleY;

        // Do not change a valid spawn. Recover only when the character capsule is already
        // below the actual Terrain surface.
        if (capsuleBottomY >= surfaceY + 0.02f)
        {
            return;
        }

        bool wasEnabled = characterController.enabled;
        if (wasEnabled)
        {
            characterController.enabled = false;
        }

        position.y = surfaceY +
            (characterController.height * 0.5f - characterController.center.y) * scaleY +
            0.05f;
        transform.position = position;

        if (wasEnabled)
        {
            characterController.enabled = true;
        }

        Physics.SyncTransforms();
        Debug.Log(
            "PlayerController: character was below Terrain; moved to surface. " +
            "surfaceY=" + surfaceY + ", newPosition=" + transform.position);
    }

    /// <summary>
    /// Applies the server's spawn surface coordinate and offsets the character root
    /// so the CharacterController feet meet that surface.
    /// </summary>
    public void SetSpawnPosition(
        Vector3 serverPosition,
        Quaternion serverRotation,
        bool positionIsCharacterRoot)
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        _hasMoveTarget = false;
        _verticalVelocity = 0f;
        _targetPosition = serverPosition;
        _hasSentNetworkState = false;
        _nextNetworkSyncTime = 0f;
        _nextNetworkHeartbeatTime = 0f;

        if (_moveTargetInstance != null)
        {
            _moveTargetInstance.SetActive(false);
        }

        Vector3 characterPosition = serverPosition;
        if (!positionIsCharacterRoot && characterController != null)
        {
            // First-time spawn coordinates describe the ground surface; persisted coordinates
            // are the exact character root position and must be restored without an offset.
            characterPosition.y +=
                (characterController.height * 0.5f - characterController.center.y) *
                Mathf.Abs(transform.lossyScale.y) + 0.05f;
        }

        bool wasEnabled = characterController != null && characterController.enabled;
        if (wasEnabled)
        {
            characterController.enabled = false;
        }

        transform.SetPositionAndRotation(characterPosition, serverRotation);

        if (wasEnabled)
        {
            characterController.enabled = true;
        }

        // The server may restore a different facing direction than the scene default.
        // Re-align the camera after applying that rotation so it starts behind the player.
        if (_cameraController == null)
        {
            _cameraController = FindFirstObjectByType<ThirdPersonCameraController>();
        }

        if (_cameraController != null)
        {
            _cameraController.ResetBehindTarget();
        }

        Physics.SyncTransforms();
        Debug.Log(
            "PlayerController: spawned from server; receivedPosition=" + serverPosition +
            ", positionIsCharacterRoot=" + positionIsCharacterRoot +
            ", characterPosition=" + transform.position +
            ", rotation=" + transform.rotation);
    }

    /// <summary>
    /// Applies a server-approved position after the server has blocked movement through an
    /// active player or monster. Stop the current click order to avoid repeatedly pushing
    /// against a collision while local world-interest data catches up.
    /// </summary>
    public void ApplyServerCollisionCorrection(Vector3 acceptedPosition, Quaternion acceptedRotation)
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        bool wasEnabled = characterController != null && characterController.enabled;
        if (wasEnabled)
        {
            characterController.enabled = false;
        }

        transform.SetPositionAndRotation(acceptedPosition, acceptedRotation);

        if (wasEnabled)
        {
            characterController.enabled = true;
        }

        _verticalVelocity = 0f;
        _hasMoveTarget = false;
        if (_moveTargetInstance != null)
        {
            _moveTargetInstance.SetActive(false);
        }

        Physics.SyncTransforms();
        _hasSentNetworkState = false;
        _nextNetworkSyncTime = 0f;

        Debug.Log(
            "PlayerController: applied server collision correction; position=" +
            acceptedPosition + ", rotation=" + acceptedRotation);
    }

    private void CreateForwardMarker()
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "ForwardMarker";
        marker.transform.SetParent(transform, false);
        marker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        marker.transform.localScale = new Vector3(
            forwardMarkerRadius * 2f,
            forwardMarkerLength * 0.5f,
            forwardMarkerRadius * 2f);

        float controllerRadius = characterController != null
            ? characterController.radius
            : 0.5f;
        float controllerCenterY = characterController != null
            ? characterController.center.y
            : 1f;

        marker.transform.localPosition = new Vector3(
            0f,
            controllerCenterY,
            controllerRadius + forwardMarkerLength * 0.5f - forwardMarkerRadius * 0.25f);

        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            Destroy(markerCollider);
        }

        Renderer markerRenderer = marker.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            markerRenderer.material.color = forwardMarkerColor;
        }
    }


    private void Update()
    {
        if (characterController == null)
        {
            return;
        }

        // Clicking the world selects a Terrain point to walk to.
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            TrySetMoveTargetFromMouse();
        }

        // Movement is exclusively click-to-move; no keyboard movement is processed.
        Vector3 horizontalMovement = Vector3.zero;

        if (_hasMoveTarget)
        {
            Vector3 toTarget = _targetPosition - transform.position;
            toTarget.y = 0f;

            if (toTarget.magnitude <= clickStopDistance)
            {
                _hasMoveTarget = false;

                if (_moveTargetInstance != null)
                {
                    _moveTargetInstance.SetActive(false);
                }
            }
            else
            {
                horizontalMovement = toTarget.normalized;
            }
        }

        if (characterController.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }

        _verticalVelocity += gravity * Time.deltaTime;

        if (horizontalMovement.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(horizontalMovement, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }

        Vector3 movement = horizontalMovement * moveSpeed;
        movement.y = _verticalVelocity;
        

        characterController.Move(movement * Time.deltaTime);
        SyncNetworkStateIfNeeded();
    }

    private void SyncNetworkStateIfNeeded()
    {
        if (_gameServerLogin == null)
        {
            _gameServerLogin = GameServerLoginUI.Instance;
        }

        if (_gameServerLogin == null || !_gameServerLogin.IsReadyForPlayerState)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now < _nextNetworkSyncTime)
        {
            return;
        }
        _nextNetworkSyncTime = now + NetworkStateSyncInterval;

        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        bool hasMeaningfulChange = !_hasSentNetworkState ||
            (position - _lastSentNetworkPosition).sqrMagnitude >=
                NetworkPositionThreshold * NetworkPositionThreshold ||
            Quaternion.Angle(rotation, _lastSentNetworkRotation) >= NetworkRotationThresholdDegrees;
        bool heartbeatDue = !_hasSentNetworkState || now >= _nextNetworkHeartbeatTime;
        if (!hasMeaningfulChange && !heartbeatDue)
        {
            return;
        }

        _gameServerLogin.SendPlayerState(position, rotation);
        _lastSentNetworkPosition = position;
        _lastSentNetworkRotation = rotation;
        _hasSentNetworkState = true;
        _nextNetworkHeartbeatTime = now + NetworkStateHeartbeatInterval;
    }

    private void TrySetMoveTargetFromMouse()
    {
        // Resolve again in case the camera became active after this component's Awake.
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        if (playerCamera == null)
        {
            Debug.LogError("Click-to-move: no rendering Camera found; ray was not created.");
            return;
        }

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = playerCamera.ScreenPointToRay(mousePosition);

        // Search all hits and select the nearest TerrainCollider specifically.
        // This avoids accidentally treating the character or another object as terrain.
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            5000f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        bool foundTerrain = false;
        RaycastHit terrainHit = default;
        Vector3 targetPoint = Vector3.zero;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider is TerrainCollider &&
                (!foundTerrain || hit.distance < terrainHit.distance))
            {
                terrainHit = hit;
                foundTerrain = true;
            }
        }

        if (foundTerrain)
        {
            targetPoint = terrainHit.point;
        }
        else if (_terrain != null && TryFindTerrainSurface(ray, out Vector3 sampledPoint))
        {
            // Fallback to height sampling if the TerrainCollider does not return
            // a hit, so click-to-move still works while the physics scene is repaired.
            targetPoint = sampledPoint;
            Debug.LogWarning("Click-to-move: TerrainCollider raycast missed; used Terrain.SampleHeight fallback.");
        }
        else
        {
            string terrainState = _terrain == null
                ? "Terrain not found"
                : "Terrain enabled=" + _terrain.enabled +
                  ", data assigned=" + (_terrain.terrainData != null) +
                  ", collider enabled=" + (_terrain.GetComponent<TerrainCollider>() != null &&
                    _terrain.GetComponent<TerrainCollider>().enabled);

            Debug.LogWarning(
                "Click-to-move: ray did not intersect the terrain. " +
                "camera=" + playerCamera.name +
                ", rayOrigin=" + ray.origin +
                ", rayDirection=" + ray.direction +
                ", " + terrainState);
            return;
        }

        _targetPosition = targetPoint;
        _hasMoveTarget = true;

        if (_moveTargetInstance != null)
        {
            _moveTargetInstance.transform.SetPositionAndRotation(
                _targetPosition,
                Quaternion.identity
            );

            _moveTargetInstance.SetActive(true);
        }

        string hitSource = foundTerrain ? terrainHit.collider.name : "Terrain.SampleHeight";
        Debug.Log("Click-to-move target: " + _targetPosition + " (source: " + hitSource + ")");
    }

    private bool TryFindTerrainSurface(Ray ray, out Vector3 intersection)
    {
        intersection = Vector3.zero;
        if (_terrain == null || _terrain.terrainData == null)
        {
            return false;
        }

        TerrainData data = _terrain.terrainData;
        Vector3 terrainOrigin = _terrain.transform.position;
        Vector3 scale = _terrain.transform.lossyScale;
        float minX = terrainOrigin.x;
        float maxX = terrainOrigin.x + data.size.x * Mathf.Abs(scale.x);
        float minZ = terrainOrigin.z;
        float maxZ = terrainOrigin.z + data.size.z * Mathf.Abs(scale.z);

        const float sampleStep = 2f;
        const float maxDistance = 5000f;
        bool previousPointWasAboveSurface = false;
        float previousDistance = 0f;

        for (float distance = 0f; distance <= maxDistance; distance += sampleStep)
        {
            Vector3 point = ray.GetPoint(distance);
            bool withinTerrain =
                point.x >= minX && point.x <= maxX &&
                point.z >= minZ && point.z <= maxZ;

            if (!withinTerrain)
            {
                previousPointWasAboveSurface = false;
                continue;
            }

            float surfaceY = _terrain.SampleHeight(point) + terrainOrigin.y;
            bool pointIsAboveSurface = point.y >= surfaceY;

            if (previousPointWasAboveSurface && !pointIsAboveSurface)
            {
                // Refine the first above-to-below crossing to sub-centimeter precision.
                float low = previousDistance;
                float high = distance;

                for (int iteration = 0; iteration < 16; iteration++)
                {
                    float middle = (low + high) * 0.5f;
                    Vector3 middlePoint = ray.GetPoint(middle);
                    float middleSurfaceY = _terrain.SampleHeight(middlePoint) + terrainOrigin.y;
                    if (middlePoint.y >= middleSurfaceY)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                Vector3 hitPoint = ray.GetPoint((low + high) * 0.5f);
                hitPoint.y = _terrain.SampleHeight(hitPoint) + terrainOrigin.y;
                intersection = hitPoint;
                return true;
            }

            previousPointWasAboveSurface = pointIsAboveSurface;
            previousDistance = distance;
        }

        return false;
    }
}
