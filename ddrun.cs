using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace DDRun;

public class _ : BasePlugin
{
    public override string ModuleName => "DDRun";
    public override string ModuleAuthor => "Fi4";
    public override string ModuleVersion => "0.7";

    private const byte ServerTickRate = 64;
    private const float DuckHeight = 18;
    private const float PlayerHeight = 72;
    private const float NormalDuckSpeed = 6.023437f;
    private const float SgsTime = 0.18f;
    private readonly List<CCSPlayerController> _players = new();
    private PlayerData?[] Users = new PlayerData?[66];

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
            if (!id.IsValid) continue;
            if (Users[id.Slot] == null) continue;;
            if (!id.Pawn.IsValid) continue;
            if (id.Pawn.Value == null) continue;
            if (!id.Pawn.Value.IsValid) continue;
            if (id.Pawn.Value.Health <= 0) continue;
            var user = Users[id.Slot]!;
            var pawn = id.Pawn.Value;

            var moveServices = pawn.MovementServices;
            if (moveServices == null || moveServices.Handle == IntPtr.Zero || pawn.MoveType == MoveType_t.MOVETYPE_OBSOLETE) continue;

            var movement = new CCSPlayer_MovementServices(moveServices.Handle);
            if (movement == null! || movement.Handle == IntPtr.Zero)
                continue;
            var currentDuckCmd = movement.ButtonPressedCmdNumber[2];

            UpdateDuckSpeed(pawn, movement, id.Slot);
            var duckChanged = currentDuckCmd != user.WhenUserDuck;
            switch (user.DuckTick)
            {
                case 1:
                    if (pawn.MoveType == MoveType_t.MOVETYPE_OBSOLETE)
                    {
                        user.DuckTick = 0;
                        continue;
                    }
                    movement.Ducking = true;
                    user.CurrentDuckHeight = GiveTrueDdHeight(id);
                    user.DuckTick = 2;
                    pawn.AbsVelocity.Z += user.CurrentDuckHeight * ServerTickRate;
                    continue;
                case 2:
                    user.DuckTick = 3;
                    continue;
                case 3:
                    if(pawn.AbsVelocity.Z > 0)
                        pawn.AbsVelocity.Z = 0;
                    if (((PlayerFlags)pawn.Flags & PlayerFlags.FL_DUCKING) == PlayerFlags.FL_DUCKING)
                    {
                        if (pawn.AbsVelocity.Length2D() - user.SpeedBeforeDuck > 200)
                        {
                            pawn.AbsVelocity.X *= 0.25f;
                            pawn.AbsVelocity.Y *= 0.25f;
                        }
                    }
                    user.DuckTick = 4;
                    continue;
                case 4:
                    user.DuckTick = 0;
                    pawn.AbsVelocity.Z -= user.CurrentDuckHeight * 2;
                    continue;
            }

            if (!duckChanged) continue;
            user.WhenUserDuck = currentDuckCmd;
            var isDucking = (pawn.Flags & (uint)PlayerFlags.FL_DUCKING) != 0 || (id.Buttons & PlayerButtons.Duck) != 0;
            if ((pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) == 0 || isDucking) continue;
            user.WhenUserStartDDRun = Server.CurrentTime + SgsTime;
            user.SpeedBeforeDuck = pawn.AbsVelocity.Length2D();
            user.DuckTick = 1;
            pawn.AbsVelocity.Z = 0;
        }
    }

    private void UpdateDuckSpeed(CBasePlayerPawn pawn, CCSPlayer_MovementServices movement, int slot)
    {
        var timeDiff = Server.CurrentTime - Users[slot]!.WhenUserStartDDRun;

        if (timeDiff is < SgsTime * 2 and > 0)
        {
            movement.DuckSpeed = NormalDuckSpeed * ServerTickRate;

            var isDucking = ((PlayerFlags)pawn.Flags & PlayerFlags.FL_DUCKING) == PlayerFlags.FL_DUCKING;
            if (isDucking && timeDiff < SgsTime)
            {
                pawn.AbsVelocity.Z += DuckHeight;
                Users[slot]!.WhenUserStartDDRun -= SgsTime;
            }
        }
        else
        {
            movement.DuckSpeed = NormalDuckSpeed;
        }
    }

    private float GiveTrueDdHeight(CCSPlayerController id)
    {
        var pawn = id.Pawn.Value;
        if (pawn == null || !pawn.IsValid) return DuckHeight;

        var pos = pawn.AbsOrigin;
        if (pos == null) return DuckHeight;

        var x = pos.X;
        var y = pos.Y;
        var z = pos.Z;

        const float originTolerance = DuckHeight * 2;
        const float zTolerance = PlayerHeight + DuckHeight * 2;

        foreach (var headPlayer in _players)
        {
            if (headPlayer == null! || headPlayer == id || !headPlayer.PawnIsAlive) continue;

            var headPawn = headPlayer.Pawn.Value;
            if (headPawn == null || !headPawn.IsValid) continue;

            var originOnHead = headPawn.AbsOrigin;
            if (originOnHead == null) continue;

            var zDiff = originOnHead.Z - z;
            if (zDiff is < PlayerHeight or > zTolerance) continue;

            if (Math.Abs(originOnHead.X - x) > originTolerance) continue;
            if (Math.Abs(originOnHead.Y - y) > originTolerance) continue;

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
        Users[id.Slot] = new PlayerData();

        return HookResult.Continue;
    }

    private HookResult OnDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var id = @event.Userid;
        if (id == null || !id.IsValid)
            return HookResult.Continue;
        _players.Remove(id);
        Users[id.Slot] = null;

        return HookResult.Continue;
    }
}
public class PlayerData
{
    public byte DuckTick { get; set; }
    public ulong WhenUserDuck { get; set; }
    public float WhenUserStartDDRun { get; set; }
    public float CurrentDuckHeight { get; set; }
    public float SpeedBeforeDuck { get; set; }
}
