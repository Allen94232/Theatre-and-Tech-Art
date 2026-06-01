using UnityEngine;

public abstract class TDPowerupPickup : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] float _bobHeight = 0.08f;
    [SerializeField] float _bobSpeed = 2.2f;
    [SerializeField] float _spinSpeed = 80f;

    [Header("Pickup Range")]
    [SerializeField] Vector2 _pickupRectSize = new Vector2(1.2f, 1.2f);

    [Header("Scene Debug")]
    [SerializeField] bool _showPickupRangeGizmo = true;
    [SerializeField] bool _drawOnlyWhenSelected = false;
    [SerializeField] bool _drawOnXZPlane = false;
    [SerializeField] Color _pickupRangeGizmoColor = new Color(0.2f, 1f, 1f, 0.8f);

    Vector3 _basePosition;
    float _phase;
    Vector2 _arenaPosition;

    public abstract TDPowerupKind Kind { get; }
    public Vector2 ArenaPosition => _arenaPosition;
    public Vector2 PickupRectSize => new Vector2(
        Mathf.Max(0.05f, _pickupRectSize.x),
        Mathf.Max(0.05f, _pickupRectSize.y)
    );

    protected virtual void OnEnable()
    {
        _basePosition = transform.position;
        _phase = Random.Range(0f, Mathf.PI * 2f);
    }

    protected virtual void Update()
    {
        var yOffset = Mathf.Sin(Time.time * _bobSpeed + _phase) * _bobHeight;
        transform.position = _basePosition + new Vector3(0f, yOffset, 0f);

        if (Mathf.Abs(_spinSpeed) > 0.01f)
            transform.Rotate(0f, 0f, _spinSpeed * Time.deltaTime, Space.Self);
    }

    public void Place(Vector3 worldPosition)
    {
        Place(worldPosition, new Vector2(worldPosition.x, worldPosition.y));
    }

    public void Place(Vector3 worldPosition, Vector2 arenaPosition)
    {
        _basePosition = worldPosition;
        transform.position = worldPosition;
        _arenaPosition = arenaPosition;
    }

    protected virtual void OnValidate()
    {
        _pickupRectSize.x = Mathf.Max(0.05f, _pickupRectSize.x);
        _pickupRectSize.y = Mathf.Max(0.05f, _pickupRectSize.y);
        _pickupRangeGizmoColor.a = Mathf.Clamp01(_pickupRangeGizmoColor.a);
    }

    void OnDrawGizmos()
    {
        if (!_showPickupRangeGizmo || _drawOnlyWhenSelected)
            return;

        DrawPickupRangeGizmo();
    }

    void OnDrawGizmosSelected()
    {
        if (!_showPickupRangeGizmo)
            return;

        DrawPickupRangeGizmo();
    }

    void DrawPickupRangeGizmo()
    {
        var size = PickupRectSize;
        var halfX = size.x * 0.5f;
        var halfY = size.y * 0.5f;

        var center = transform.position;
        Vector3 p1;
        Vector3 p2;
        Vector3 p3;
        Vector3 p4;

        if (_drawOnXZPlane)
        {
            p1 = new Vector3(center.x - halfX, center.y, center.z - halfY);
            p2 = new Vector3(center.x + halfX, center.y, center.z - halfY);
            p3 = new Vector3(center.x + halfX, center.y, center.z + halfY);
            p4 = new Vector3(center.x - halfX, center.y, center.z + halfY);
        }
        else
        {
            p1 = new Vector3(center.x - halfX, center.y - halfY, center.z);
            p2 = new Vector3(center.x + halfX, center.y - halfY, center.z);
            p3 = new Vector3(center.x + halfX, center.y + halfY, center.z);
            p4 = new Vector3(center.x - halfX, center.y + halfY, center.z);
        }

        Gizmos.color = _pickupRangeGizmoColor;
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);
    }

    public abstract void Apply(TDTableReceiver receiver, int playerIndex);
}
