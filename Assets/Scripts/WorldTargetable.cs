using UnityEngine;

/// <summary>
/// Client-side interaction metadata for a server-owned world entity.
/// The server remains authoritative for whether the target exists and is alive.
/// </summary>
public sealed class WorldTargetable : MonoBehaviour
{
    private static readonly Color NormalNameColor = Color.white;
    private static readonly Color HighlightNameColor = new Color(1f, 0.84f, 0.02f);

    private TextMesh _nameLabel;
    private Camera _cachedCamera;
    private bool _hovered;
    private bool _selected;

    public string TargetType { get; private set; } = string.Empty;
    public string TargetID { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;

    public void Configure(string targetType, string targetID, string displayName, TextMesh nameLabel)
    {
        TargetType = targetType ?? string.Empty;
        TargetID = targetID ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        if (nameLabel != null)
        {
            _nameLabel = nameLabel;
            _nameLabel.text = DisplayName;
            _nameLabel.richText = false;
        }

        RefreshNameStyle();
    }

    public void SetHovered(bool hovered)
    {
        if (_hovered == hovered)
        {
            return;
        }

        _hovered = hovered;
        RefreshNameStyle();
    }

    public void SetSelected(bool selected)
    {
        if (_selected == selected)
        {
            return;
        }

        _selected = selected;
        RefreshNameStyle();
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
        if (directionToCamera.sqrMagnitude > 0.0001f)
        {
            // TextMesh's front points backwards in this project; rotate the nameplate
            // so it remains readable from the active camera.
            _nameLabel.transform.rotation = Quaternion.LookRotation(directionToCamera, Vector3.up) *
                Quaternion.Euler(0f, 180f, 0f);
        }
    }
}
