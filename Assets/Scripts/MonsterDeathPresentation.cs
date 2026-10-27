using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Plays the local monster death presentation. Health and the death transition are supplied
/// by the server; this component only animates the visuals and fades the retained corpse.
/// </summary>
public sealed class MonsterDeathPresentation : MonoBehaviour
{
    [SerializeField, Min(0.2f)] private float dogFallDuration = 1f;
    [SerializeField, Min(0.2f)] private float faceChangeDuration = 1f;
    [SerializeField, Min(0f)] private float corpseFadeDelay = 6f;
    [SerializeField, Min(0.2f)] private float corpseFadeDuration = 1f;

    private sealed class FadeMaterial
    {
        public Material Material;
        public Color OriginalColor;
    }

    private readonly List<FadeMaterial> _fadeMaterials = new List<FadeMaterial>();
    private readonly List<Transform> _smileDots = new List<Transform>();
    private readonly List<Vector3> _smileStartPositions = new List<Vector3>();
    private readonly List<Vector3> _sadEndPositions = new List<Vector3>();

    private bool _isDead;
    private bool _fadePrepared;
    private bool _renderersHidden;
    private string _templateID = string.Empty;
    private float _deathStartedAt;
    private Vector3 _standingPosition;
    private Quaternion _standingRotation;
    private Renderer[] _renderers = System.Array.Empty<Renderer>();

    public bool IsDead => _isDead;

    /// <summary>Applies a server-owned alive/dead state and aligns late-joining observers.</summary>
    public void ApplyServerState(bool alive, string templateID, float deathElapsedSeconds)
    {
        if (alive || _isDead)
        {
            return;
        }

        _templateID = templateID ?? string.Empty;
        BeginDeath(Mathf.Max(0f, deathElapsedSeconds));
    }

    private void BeginDeath(float elapsedAtReceipt)
    {
        if (_isDead)
        {
            return;
        }

        _isDead = true;
        _deathStartedAt = Time.time - elapsedAtReceipt;
        _standingPosition = transform.position;
        _standingRotation = transform.rotation;

        // Keep the monster's collision active while its corpse is visible. Disabling every
        // collider here allowed the player to walk halfway into the dog during the death roll.
        // The visual roll is only a presentation change; the server-owned entity still blocks
        // movement until the server removes it at the end of the corpse lifetime.

        WorldTargetable targetable = GetComponent<WorldTargetable>();
        if (targetable != null)
        {
            targetable.SetAlive(false);
        }

        CacheSmileDots();
        PrepareFadeMaterials();
        UpdateDeathVisuals();
    }

    private void Update()
    {
        if (_isDead)
        {
            UpdateDeathVisuals();
        }
    }

    private void CacheSmileDots()
    {
        _smileDots.Clear();
        _smileStartPositions.Clear();
        _sadEndPositions.Clear();

        if (_templateID != "test-sphere-enemy")
        {
            return;
        }

        for (int i = 1; i <= 7; i++)
        {
            Transform dot = transform.Find("Smile" + i);
            if (dot == null)
            {
                continue;
            }

            Vector3 start = dot.localPosition;
            Vector3 end = start;
            float normalizedX = Mathf.Clamp01(Mathf.Abs(start.x) / 0.17f);
            // The old smile sits lower in the middle. Invert that curve into a gentle frown.
            end.y = -0.105f + 0.085f * (1f - normalizedX * normalizedX);
            _smileDots.Add(dot);
            _smileStartPositions.Add(start);
            _sadEndPositions.Add(end);
        }
    }

    private void UpdateDeathVisuals()
    {
        float elapsed = Mathf.Max(0f, Time.time - _deathStartedAt);

        if (_templateID == "test-cube-enemy")
        {
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.2f, dogFallDuration));
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            // The dog's +Z-facing muzzle rolls down toward the ground while the -Z tail
            // swings upward. Rotation is spread over the full duration, never instant.
            transform.rotation = _standingRotation *
                Quaternion.AngleAxis(90f * easedProgress, Vector3.right);
            // Let the muzzle lead the fall slightly so the dog rolls forward rather than tipping in place.
            transform.position = _standingPosition +
                (_standingRotation * Vector3.forward) * (0.45f * easedProgress);
        }
        else if (_templateID == "test-sphere-enemy" && _smileDots.Count > 0)
        {
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.2f, faceChangeDuration));
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            for (int i = 0; i < _smileDots.Count; i++)
            {
                if (_smileDots[i] != null)
                {
                    _smileDots[i].localPosition = Vector3.Lerp(
                        _smileStartPositions[i], _sadEndPositions[i], easedProgress);
                }
            }
        }

        if (elapsed >= corpseFadeDelay)
        {
            float fadeProgress = Mathf.Clamp01(
                (elapsed - corpseFadeDelay) / Mathf.Max(0.2f, corpseFadeDuration));
            SetFadeAlpha(1f - fadeProgress);

            if (fadeProgress >= 1f && !_renderersHidden)
            {
                _renderersHidden = true;
                foreach (Renderer renderer in _renderers)
                {
                    if (renderer != null)
                    {
                        renderer.enabled = false;
                    }
                }

                // The server removes its entity at seven seconds. Remove the visual object
                // at the same point; the world snapshot will then clear the stale dictionary entry.
                Destroy(gameObject);
            }
        }
    }

    private void PrepareFadeMaterials()
    {
        if (_fadePrepared)
        {
            return;
        }

        _fadePrepared = true;
        _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in _renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            // Renderer.materials creates per-renderer instances, so fading a corpse never
            // makes another monster using the same source material transparent.
            Material[] materials = renderer.materials;
            foreach (Material material in materials)
            {
                if (material == null)
                {
                    continue;
                }

                ConfigureTransparentMaterial(material);
                Color original = material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor")
                    : material.color;
                _fadeMaterials.Add(new FadeMaterial
                {
                    Material = material,
                    OriginalColor = original
                });
            }
        }
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHATEST_ON");
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private void SetFadeAlpha(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        foreach (FadeMaterial state in _fadeMaterials)
        {
            if (state.Material == null)
            {
                continue;
            }

            Color faded = state.OriginalColor;
            faded.a *= alpha;
            if (state.Material.HasProperty("_BaseColor"))
            {
                state.Material.SetColor("_BaseColor", faded);
            }
            if (state.Material.HasProperty("_Color"))
            {
                state.Material.SetColor("_Color", faded);
            }
            state.Material.color = faded;
        }
    }
}
