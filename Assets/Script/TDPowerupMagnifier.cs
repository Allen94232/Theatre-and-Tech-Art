using UnityEngine;

[AddComponentMenu("TD/Powerups/TDPowerupMagnifier")]
public class TDPowerupMagnifier : TDPowerupPickup
{
    [Header("Balance")]
    [SerializeField] float _radiusMultiplier = 1.5f;
    [SerializeField] float _durationSeconds = 8f;

    public float RadiusMultiplier => _radiusMultiplier;
    public float DurationSeconds => _durationSeconds;

    public override TDPowerupKind Kind => TDPowerupKind.Grow;

    public override void Apply(TDTableReceiver receiver, int playerIndex)
    {
        if (receiver == null)
            return;

        receiver.ApplyGrowPowerup(playerIndex, _radiusMultiplier, _durationSeconds);
    }
}
