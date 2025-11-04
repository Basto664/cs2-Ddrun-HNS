using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace DDRun;

public class _ : BasePlugin
{
    public override string ModuleName => "DDRun";
    public override string ModuleAuthor => "Fi4";
    public override string ModuleVersion => "0.1";
    private const byte ServerTickRate = 64;
    private const float DuckHeight = 20f;
    private const float NormalDuckSpeed = 6.023437f;//6.023437f

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    private byte _tickCache;
    private const byte TickMaxCache = 128;
    private List<CCSPlayerController> _player = null!;
    private readonly ulong[] _whenUserDuck = new ulong[64];

    private void OnTick()
    {
        if (_player == null! || _player.Count == 0)
            _player = Utilities.GetPlayers();

        foreach (var id in _player)
        {
            if (id == null! || !id.IsValid) continue;
            var idPlayerPawn = id.PlayerPawn.Value;
            if (!id.PawnIsAlive || idPlayerPawn == null || idPlayerPawn.MovementServices == null) continue;

            var idMove = idPlayerPawn.MovementServices;
            var isOnGround = idPlayerPawn.OnGroundLastTick;
            var whenDuckButton = idMove.ButtonPressedCmdNumber[2];

            switch (isOnGround)
            {
                case false when whenDuckButton != _whenUserDuck[id.Slot]:
                case true when (id.Buttons & PlayerButtons.Duck) != 0:
                case true when ((PlayerFlags)idPlayerPawn.Flags & PlayerFlags.FL_DUCKING) == PlayerFlags.FL_DUCKING:
                    ChangePlayerStatus();
                    Server.NextFrame(()=> new CCSPlayer_MovementServices(idMove.Handle).DuckSpeed = NormalDuckSpeed);
                    break;
                case true when whenDuckButton != _whenUserDuck[id.Slot]:
                    ChangePlayerStatus();
                    new CCSPlayer_MovementServices(idMove.Handle).DuckSpeed = NormalDuckSpeed * ServerTickRate;// * for normal sgs

                    idPlayerPawn.AbsVelocity.Z += DuckHeight * ServerTickRate;
                    Server.NextFrame(()=>
                    {
                        ChangePlayerStatus();
                        Server.NextFrame(() =>
                        {
                            ChangePlayerStatus();
                            idPlayerPawn.AbsVelocity.Z += -DuckHeight * ServerTickRate;
                        });
                    });
                    break;
            }
            continue;

            void ChangePlayerStatus()
            {
                _whenUserDuck[id.Slot] = whenDuckButton;
                isOnGround = false;
            }
        }

        _tickCache++;
        if (_tickCache <= TickMaxCache) return;
        _tickCache = 0;
        _player = Utilities.GetPlayers();
    }
}
