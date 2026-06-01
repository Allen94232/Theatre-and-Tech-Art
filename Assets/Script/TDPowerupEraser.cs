using UnityEngine;

[AddComponentMenu("TD/Powerups/TDPowerupEraser")]
public class TDPowerupEraser : TDPowerupPickup
{
    [Header("Balance")]
    [SerializeField] float _radiusMultiplier = 2.5f;

    public float RadiusMultiplier => _radiusMultiplier;

    public override TDPowerupKind Kind => TDPowerupKind.CleanseBurst;

    public override void Apply(TDTableReceiver receiver, int playerIndex)
    {
        if (receiver == null)
            return;

        receiver.ApplyCleanseBurstPowerup(playerIndex, _radiusMultiplier);
    }
}
