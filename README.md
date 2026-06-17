# Cosmic Claw

Play the **Cosmic Predator**'s Magitek *Claw Sweep* animation on demand once, or on
a loop while you're riding it. Purely cosmetic and **local-only**: nothing is sent to
the server, so there's no unlock to earn and nothing for other players' clients to
validate.

Distributed as the **HClawsweep** plugin.

## Commands

- `/clawsweep` plays the claw sweep once, then settles back to idle.
- `/clawsweeploop` toggles a continuous sweep. Run it again (or dismount) to stop; moving
  around won't interrupt it.

You must be mounted on a Magitek Predator (Cosmic or standard); otherwise the command tells
you so.

## Installing

### From the custom repo (recommended)

1. In game, open `/xlsettings` → **Experimental** → **Custom Plugin Repositories**.
2. Paste this URL into a new row, click the **+**, then **Save**:

   ```
   https://raw.githubusercontent.com/Enceladeum/HClawsweep/main/repo.json
   ```
3. Open the plugin installer (`/xlplugins`), search for **Cosmic Claw**, and click
   **Install**.

### Local dev build

1. Build it (see **Building**). The output folder gets `HClawsweep.dll` and a generated
   `HClawsweep.json` manifest.
2. In game, `/xlsettings` → **Experimental** → **Dev Plugin Locations**. Add the path to
   the built `HClawsweep.dll` (or its folder), save, and hit the reload/scan button.
3. **Cosmic Claw** appears in **Installed Dev Plugins**; enable it.

## How it works

The Magitek Predator's claw sweep is `ActionTimeline` row **15140**. While you're mounted
(the Cosmic Predator is Mount row **440**; the standard Magitek Predator is row **121**, a
reskin that shares the same animations), the plugin plays that timeline directly on the
**ridden mount object** via `TimelineContainer.PlayActionTimeline(intro, loop)`:

- **single** is `PlayActionTimeline(15140, 0)`: the sweep plays once, then the loop slot
  settles back to base/idle.
- **loop** keeps the sweep going. Row 15140 is a one-shot timeline that does not
  self-repeat, so a plain `PlayActionTimeline(15140, 15140)` plays it through only once
  and stops. Instead the plugin watches the mount's animation slots from the framework
  update and re-asserts the sweep whenever it has left every slot. Re-asserting after the
  fact, rather than fighting the animation controller each frame, is what lets running,
  jumping and rotating play out without ever interrupting the loop.

No action request is sent to the server, so there's no grant or unlock that can fail; the
animation just plays on your client. By the same token other players don't see it: a
purely client-side timeline is never broadcast, and appearance-sync tools (Mare, Lightless
and the like) sync files and glamour, not local animation events.

The companion **Predatory Dash** is deliberately *not* implemented: it's a displacement (a
position change), and there's no server-safe way to author that purely client-side.

Everything that touches game memory runs on the framework thread, and the plugin drops any
running loop back to idle when it unloads.

## Building

```
dotnet build -c Release
```

Requires the Dalamud dev environment (the `Dalamud.NET.Sdk` resolves the game
references). The `Dalamud.NET.Sdk` version in the `.csproj` must match your installed
Dalamud (15.x here); the SDK generates the manifest and stamps the API level — there is
no hand-written `.json`. Load `bin/Release/HClawsweep.dll` as a dev plugin.

## Caveats

- Only works while you're mounted on a **Magitek Predator** (Cosmic row 440 or standard
  row 121).
- Other players can't see the sweep. It plays only on your client; there's no server-side
  trigger for a sync tool to relay, so VFX sync won't carry it to a friend.
- The data values (`Mount` 440/121, `ActionTimeline` 15140) are game-data specific; if a
  patch shuffles them and the sweep stops playing, those are the first things to
  re-check.

## License

MIT — see [LICENSE](LICENSE).
