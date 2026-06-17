using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace HClawsweep;

// Cosmetic, local-only. Plays the Magitek Predator's Claw Sweep animation
// (ActionTimeline 15140) directly on the ridden mount object. No action request is
// sent to the server, so there is no grant/unlock to fail on. By the same token,
// other players' clients never see it: a purely client-side timeline is not broadcast,
// and appearance-sync tools (Mare/Lightless and friends) sync files and glamour, not
// local animation events.
//
// Works on the Cosmic Predator (Mount 440) and the standard Magitek Predator
// (Mount 121); 440 is a reskin of 121 and shares the same animations.
//
// Game field/method names below are confirmed against the FFXIVClientStructs source
// (Character.cs / MountContainer.cs / TimelineContainer.cs / ActionTimelineSequencer.cs
// / Control.cs):
//   Character.Mount                     : MountContainer          @ 0x670
//   MountContainer.MountId              : ushort                  @ 0x18  (0 when unmounted)
//   MountContainer.MountObject          : Character*              @ 0x10  (the ridden mount)
//   Character.Timeline                  : TimelineContainer       @ 0xA30
//   TimelineContainer.TimelineSequencer : ActionTimelineSequencer @ 0x10
//   TimelineContainer.PlayActionTimeline(ushort introId, ushort loopId = 0, void* = null)
//   ActionTimelineSequencer.GetSlotTimeline(uint slot) -> ushort (0 if the slot is idle)
//   Control.GetLocalPlayer() -> BattleChara* (a Character* at the same address)
//
// PlayActionTimeline(intro, loop): the intro plays, then the loop slot takes over.
//   loop = 0 -> after one sweep, settle back to base/idle. This is SINGLE, and it works.
//
// Looping is NOT just loop = 15140: the claw sweep is a one-shot timeline that does not
// self-repeat, so PlayActionTimeline(15140, 15140) only plays intro+loop once each (two
// sweeps) and then settles to idle. To loop "until toggled off" we re-assert the sweep
// from OnUpdate whenever it has left every animation slot. Re-asserting after the fact
// (rather than fighting the animation controller every frame) preserves the behaviour we
// want: running, jumping and rotating never interrupt the loop.
//
// Predatory Dash is deliberately NOT implemented: it is displacement, and there is no
// server-safe client-side way to author the position change.
public sealed unsafe class Plugin : IDalamudPlugin
{
    private const ushort CosmicPredatorMountId   = 440;    // Mount sheet row (reskin)
    private const ushort StandardPredatorMountId = 121;    // Mount sheet row (base, same anims)
    private const ushort ClawTimelineId          = 15140;  // ActionTimeline row (claw sweep)
    private const ushort IdleTimelineId          = 0;      // intro 0 / loop 0 => back to base
    private const uint   TimelineSlotCount       = 14;     // ActionTimelineSequencer slot count

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
            HelpMessage = "Single sweep"
        });
        _commands.AddHandler("/clawsweeploop", new CommandInfo(OnLoopToggle)
        {
            HelpMessage = "Play continuously until toggled off"
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
            var m = GetPredatorMount();
            if (m != null) StopClaw(m);
        });
    }

    // ── Commands ──────────────────────────────────────────────────────────────
    private void OnSingle(string _, string __)
        => _framework.RunOnFrameworkThread(() =>
        {
            _loopActive = false;                 // single and loop shouldn't fight
            var mount = GetPredatorMount();
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
                var m = GetPredatorMount();
                if (m != null) StopClaw(m);
                _log.Debug("[Cosmic Claw] loop OFF.");
                return;
            }

            var mount = GetPredatorMount();
            if (mount == null) { NotMounted(); return; }

            PlayClaw(mount, loop: true);
            _loopActive = true;
            _log.Debug("[Cosmic Claw] loop ON.");
        });

    // Drives the loop on the framework thread: re-assert the sweep once it settles back
    // to idle, and drop the toggle if the player dismounts (the animation stops itself).
    private void OnUpdate(IFramework framework)
    {
        if (!_loopActive) return;

        var mount = GetPredatorMount();
        if (mount == null) { _loopActive = false; return; }   // dismounted: stop tracking

        if (!IsClawPlaying(mount))
        {
            PlayClaw(mount, loop: true);
            _log.Debug("[Cosmic Claw] re-asserted loop (sweep had returned to idle).");
        }
    }

    // ── Core ────────────────────────────────────────────────────────────────────

    /// Ridden-mount Character* iff it's a Magitek Predator (Cosmic 440 or standard 121).
    private Character* GetPredatorMount()
    {
        var local = (Character*)Control.GetLocalPlayer();
        if (local == null) return null;

        var id = local->Mount.MountId;
        if (id != CosmicPredatorMountId && id != StandardPredatorMountId) return null;

        return local->Mount.MountObject;   // already a Character* (the ridden mount)
    }

    /// True while the claw sweep occupies any animation slot on the mount. Scanning every
    /// slot (instead of assuming which one it lands in) means we only re-assert once it has
    /// truly ended — never restarting it mid-sweep.
    private bool IsClawPlaying(Character* mount)
    {
        var seq = &mount->Timeline.TimelineSequencer;
        for (uint slot = 0; slot < TimelineSlotCount; slot++)
            if (seq->GetSlotTimeline(slot) == ClawTimelineId)
                return true;
        return false;
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
        => _chat.PrintError("[Cosmic Claw] Not mounted on a Magitek Predator (Cosmic or standard).");
}
