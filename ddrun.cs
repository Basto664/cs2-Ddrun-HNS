using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace DDRun;

public class _ : BasePlugin
{
    public override string ModuleName => "DDRun";
    public override string ModuleAuthor => "Fi4";
    public override string ModuleVersion => "0.2";

    private static readonly byte ServerTickRate = 64;
    private const float DuckHeight = 17.5f;
    private const float PlayerHeight = 72;
    private const float NormalDuckSpeed = 6.023437f; //6.023437f
    private List<CCSPlayerController> _players = null!;
    private readonly ulong[] _whenUserDuck = new ulong[64];
    private readonly bool[] _isPlayerStandUp = new bool[64];

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    private void OnTick()
    {
        GetPlayers();
        foreach (var id in _players)
        {
            if (id == null! || !id.IsValid) continue;
            var pawn = id.PlayerPawn.Value;
            if (!id.PawnIsAlive || pawn == null || pawn.MovementServices == null || pawn.MoveType == MoveType_t.MOVETYPE_OBSOLETE) continue;
            var idMove = pawn.MovementServices;
            var isOnGround = pawn.OnGroundLastTick;
            var whenDuckButton = idMove.ButtonPressedCmdNumber[2];
            switch (isOnGround)
            {
                case false when whenDuckButton != _whenUserDuck[id.Slot]:
                case true when (id.Buttons & PlayerButtons.Duck) != 0:
                case true when ((PlayerFlags)pawn.Flags & PlayerFlags.FL_DUCKING) == PlayerFlags.FL_DUCKING:
                    ChangePlayerStatus();
                    break;
                case true when whenDuckButton != _whenUserDuck[id.Slot]:
                    ChangePlayerStatus();
                    var ddHeight = GiveTrueDdHeight(id);
                    _isPlayerStandUp[id.Slot] = true;
                    new CCSPlayer_MovementServices(idMove.Handle).Ducking = true;
                    Server.NextFrame(() =>
                    {
                        ChangePlayerStatus();
                        pawn.AbsVelocity.Z += ddHeight * ServerTickRate;
                        Server.NextFrame(() =>
                        {
                            ChangePlayerStatus();
                            Server.NextFrame(() =>
                            {
                                pawn.AbsVelocity.Z += -ddHeight * ServerTickRate;
                                ChangePlayerStatus();
                                pawn.AbsVelocity.Z = 0;
                                Server.NextFrame(() => _isPlayerStandUp[id.Slot] = false);
                            });
                        });
                    });
                    break;
            }

            var duckSpeed = NormalDuckSpeed;
            if (_isPlayerStandUp[id.Slot])
                duckSpeed *= ServerTickRate;

            new CCSPlayer_MovementServices(idMove.Handle).DuckSpeed = duckSpeed;
            continue;

            void ChangePlayerStatus()
            {
                _whenUserDuck[id.Slot] = whenDuckButton;
                isOnGround = false;
            }

        }
    }
    private float GiveTrueDdHeight(CCSPlayerController id)
    {
        var idPlayerPawn = id.PlayerPawn.Value;
        float ddRunHeight = DuckHeight;
        if(idPlayerPawn == null || idPlayerPawn.AbsOrigin == null) return ddRunHeight;
        var origin = idPlayerPawn.AbsOrigin!;

        foreach (var pawnOnHead in from onHeadPlayer in _players where onHeadPlayer.PawnIsAlive && onHeadPlayer != id select onHeadPlayer.PlayerPawn.Value)
        {
            if(pawnOnHead == null || pawnOnHead.AbsOrigin == null) continue;
            var originOnHead = pawnOnHead.AbsOrigin!;
            const float originTolerance = 32;
            const float zTolerance = PlayerHeight + DuckHeight;

            if (Math.Abs(originOnHead.X - origin.X) <= originTolerance
                && Math.Abs(originOnHead.Y - origin.Y) <= originTolerance
                && originOnHead.Z > origin.Z
                && Math.Abs(originOnHead.Z - origin.Z) <= zTolerance
                && Math.Abs(originOnHead.Z - origin.Z) >= PlayerHeight)
            {
                ddRunHeight -= zTolerance - (originOnHead.Z - origin.Z);
                return ddRunHeight > 0? ddRunHeight : 0;
            }
        }
        return ddRunHeight;
    }

    private byte _tickCache;
    private void GetPlayers()
    {
        const int intervalInSecond = 2;
        _tickCache++;
        if (_tickCache <= intervalInSecond*ServerTickRate && _players != null! && _players.Count != 0) return;
        _tickCache = 0;
        _players = Utilities.GetPlayers();
    }
}
