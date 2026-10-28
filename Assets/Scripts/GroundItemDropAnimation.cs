using UnityEngine;

/// <summary>
/// Client-side presentation for a newly visible ground item. The server remains authoritative
/// for the landing position; this component only animates the item down from a short drop.
/// </summary>
[DisallowMultipleComponent]
public sealed class GroundItemDropAnimation : MonoBehaviour
{
    private Vector3 restingPosition;
    private float dropHeight;
    private float duration;
    private float elapsed;

    public bool IsFalling { get; private set; }

    public void Begin(Vector3 targetPosition, float height = 1f, float fallDuration = 0.45f)
    {
        restingPosition = targetPosition;
        dropHeight = Mathf.Max(0f, height);
        duration = Mathf.Max(0.01f, fallDuration);
        elapsed = 0f;
        IsFalling = dropHeight > 0f;

        transform.position = restingPosition + Vector3.up * dropHeight;
        if (!IsFalling)
        {
            transform.position = restingPosition;
        }
    }

    public void SetRestingPosition(Vector3 targetPosition)
    {
        restingPosition = targetPosition;
        if (!IsFalling)
        {
            transform.position = restingPosition;
            return;
        }

        float progress = Mathf.Clamp01(elapsed / duration);
        float remainingHeight = dropHeight * (1f - progress * progress);
        transform.position = restingPosition + Vector3.up * remainingHeight;
    }

    private void Update()
    {
        if (!IsFalling)
        {
            return;
        }

        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        float remainingHeight = dropHeight * (1f - progress * progress);
        transform.position = restingPosition + Vector3.up * remainingHeight;

        if (progress >= 1f)
        {
            IsFalling = false;
            transform.position = restingPosition;
        }
    }
}
