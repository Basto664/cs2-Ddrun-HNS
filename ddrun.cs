using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace DDRun;

public class _ : BasePlugin
{
    public override string ModuleName => "DDRun";
    public override string ModuleAuthor => "Fi4";
    public override string ModuleVersion => "0.6";

    private const byte ServerTickRate = 64;
    private const float DuckHeight = 17.5f;
    private const float PlayerHeight = 72;
    private const float NormalDuckSpeed = 6.023437f; //6.023437f
    private const float SgsTime = 0.15f;
    private readonly List<CCSPlayerController> _players = new();
    private readonly ulong[] _whenUserDuck = new ulong[64];
    private readonly float[] _whenUserStartDdRun = new float[64];

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnDisconnect);
    }

    private void OnTick()
    {
        foreach (var id in _players)
        {
            var pawn = id.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.Health <= 0) continue;

            var moveServices = pawn.MovementServices;
            if (moveServices == null || pawn.MoveType == MoveType_t.MOVETYPE_OBSOLETE) continue;

            var movement = new CCSPlayer_MovementServices(moveServices.Handle);
            var currentDuckCmd = movement.ButtonPressedCmdNumber[2];
            var slot = id.Slot;

            UpdateDuckSpeed(pawn, movement, slot);

            var isDucking = ((PlayerFlags)pawn.Flags & PlayerFlags.FL_DUCKING) == PlayerFlags.FL_DUCKING || (id.Buttons & PlayerButtons.Duck) != 0;
            var duckChanged = currentDuckCmd != _whenUserDuck[slot];

            if (!duckChanged) continue;
            _whenUserDuck[slot] = currentDuckCmd;
            if (!pawn.OnGroundLastTick || isDucking) continue;
            pawn.OnGroundLastTick = false;
            var ddHeight = GiveTrueDdHeight(id);
            _whenUserStartDdRun[id.Slot] = Server.CurrentTime + SgsTime;
            var speedOld = pawn.AbsVelocity.Length2D();
            Server.NextFrame(() =>
            {
                if (pawn.MoveType == MoveType_t.MOVETYPE_OBSOLETE) return;
                movement.Ducking = true;
                pawn.OnGroundLastTick = false;
                pawn.AbsVelocity.Z += ddHeight * ServerTickRate;
                Server.NextFrame(() =>
                {
                    pawn.OnGroundLastTick = false;
                    Server.NextFrame(() =>
                    {
                        pawn.OnGroundLastTick = false;
                        pawn.AbsVelocity.Z = 0;
                        if(((PlayerFlags)pawn.Flags & PlayerFlags.FL_DUCKING) == PlayerFlags.FL_DUCKING)
                        {
                            if (pawn.AbsVelocity.Length2D() - speedOld > 200)
                            {
                                pawn.AbsVelocity.X *= 0.25f;
                                pawn.AbsVelocity.Y *= 0.25f;
                            }
                        }
                        Server.NextFrame(() => pawn.AbsVelocity.Z -= ddHeight * 2);
                    });
                });
            });
        }
    }

    private void UpdateDuckSpeed(CCSPlayerPawn pawn, CCSPlayer_MovementServices movement, int slot)
    {
        var timeDiff = Server.CurrentTime - _whenUserStartDdRun[slot];

        if (timeDiff is < SgsTime * 2 and > 0)
        {
            movement.DuckSpeed = NormalDuckSpeed * ServerTickRate;

            var isDucking = ((PlayerFlags)pawn.Flags & PlayerFlags.FL_DUCKING) == PlayerFlags.FL_DUCKING;
            if (isDucking && timeDiff < SgsTime)
            {
                pawn.AbsVelocity.Z += DuckHeight;
                _whenUserStartDdRun[slot] -= SgsTime;
            }
        }
        else
        {
            movement.DuckSpeed = NormalDuckSpeed;
        }
    }

    private float GiveTrueDdHeight(CCSPlayerController id)
    {
        var pawn = id.PlayerPawn.Value;
        if (pawn == null) return DuckHeight;

        var pos = pawn.AbsOrigin;
        if (pos == null) return DuckHeight;

        var x = pos.X;
        var y = pos.Y;
        var z = pos.Z;

        const float originTolerance = DuckHeight*2;
        const float zTolerance = PlayerHeight + DuckHeight*2;

        foreach (var headPlayer in _players)
        {
            if (headPlayer == null! || headPlayer == id || !headPlayer.PawnIsAlive) continue;

            var pawnOnHead = headPlayer.PlayerPawn.Value;
            if (pawnOnHead == null) continue;

            var originOnHead = pawnOnHead.AbsOrigin;
            if (originOnHead == null) continue;

            var zDiff = originOnHead.Z - z;
            if (zDiff is < PlayerHeight or > zTolerance) continue;

            if (Math.Abs(originOnHead.X - x) > originTolerance) continue;
            if (Math.Abs(originOnHead.Y - y) > originTolerance) continue;

            // Если дошли сюда — игрок сверху
            var resultHeight = DuckHeight - (zTolerance - zDiff);
            return resultHeight > 0 ? resultHeight : 0;
        }

        return DuckHeight;
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo env)
    {
        var id = @event.Userid;
        if (id == null || !id.IsValid) return HookResult.Continue;
        _players.Add(id);

        return HookResult.Continue;
    }

    private HookResult OnDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var id = @event.Userid;
        if (id == null || !id.IsValid)
            return HookResult.Continue;
        _players.Remove(id);

        return HookResult.Continue;
    }
}
