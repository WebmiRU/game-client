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
    private WorldTargetable _hoveredWorldTarget;
    [SerializeField, Min(0.001f)] private float visualGroundSmoothingTime = 0.075f;
    private Transform _playerVisualTransform;
    private PlayerCombatVisuals _combatVisuals;
    [SerializeField, Min(0f)] private float meleeRangeGap = 0.15f;
    [SerializeField, Min(0.05f)] private float meleeAttackAnimationMaxDuration = 0.38f;
    private WorldTargetable _meleeTarget;
    private Vector3 _meleeApproachDirection;
    private bool _isInMeleeRange;
    private bool _isMeleeCombatActive;
    private bool _attackRequestPending;
    private int _attackRequestSequence;
    private int _pendingAttackRequestId;
    private string _pendingAttackTargetType = string.Empty;
    private string _pendingAttackTargetID = string.Empty;
    private float _nextAttackAttemptTime;
    private float _smoothedVisualWorldY;
    private float _visualWorldYVelocity;
    private bool _hasSmoothedVisualHeight;

    private GameServerLoginUI _gameServerLogin;
    private ThirdPersonCameraController _cameraController;
    private float _nextNetworkSyncTime;
    private float _nextNetworkHeartbeatTime;
    private Vector3 _lastSentNetworkPosition;
    private Quaternion _lastSentNetworkRotation;
    private bool _hasSentNetworkState;
    private bool _lastSentHasMoveTarget;
    private Vector3 _lastSentMoveTargetPosition;

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
        AlignPrimitiveCapsuleVisualWithFeetOrigin();

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
        InitializeCombatVisuals();
    }

    private void InitializeCombatVisuals()
    {
        _combatVisuals = GetComponent<PlayerCombatVisuals>();
        if (_combatVisuals == null)
        {
            _combatVisuals = gameObject.AddComponent<PlayerCombatVisuals>();
        }

        // Parent equipment to the visible capsule so it follows visual ground smoothing.
        Transform visualAnchor = _playerVisualTransform != null
            ? _playerVisualTransform
            : transform;
        _combatVisuals.Initialize(visualAnchor);
    }

    /// <summary>Starts a local sword swing using the requested animation duration.</summary>
    public void PlayAttackAnimation()
    {
        if (_combatVisuals != null)
        {
            _combatVisuals.PlayAttack();
        }
    }

    public void PlayAttackAnimation(float animationDuration)
    {
        if (_combatVisuals != null)
        {
            _combatVisuals.PlayAttack(animationDuration);
        }
    }

    private void BeginMeleeAttack(WorldTargetable target)
    {
        if (target == null)
        {
            return;
        }

        if (_meleeTarget == target)
        {
            // Re-clicking the active target does not reset its cadence.
            return;
        }

        CancelMeleeAttack();
        _meleeTarget = target;

        Vector3 fromTargetToPlayer = transform.position - target.transform.position;
        fromTargetToPlayer.y = 0f;
        _meleeApproachDirection = fromTargetToPlayer.sqrMagnitude > 0.0001f
            ? fromTargetToPlayer.normalized
            : -transform.forward;
        _isMeleeCombatActive = true;
        _nextAttackAttemptTime = Time.time;
        _isInMeleeRange = false;
        Debug.Log("Melee combat started: target=" + target.DisplayName + " (" + target.TargetID + ").");

        // The second click sends the attack command immediately. The server resolves
        // its own selected target; if the player is too far away, we pursue and retry
        // once we reach melee distance.
        TryRequestMeleeAttack(Time.time);
    }

    private void CancelMeleeAttack()
    {
        if (_meleeTarget != null)
        {
            Debug.Log("Melee combat stopped: target=" + _meleeTarget.DisplayName + ".");
        }

        _meleeTarget = null;
        _isMeleeCombatActive = false;
        _isInMeleeRange = false;
        _nextAttackAttemptTime = Time.time;
        _hasMoveTarget = false;

        if (_moveTargetInstance != null)
        {
            _moveTargetInstance.SetActive(false);
        }

        if (_combatVisuals != null)
        {
            _combatVisuals.StopAttack();
        }
    }

    private void UpdateMeleeCombatState()
    {
        if (!_isMeleeCombatActive)
        {
            return;
        }

        // Unity destroyed objects compare equal to null. Keep a separate combat-active
        // flag so a despawned target also cancels pursuit rather than leaving the last
        // movement destination running forever.
        if (_meleeTarget == null ||
            !_meleeTarget.gameObject.activeInHierarchy ||
            (!System.String.Equals(
                _meleeTarget.TargetType,
                "monster",
                System.StringComparison.OrdinalIgnoreCase) &&
             !System.String.Equals(
                _meleeTarget.TargetType,
                "player",
                System.StringComparison.OrdinalIgnoreCase)))
        {
            CancelMeleeAttack();
            return;
        }

        Vector3 targetPosition = _meleeTarget.transform.position;
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;
        float distanceToTarget = toTarget.magnitude;

        if (_meleeApproachDirection.sqrMagnitude < 0.0001f)
        {
            _meleeApproachDirection = distanceToTarget > 0.0001f
                ? -toTarget.normalized
                : -transform.forward;
        }

        float meleeRange = GetMeleeRange(_meleeTarget, _meleeApproachDirection);
        // Use the movement arrival tolerance to enter range, and a small hysteresis
        // margin to avoid bouncing between walking and attacking on contact.
        float enterRange = meleeRange + clickStopDistance;
        float leaveRange = enterRange + 0.25f;
        bool inRange = _isInMeleeRange
            ? distanceToTarget <= leaveRange
            : distanceToTarget <= enterRange;

        if (inRange)
        {
            bool justEnteredRange = !_isInMeleeRange;
            _isInMeleeRange = true;
            _hasMoveTarget = false;
            if (_moveTargetInstance != null)
            {
                _moveTargetInstance.SetActive(false);
            }

            float now = Time.time;
            if (justEnteredRange)
            {
                _nextAttackAttemptTime = now;
            }

            // Request each swing only while in range. The first attack command was
            // already sent from BeginMeleeAttack on the second target click.
            TryRequestMeleeAttack(now);
            return;
        }

        _isInMeleeRange = false;
        _hasMoveTarget = true;
        _targetPosition = targetPosition + _meleeApproachDirection * meleeRange;
        _targetPosition.y = transform.position.y;

        if (_moveTargetInstance != null)
        {
            _moveTargetInstance.transform.SetPositionAndRotation(_targetPosition, Quaternion.identity);
            _moveTargetInstance.SetActive(true);
        }
    }

    private void TryRequestMeleeAttack(float now)
    {
        if (!_isMeleeCombatActive || _meleeTarget == null ||
            _attackRequestPending || now < _nextAttackAttemptTime)
        {
            return;
        }

        if (_gameServerLogin == null)
        {
            _gameServerLogin = GameServerLoginUI.Instance;
        }

        float attackInterval = 1f / GetCurrentServerAttackSpeed();
        int requestId = NextAttackRequestId();
        if (_gameServerLogin != null && _gameServerLogin.RequestAttack(
            requestId,
            _meleeTarget.TargetType,
            _meleeTarget.TargetID))
        {
            _attackRequestPending = true;
            _pendingAttackRequestId = requestId;
            _pendingAttackTargetType = _meleeTarget.TargetType;
            _pendingAttackTargetID = _meleeTarget.TargetID;
            _nextAttackAttemptTime = now + attackInterval;
        }
        else
        {
            _nextAttackAttemptTime = now + 0.2f;
        }
    }

    private int NextAttackRequestId()
    {
        unchecked
        {
            _attackRequestSequence++;
        }
        if (_attackRequestSequence <= 0)
        {
            _attackRequestSequence = 1;
        }
        return _attackRequestSequence;
    }

    /// <summary>Receives the result of the server-authoritative attack command.</summary>
    public void HandleAttackResult(
        int requestId,
        string targetType,
        string targetId,
        bool success,
        string message,
        float serverAttackSpeed,
        float retryAfter)
    {
        if (!_attackRequestPending || requestId != _pendingAttackRequestId)
        {
            // A delayed reply from an attack that was cancelled or superseded must not
            // animate the currently selected target.
            Debug.Log("PlayerController: ignored stale attack result; requestId=" + requestId);
            return;
        }

        string requestedType = _pendingAttackTargetType;
        string requestedID = _pendingAttackTargetID;
        _attackRequestPending = false;
        _pendingAttackRequestId = 0;
        _pendingAttackTargetType = string.Empty;
        _pendingAttackTargetID = string.Empty;

        bool stillAttackingThisTarget = _isMeleeCombatActive &&
            _meleeTarget != null &&
            System.String.Equals(_meleeTarget.TargetType, requestedType, System.StringComparison.OrdinalIgnoreCase) &&
            System.String.Equals(_meleeTarget.TargetID, requestedID, System.StringComparison.Ordinal);
        bool serverTargetMatchesRequest =
            string.IsNullOrEmpty(targetType) ||
            (System.String.Equals(targetType, requestedType, System.StringComparison.OrdinalIgnoreCase) &&
             System.String.Equals(targetId, requestedID, System.StringComparison.Ordinal));

        if (!stillAttackingThisTarget || !serverTargetMatchesRequest)
        {
            return;
        }

        if (success)
        {
            float attacksPerSecond = serverAttackSpeed > 0f &&
                !float.IsNaN(serverAttackSpeed) && !float.IsInfinity(serverAttackSpeed)
                    ? Mathf.Clamp(serverAttackSpeed, 0.1f, 10f)
                    : GetCurrentServerAttackSpeed();
            _nextAttackAttemptTime = Time.time + 1f / attacksPerSecond;
            float animationDuration = Mathf.Min(
                meleeAttackAnimationMaxDuration,
                (1f / attacksPerSecond) * 0.9f);
            PlayAttackAnimation(animationDuration);
            Debug.Log("Melee attack confirmed by server: target=" +
                (string.IsNullOrEmpty(targetType) ? requestedType : targetType) + ":" +
                (string.IsNullOrEmpty(targetId) ? requestedID : targetId) +
                ", attacksPerSecond=" + attacksPerSecond.ToString("0.##"));
            return;
        }

        if (message == "attack_cooldown")
        {
            _nextAttackAttemptTime = Time.time + Mathf.Max(0.05f, retryAfter);
            return;
        }

        if (message == "attack_out_of_range")
        {
            _isInMeleeRange = false;
            _nextAttackAttemptTime = Time.time + Mathf.Max(0.05f, retryAfter);
            return;
        }

        Debug.LogWarning("Melee attack stopped after server rejection: " + message);
        CancelMeleeAttack();
    }

    private float GetMeleeRange(WorldTargetable target, Vector3 targetToPlayerDirection)
    {
        float rootScaleX = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
        float rootScaleZ = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z));
        float playerRadius = characterController != null
            ? characterController.radius * Mathf.Max(rootScaleX, rootScaleZ)
            : 0.5f;

        float targetRadius = GetTargetHorizontalRadius(target, targetToPlayerDirection);
        return playerRadius + targetRadius + meleeRangeGap;
    }

    private static float GetTargetHorizontalRadius(WorldTargetable target, Vector3 direction)
    {
        Collider targetCollider = target != null ? target.GetComponent<Collider>() : null;
        if (targetCollider == null)
        {
            return 0.5f;
        }

        Bounds bounds = targetCollider.bounds;
        if (targetCollider is BoxCollider)
        {
            // Support radius of the box along the current approach direction.
            return Mathf.Abs(direction.x) * bounds.extents.x +
                Mathf.Abs(direction.z) * bounds.extents.z;
        }

        // Sphere/capsule targets use a radial footprint; the larger horizontal extent
        // is safe for both the default spherical enemies and a future taller capsule.
        return Mathf.Max(bounds.extents.x, bounds.extents.z);
    }

    private float GetCurrentServerAttackSpeed()
    {
        if (_gameServerLogin == null)
        {
            _gameServerLogin = GameServerLoginUI.Instance;
        }

        float attackSpeed = _gameServerLogin != null && _gameServerLogin.HasServerAttackSpeed
            ? _gameServerLogin.ServerAttackSpeed
            : 1f;
        return Mathf.Clamp(attackSpeed, 0.1f, 10f);
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

        // Keep the world-space collider at height 2 m and diameter 1 m even if
        // the imported character root uses a non-unit scale. Its bottom is at transform.position.
        characterController.height = 2f / scaleY;
        characterController.radius = 0.5f / Mathf.Max(scaleX, scaleZ);
        characterController.center = new Vector3(0f, 1f / scaleY, 0f);
        // A very narrow skin forces the controller to react to tiny contact changes
        // on every frame. The recommended 10% radius helps keep contact stable.
        characterController.skinWidth = Mathf.Min(
            0.05f / Mathf.Max(scaleX, scaleZ),
            characterController.radius * 0.1f);
        characterController.minMoveDistance = 0f;
        characterController.stepOffset = Mathf.Min(0.3f / scaleY, characterController.height * 0.4f);

        // The scene's original primitive CapsuleCollider is centred on the root,
        // while CharacterController is feet-anchored. Keeping both active leaves
        // a second collider intersecting the Terrain and can interfere with movement.
        CapsuleCollider legacyCollider = GetComponent<CapsuleCollider>();
        if (legacyCollider != null)
        {
            legacyCollider.enabled = false;
        }

        Debug.Log(
            "PlayerController: collider configured; worldHeight=2, worldDiameter=1, " +
            "localHeight=" + characterController.height +
            ", localRadius=" + characterController.radius +
            ", localCenter=" + characterController.center);
    }

    private void AlignPrimitiveCapsuleVisualWithFeetOrigin()
    {
        if (characterController == null)
        {
            return;
        }

        MeshFilter sourceFilter = GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer = GetComponent<MeshRenderer>();
        if (sourceFilter == null || sourceFilter.sharedMesh == null || sourceRenderer == null ||
            !string.Equals(sourceFilter.sharedMesh.name, "Capsule", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // The built-in Capsule mesh and CharacterController both have a 1m diameter
        // and 2m height in world space, including a non-unit/non-uniform root scale.
        Vector3 rootScale = transform.lossyScale;
        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(rootScale.x));
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(rootScale.y));
        float scaleZ = Mathf.Max(0.0001f, Mathf.Abs(rootScale.z));
        Vector3 visualScale = new Vector3(
            1f / scaleX,
            1f / scaleY,
            1f / scaleZ);

        // PlayerController.transform.position is the feet/base point used by the
        // server and CharacterController. The visible mesh is centred on the collider.
        Transform existingVisual = transform.Find("PlayerVisual");
        if (existingVisual != null)
        {
            existingVisual.localPosition = characterController.center;
            existingVisual.localScale = visualScale;
            _playerVisualTransform = existingVisual;
            sourceRenderer.enabled = false;
            return;
        }

        GameObject visual = new GameObject("PlayerVisual");
        visual.layer = gameObject.layer;
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = characterController.center;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = visualScale;
        _playerVisualTransform = visual.transform;

        MeshFilter visualFilter = visual.AddComponent<MeshFilter>();
        visualFilter.sharedMesh = sourceFilter.sharedMesh;

        MeshRenderer visualRenderer = visual.AddComponent<MeshRenderer>();
        visualRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        visualRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        visualRenderer.receiveShadows = sourceRenderer.receiveShadows;
        visualRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        visualRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        visualRenderer.motionVectorGenerationMode = sourceRenderer.motionVectorGenerationMode;
        visualRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        visualRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        visualRenderer.sortingOrder = sourceRenderer.sortingOrder;
        visualRenderer.enabled = sourceRenderer.enabled;

        sourceRenderer.enabled = false;
        Debug.Log("PlayerController: moved primitive Capsule visual to the CharacterController centre.");
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

        Vector3 position = transform.position;
        if (!TryGetTerrainSurfaceY(position, out float surfaceY))
        {
            return;
        }

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

    private bool TryGetTerrainSurfaceY(Vector3 position, out float surfaceY)
    {
        surfaceY = 0f;
        if (_terrain == null || _terrain.terrainData == null)
        {
            return false;
        }

        Vector3 terrainOrigin = _terrain.transform.position;
        Vector3 terrainScale = _terrain.transform.lossyScale;
        float terrainWidth = _terrain.terrainData.size.x * Mathf.Abs(terrainScale.x);
        float terrainLength = _terrain.terrainData.size.z * Mathf.Abs(terrainScale.z);
        if (terrainWidth <= 0f || terrainLength <= 0f)
        {
            return false;
        }

        float normalizedX = (position.x - terrainOrigin.x) / terrainWidth;
        float normalizedZ = (position.z - terrainOrigin.z) / terrainLength;
        if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f)
        {
            return false;
        }

        surfaceY = terrainOrigin.y +
            _terrain.terrainData.GetInterpolatedHeight(normalizedX, normalizedZ) *
            Mathf.Abs(terrainScale.y);
        return true;
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

        CancelMeleeAttack();
        _attackRequestPending = false;
        _pendingAttackRequestId = 0;
        _pendingAttackTargetType = string.Empty;
        _pendingAttackTargetID = string.Empty;
        _hasMoveTarget = false;
        _verticalVelocity = 0f;
        _hasSmoothedVisualHeight = false;
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
    public void ApplyServerCollisionCorrection(Vector3 acceptedPosition, Quaternion acceptedRotation, bool stopMovement = false)
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        if (stopMovement)
        {
            // If the server cannot make progress toward the melee approach point,
            // stop combat too; otherwise the combat update would issue the same
            // impossible move order again on the very next frame.
            if (_isMeleeCombatActive)
            {
                CancelMeleeAttack();
            }
            else
            {
                _hasMoveTarget = false;
                if (_moveTargetInstance != null)
                {
                    _moveTargetInstance.SetActive(false);
                }
            }
        }

        if (characterController == null)
        {
            _hasSentNetworkState = false;
            _nextNetworkSyncTime = 0f;
            return;
        }

        Vector3 currentPosition = transform.position;
        float horizontalDelta = Vector2.Distance(
            new Vector2(currentPosition.x, currentPosition.z),
            new Vector2(acceptedPosition.x, acceptedPosition.z));
        float verticalDelta = Mathf.Abs(currentPosition.y - acceptedPosition.y);
        float rotationDelta = Quaternion.Angle(transform.rotation, acceptedRotation);

        // Unity physics and the server solver will differ by a few centimetres.
        // Snapping those differences causes visible movement and shadow shimmer.
        const float horizontalCorrectionThreshold = 0.06f;
        const float verticalCorrectionThreshold = 0.08f;
        const float rotationCorrectionThreshold = 1f;
        if (horizontalDelta < horizontalCorrectionThreshold &&
            verticalDelta < verticalCorrectionThreshold &&
            rotationDelta < rotationCorrectionThreshold)
        {
            _hasSentNetworkState = false;
            _nextNetworkSyncTime = 0f;
            return;
        }

        Vector3 correctedPosition = acceptedPosition;
        if (horizontalDelta < horizontalCorrectionThreshold)
        {
            correctedPosition.x = currentPosition.x;
            correctedPosition.z = currentPosition.z;
        }
        if (verticalDelta < verticalCorrectionThreshold)
        {
            correctedPosition.y = currentPosition.y;
        }

        bool wasEnabled = characterController.enabled;
        if (wasEnabled)
        {
            characterController.enabled = false;
        }

        // Apply only meaningful server displacement; keep minor local ground offsets.
        transform.SetPositionAndRotation(
            correctedPosition,
            rotationDelta >= rotationCorrectionThreshold ? acceptedRotation : transform.rotation);

        if (wasEnabled)
        {
            characterController.enabled = true;
        }

        _verticalVelocity = 0f;
        Physics.SyncTransforms();
        _hasSentNetworkState = false;
        _nextNetworkSyncTime = 0f;

        Debug.Log(
            "PlayerController: applied meaningful server collision correction; position=" +
            correctedPosition + ", deltaXZ=" + horizontalDelta + ", deltaY=" + verticalDelta);
    }

    /// <summary>
    /// Handles a server Y-only terrain correction without cancelling click-to-move.
    /// This is used when the server's heightmap grounds a position differently from
    /// the client's last CharacterController step, or when Terrain changed underneath
    /// a connected player. Horizontal coordinates and the active move target are kept.
    /// </summary>
    public void ApplyServerGroundCorrection(float serverGroundY)
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }
        if (characterController == null)
        {
            return;
        }

        Vector3 position = transform.position;
        float scaleY = Mathf.Abs(transform.lossyScale.y);
        float rootAboveSurface = (characterController.height * 0.5f -
            characterController.center.y) * scaleY + 0.05f;

        // The server's Y belongs to the last requested X/Z and may arrive after the
        // player has already moved onto a different slope. Prefer the terrain directly
        // under the current client position; use server Y only when no local Terrain exists.
        float minimumRootY = TryGetTerrainSurfaceY(position, out float localSurfaceY)
            ? localSurfaceY + rootAboveSurface
            : serverGroundY;

        // Ground correction is intentionally one-way: never push a character down
        // through local terrain. Ordinary downward settling remains the controller's job.
        if (position.y >= minimumRootY - 0.08f)
        {
            return;
        }

        bool wasEnabled = characterController.enabled;
        if (wasEnabled)
        {
            characterController.enabled = false;
        }

        position.y = minimumRootY;
        transform.position = position;

        if (wasEnabled)
        {
            characterController.enabled = true;
        }

        _verticalVelocity = 0f;
        Physics.SyncTransforms();
        _hasSentNetworkState = false;
        _nextNetworkSyncTime = 0f;

        // Do not clear _hasMoveTarget: a vertical terrain adjustment is not a wall.
        Debug.Log(
            "PlayerController: raised player to current ground without cancelling movement; position=" +
            transform.position);
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

        UpdateHoveredWorldTarget();

        // Clicking an entity selects it; clicking the selected monster again starts melee combat.
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            TrySetMoveTargetFromMouse();
        }

        UpdateMeleeCombatState();

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

        if (characterController.isGrounded)
        {
            if (_verticalVelocity < 0f)
            {
                // Maintain gentle downward contact without adding gravity again each frame.
                _verticalVelocity = -2f;
            }
        }
        else
        {
            _verticalVelocity = Mathf.Max(
                _verticalVelocity + gravity * Time.deltaTime,
                -20f);
        }

        Vector3 facingDirection = horizontalMovement;
        if (_meleeTarget != null && _isInMeleeRange)
        {
            Vector3 toCombatTarget = _meleeTarget.transform.position - transform.position;
            toCombatTarget.y = 0f;
            if (toCombatTarget.sqrMagnitude > 0.001f)
            {
                facingDirection = toCombatTarget.normalized;
            }
        }

        if (facingDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
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

    private void LateUpdate()
    {
        if (_playerVisualTransform == null || characterController == null)
        {
            return;
        }

        // Ease only the rendered shell. The CharacterController/server-facing root remains
        // exact, while lighting and shadows follow a smoother visual height on slopes.
        float targetWorldY = transform.TransformPoint(characterController.center).y;
        if (!_hasSmoothedVisualHeight)
        {
            _smoothedVisualWorldY = targetWorldY;
            _hasSmoothedVisualHeight = true;
        }
        else
        {
            _smoothedVisualWorldY = Mathf.SmoothDamp(
                _smoothedVisualWorldY,
                targetWorldY,
                ref _visualWorldYVelocity,
                visualGroundSmoothingTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
        }

        Vector3 localPosition = _playerVisualTransform.localPosition;
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));
        localPosition.y = (_smoothedVisualWorldY - transform.position.y) / scaleY;
        _playerVisualTransform.localPosition = localPosition;
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
        bool moveTargetChanged = _lastSentHasMoveTarget != _hasMoveTarget ||
            (_hasMoveTarget &&
             (_targetPosition - _lastSentMoveTargetPosition).sqrMagnitude >=
                NetworkPositionThreshold * NetworkPositionThreshold);
        bool hasMeaningfulChange = !_hasSentNetworkState ||
            (position - _lastSentNetworkPosition).sqrMagnitude >=
                NetworkPositionThreshold * NetworkPositionThreshold ||
            Quaternion.Angle(rotation, _lastSentNetworkRotation) >= NetworkRotationThresholdDegrees ||
            moveTargetChanged;

        // While a move order is active, send a heartbeat at the normal bounded sync
        // interval even if a wall prevents the CharacterController from changing position.
        // That lets the server apply its retry timeout promptly instead of waiting for a 1s heartbeat.
        bool heartbeatDue = !_hasSentNetworkState || now >= _nextNetworkHeartbeatTime || _hasMoveTarget;
        if (!hasMeaningfulChange && !heartbeatDue)
        {
            return;
        }

        _gameServerLogin.SendPlayerState(position, rotation, _hasMoveTarget, _targetPosition);
        _lastSentNetworkPosition = position;
        _lastSentNetworkRotation = rotation;
        _lastSentHasMoveTarget = _hasMoveTarget;
        _lastSentMoveTargetPosition = _targetPosition;
        _hasSentNetworkState = true;
        _nextNetworkHeartbeatTime = now + NetworkStateHeartbeatInterval;
    }

    private void UpdateHoveredWorldTarget()
    {
        WorldTargetable nextTarget = GetWorldTargetUnderMouse();
        if (_hoveredWorldTarget == nextTarget)
        {
            return;
        }

        if (_hoveredWorldTarget != null)
        {
            _hoveredWorldTarget.SetHovered(false);
        }
        _hoveredWorldTarget = nextTarget;
        if (_hoveredWorldTarget != null)
        {
            _hoveredWorldTarget.SetHovered(true);
        }
    }

    private WorldTargetable GetWorldTargetUnderMouse()
    {
        if (Mouse.current == null)
        {
            return null;
        }

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
        if (playerCamera == null)
        {
            return null;
        }

        Ray ray = playerCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(
            ray, 5000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        Collider nearestCollider = null;
        foreach (RaycastHit hit in hits)
        {
            Collider hitCollider = hit.collider;
            if (hitCollider == null)
            {
                continue;
            }

            Transform hitTransform = hitCollider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                nearestCollider = hitCollider;
            }
        }

        // The nearest hit wins. Terrain or a wall in front of a target blocks selection.
        return nearestCollider != null
            ? nearestCollider.GetComponentInParent<WorldTargetable>()
            : null;
    }

    private void TrySetMoveTargetFromMouse()
    {
        WorldTargetable clickedTarget = GetWorldTargetUnderMouse();
        if (clickedTarget != null)
        {
            if (_gameServerLogin == null)
            {
                _gameServerLogin = GameServerLoginUI.Instance;
            }

            bool alreadySelected = _gameServerLogin != null &&
                _gameServerLogin.IsTargetSelected(clickedTarget);
            bool isAttackableTarget =
                System.String.Equals(clickedTarget.TargetType, "monster", System.StringComparison.OrdinalIgnoreCase) ||
                System.String.Equals(clickedTarget.TargetType, "player", System.StringComparison.OrdinalIgnoreCase);

            if (alreadySelected && isAttackableTarget)
            {
                // First click selects the monster; clicking that same selection again
                // starts melee pursuit and repeated attacks.
                BeginMeleeAttack(clickedTarget);
            }
            else
            {
                // Selecting a different entity changes only the UI target. It must not
                // cancel an existing move order or retarget an active melee command.
                if (_gameServerLogin != null)
                {
                    _gameServerLogin.SelectTarget(clickedTarget);
                }
            }

            return;
        }

        // Clicking the terrain exits combat mode and issues a normal move order.
        CancelMeleeAttack();

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
        _hasSentNetworkState = false;
        _nextNetworkSyncTime = 0f;

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
