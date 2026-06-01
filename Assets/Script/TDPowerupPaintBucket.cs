using UnityEngine;

[AddComponentMenu("TD/Powerups/TDPowerupPaintBucket")]
public class TDPowerupPaintBucket : TDPowerupPickup
{
    [Header("Balance")]
    [SerializeField] float _radiusMultiplier = 2f;

    public float RadiusMultiplier => _radiusMultiplier;

    public override TDPowerupKind Kind => TDPowerupKind.PaintBurst;

    public override void Apply(TDTableReceiver receiver, int playerIndex)
    {
        if (receiver == null)
            return;

        receiver.ApplyPaintBurstPowerup(playerIndex, _radiusMultiplier);
    }
}
