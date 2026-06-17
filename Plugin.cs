using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace HClawsweep;

// Cosmetic, local-only. Plays the Cosmic Predator's Magitek Claw sweep animation
// (ActionTimeline 15140 -> mount_sp/m0507/mon_sp001) directly on the ridden mount.
// No action request is sent to the server, so there is no grant/unlock to fail on.
//
// All game field/method names below are confirmed against the FFXIVClientStructs
// source (Character.cs / MountContainer.cs / TimelineContainer.cs / Control.cs):
//   Character.Mount      : MountContainer @ 0x670
//   MountContainer.MountId    : ushort     @ 0x18  (Character.IsMounted() == MountId != 0)
//   MountContainer.MountObject: Character*  @ 0x10  (the ridden mount object)
//   Character.Timeline   : TimelineContainer @ 0xA30
//   TimelineContainer.PlayActionTimeline(ushort introId, ushort loopId = 0, void* = null)
//   Control.GetLocalPlayer() -> BattleChara* (a Character* at the same address)
//
// PlayActionTimeline(intro, loop): the intro plays, then the loop slot takes over.
//   loop = 0           -> after one sweep, fall back to base/idle  (SINGLE)
//   loop = ClawTimeline-> the sweep repeats continuously           (LOOPED)
//
// Predatory Dash is deliberately NOT implemented: it is displacement (CastType 12,
// EffectRange 35, XAxisModifier 12) and there is no server-safe client-side way to
// author the position change.
public sealed unsafe class Plugin : IDalamudPlugin
{
    private const ushort CosmicPredatorMountId = 440;    // Mount sheet row
    private const ushort ClawTimelineId         = 15140;  // ActionTimeline row (claw sweep)
    private const ushort IdleTimelineId         = 0;      // intro 0 / loop 0 => back to base

    private readonly ICommandManager _commands;
    private readonly IFramework      _framework;
    private readonly IChatGui        _chat;
    private readonly IPluginLog      _log;

    private bool _loopActive;

    public Plugin(
        ICommandManager commands,
        IFramework      framework,
        IChatGui        chat,
        IPluginLog      log)
    {
        _commands  = commands;
        _framework = framework;
        _chat      = chat;
        _log       = log;

        _commands.AddHandler("/clawsweep", new CommandInfo(OnSingle)
        {
            HelpMessage = "Cosmic Predator claw sweep, once (cosmetic, local-only)."
        });
        _commands.AddHandler("/clawsweeploop", new CommandInfo(OnLoopToggle)
        {
            HelpMessage = "Toggle a continuous Cosmic Predator claw sweep (cosmetic, local-only)."
        });

        _framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        _commands.RemoveHandler("/clawsweep");
        _commands.RemoveHandler("/clawsweeploop");

        // best-effort: drop any running loop back to idle on unload.
        _loopActive = false;
        _framework.RunOnFrameworkThread(() =>
        {
            var m = GetCosmicPredatorMount();
            if (m != null) StopClaw(m);
        });
    }

    // ── Commands ──────────────────────────────────────────────────────────────
    private void OnSingle(string _, string __)
        => _framework.RunOnFrameworkThread(() =>
        {
            _loopActive = false;                 // single and loop shouldn't fight
            var mount = GetCosmicPredatorMount();
            if (mount == null) { NotMounted(); return; }

            PlayClaw(mount, loop: false);
            _log.Debug($"[Cosmic Claw] single sweep on mount 0x{(nint)mount:X}");
        });

    private void OnLoopToggle(string _, string __)
        => _framework.RunOnFrameworkThread(() =>
        {
            if (_loopActive)
            {
                _loopActive = false;
                var m = GetCosmicPredatorMount();
                if (m != null) StopClaw(m);
                _chat.Print("[Cosmic Claw] loop OFF.");
                return;
            }

            var mount = GetCosmicPredatorMount();
            if (mount == null) { NotMounted(); return; }

            PlayClaw(mount, loop: true);
            _loopActive = true;
            _chat.Print("[Cosmic Claw] loop ON (run again, or move, to stop).");
        });

    // Keep the toggle state honest if the player dismounts mid-loop.
    private void OnUpdate(IFramework framework)
    {
        if (_loopActive && GetCosmicPredatorMount() == null)
            _loopActive = false;
    }

    // ── Core ────────────────────────────────────────────────────────────────────

    /// Ridden-mount Character* iff it's the Cosmic Predator, else null.
    private Character* GetCosmicPredatorMount()
    {
        var local = (Character*)Control.GetLocalPlayer();
        if (local == null) return null;

        if (local->Mount.MountId != CosmicPredatorMountId) return null;

        return local->Mount.MountObject;   // already a Character* (the ridden mount)
    }

    private void PlayClaw(Character* mount, bool loop)
    {
        if (mount == null) return;
        mount->Timeline.PlayActionTimeline(ClawTimelineId, loop ? ClawTimelineId : (ushort)0);
    }

    private void StopClaw(Character* mount)
    {
        if (mount == null) return;
        mount->Timeline.PlayActionTimeline(IdleTimelineId);   // intro 0 / loop 0 => base
    }

    private void NotMounted()
        => _chat.PrintError("[Cosmic Claw] Not mounted on the Cosmic Predator.");
}
