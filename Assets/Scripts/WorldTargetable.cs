using UnityEngine;

/// <summary>
/// Client-side interaction metadata and nameplate for a server-owned world entity.
/// The server remains authoritative for target state and monster health.
/// </summary>
public sealed class WorldTargetable : MonoBehaviour
{
    private static readonly Color NormalNameColor = Color.white;
    private static readonly Color HighlightNameColor = new Color(1f, 0.84f, 0.02f);
    private const float HealthBarWidth = 1.0f;
    private const float HealthBarHeight = 0.065f;
    private const float HealthBarDepth = 0.02f;

    private static Material healthBarBackgroundMaterial;
    private static Material healthBarFillMaterial;

    private TextMesh _nameLabel;
    private Camera _cachedCamera;
    private bool _hovered;
    private bool _selected;
    private bool _alive = true;
    private GameObject _healthBarRoot;
    private Transform _healthBarFill;
    private bool _isMonster;

    public string TargetType { get; private set; } = string.Empty;
    public string TargetID { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public bool IsAlive => _alive;

    public void Configure(string targetType, string targetID, string displayName, TextMesh nameLabel)
    {
        TargetType = targetType ?? string.Empty;
        TargetID = targetID ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        _isMonster = string.Equals(TargetType, "monster", System.StringComparison.OrdinalIgnoreCase);

        if (nameLabel != null)
        {
            _nameLabel = nameLabel;
            _nameLabel.text = DisplayName;
            _nameLabel.richText = false;
        }

        if (_isMonster)
        {
            EnsureHealthBar();
        }
        else
        {
            DestroyHealthBar();
        }

        RefreshNameStyle();
    }

    public void SetHovered(bool hovered)
    {
        if (_hovered == hovered) return;
        _hovered = hovered;
        RefreshNameStyle();
    }

    public void SetSelected(bool selected)
    {
        if (_selected == selected) return;
        _selected = selected;
        RefreshNameStyle();
    }

    public void SetAlive(bool alive)
    {
        _alive = alive;
        if (_healthBarRoot != null && !_alive)
        {
            _healthBarRoot.SetActive(false);
        }
    }

    /// <summary>Updates the red monster health bar using the latest server snapshot.</summary>
    public void SetHealth(int health, int maxHealth, bool alive)
    {
        SetAlive(alive);
        if (!_isMonster)
        {
            return;
        }

        EnsureHealthBar();
        if (_healthBarRoot == null || _healthBarFill == null)
        {
            return;
        }

        if (!_alive)
        {
            _healthBarRoot.SetActive(false);
            return;
        }

        float ratio = maxHealth > 0 ? Mathf.Clamp01((float)health / maxHealth) : 0f;
        float width = HealthBarWidth * ratio;
        _healthBarFill.localScale = new Vector3(width, HealthBarHeight, 0.008f);
        _healthBarFill.localPosition = new Vector3(
            -HealthBarWidth * 0.5f + width * 0.5f, 0f, 0.016f);
        _healthBarRoot.SetActive(true);
    }

    private void RefreshNameStyle()
    {
        if (_nameLabel == null)
        {
            return;
        }

        _nameLabel.color = _selected || _hovered ? HighlightNameColor : NormalNameColor;
        _nameLabel.fontStyle = _selected ? FontStyle.Bold : FontStyle.Normal;
    }

    private void EnsureHealthBar()
    {
        if (_healthBarRoot != null)
        {
            return;
        }

        if (healthBarBackgroundMaterial == null)
        {
            healthBarBackgroundMaterial = CreateHealthBarMaterial(
                "MonsterHealthBar_Background", new Color(0.13f, 0.035f, 0.035f));
        }
        if (healthBarFillMaterial == null)
        {
            healthBarFillMaterial = CreateHealthBarMaterial(
                "MonsterHealthBar_RedFill", new Color(0.88f, 0.035f, 0.025f));
        }

        _healthBarRoot = new GameObject("MonsterHealthBar");
        Transform anchor = _nameLabel != null ? _nameLabel.transform : transform;
        _healthBarRoot.transform.SetParent(anchor, false);
        if (_nameLabel != null)
        {
            // NameLabel is already scale-compensated and billboarded. Parent the bar to it
            // so it always stays directly beneath the text instead of drifting in world space.
            // TextMesh flips 180 degrees to face the camera, so cancel that flip for the bar
            // and offset it toward the camera to keep it visible over the monster mesh.
            _healthBarRoot.transform.localPosition = new Vector3(0f, -0.22f, -0.03f);
            _healthBarRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _healthBarRoot.transform.localScale = Vector3.one;
        }
        else
        {
            Vector3 scale = transform.lossyScale;
            float scaleX = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            float scaleY = Mathf.Max(0.0001f, Mathf.Abs(scale.y));
            float scaleZ = Mathf.Max(0.0001f, Mathf.Abs(scale.z));
            _healthBarRoot.transform.localScale = new Vector3(1f / scaleX, 1f / scaleY, 1f / scaleZ);
        }

        GameObject background = GameObject.CreatePrimitive(PrimitiveType.Cube);
        background.name = "HealthBar_Background";
        background.transform.SetParent(_healthBarRoot.transform, false);
        background.transform.localPosition = Vector3.zero;
        background.transform.localScale = new Vector3(HealthBarWidth, HealthBarHeight, HealthBarDepth);
        RemoveCollider(background);
        Renderer backgroundRenderer = background.GetComponent<Renderer>();
        if (backgroundRenderer != null)
        {
            backgroundRenderer.sharedMaterial = healthBarBackgroundMaterial;
            backgroundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            backgroundRenderer.receiveShadows = false;
        }

        GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fill.name = "HealthBar_Fill";
        fill.transform.SetParent(_healthBarRoot.transform, false);
        fill.transform.localPosition = new Vector3(-HealthBarWidth * 0.5f, 0f, 0.016f);
        fill.transform.localScale = new Vector3(HealthBarWidth, HealthBarHeight, 0.008f);
        RemoveCollider(fill);
        Renderer fillRenderer = fill.GetComponent<Renderer>();
        if (fillRenderer != null)
        {
            fillRenderer.sharedMaterial = healthBarFillMaterial;
            fillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fillRenderer.receiveShadows = false;
        }
        _healthBarFill = fill.transform;
        _healthBarRoot.SetActive(_alive);
    }

    private static Material CreateHealthBarMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogWarning("WorldTargetable: no supported shader for " + materialName + ".");
            return null;
        }

        Material material = new Material(shader) { name = materialName };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        material.color = color;
        return material;
    }

    private static void RemoveCollider(GameObject part)
    {
        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }
    }

    private void LateUpdate()
    {
        if (_nameLabel == null)
        {
            return;
        }

        if (_cachedCamera == null)
        {
            _cachedCamera = Camera.main;
        }
        if (_cachedCamera == null)
        {
            return;
        }

        Vector3 directionToCamera = _cachedCamera.transform.position - _nameLabel.transform.position;
        if (directionToCamera.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        if (!_alive && _isMonster)
        {
            // A corpse may roll, but its nameplate remains readable above it.
            _nameLabel.transform.position = transform.position + Vector3.up * 1.45f;
        }

        directionToCamera = _cachedCamera.transform.position - _nameLabel.transform.position;
        if (directionToCamera.sqrMagnitude > 0.0001f)
        {
            // TextMesh's front points backwards in this project, so flip the label.
            _nameLabel.transform.rotation = Quaternion.LookRotation(directionToCamera, Vector3.up) *
                Quaternion.Euler(0f, 180f, 0f);
        }

        // The health bar is parented to the billboarded name label, so it shares the
        // same camera-facing transform and remains exactly beneath the name.
    }

    private void OnDestroy()
    {
        DestroyHealthBar();
    }

    private void DestroyHealthBar()
    {
        if (_healthBarRoot != null)
        {
            Destroy(_healthBarRoot);
        }
        _healthBarRoot = null;
        _healthBarFill = null;
    }
}
