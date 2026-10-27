using UnityEngine;

public sealed class MonsterMotionPresentation : MonoBehaviour
{
    private const float Gravity = 9.81f;
    private bool _hasState;
    private bool _falling;
    private bool _paused;
    private float _velocity;
    private float _groundCenterY;

    public void ApplyServerState(Vector3 serverPosition, bool falling, float groundY, float velocity)
    {
        bool starting = !_hasState || !_falling;
        if (!falling)
        {
            transform.position = serverPosition;
            _falling = false;
            _paused = false;
            _velocity = 0f;
            _groundCenterY = serverPosition.y;
            _hasState = true;
            return;
        }

        _groundCenterY = groundY + Mathf.Abs(transform.localScale.y) * 0.5f;
        if (starting)
        {
            transform.position = serverPosition;
        }
        else
        {
            Vector3 current = transform.position;
            float y = Mathf.Abs(current.y - serverPosition.y) > 0.35f ? serverPosition.y : current.y;
            transform.position = new Vector3(serverPosition.x, y, serverPosition.z);
        }

        _velocity = IsFinite(velocity) ? velocity : 0f;
        _paused = !starting && Mathf.Abs(_velocity) < 0.01f && transform.position.y > _groundCenterY + 0.1f;
        _falling = true;
        _hasState = true;
    }

    public void Stop()
    {
        _falling = false;
        _paused = true;
        _velocity = 0f;
    }

    private void Update()
    {
        if (!_hasState || !_falling || _paused || Time.deltaTime <= 0f)
        {
            return;
        }

        float nextVelocity = _velocity - Gravity * Time.deltaTime;
        float nextY = transform.position.y + nextVelocity * Time.deltaTime;
        if (nextY <= _groundCenterY)
        {
            nextY = _groundCenterY;
            nextVelocity = 0f;
        }

        transform.position = new Vector3(transform.position.x, nextY, transform.position.z);
        _velocity = nextVelocity;
        if (_velocity == 0f && nextY > _groundCenterY + 0.1f)
        {
            _paused = true;
        }
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
