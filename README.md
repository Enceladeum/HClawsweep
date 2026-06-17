# Cosmic Claw

Play the **Cosmic Predator**'s Magitek *Claw Sweep* animation on demand — once, or on
a loop — while you're riding it. Purely cosmetic and **local-only**: nothing is sent to
the server, so there's no unlock to earn and nothing for other players' clients to
validate.

Distributed as the **HClawsweep** plugin.

## Commands

- `/clawsweep` — play the claw sweep **once**, then settle back to idle.
- `/clawsweeploop` — **toggle** a continuous sweep. Run it again, or just move, to stop.

You must be mounted on the Cosmic Predator; otherwise the command tells you so.

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

The Cosmic Predator's claw sweep is `ActionTimeline` row **15140**. While you're mounted
on it (Mount row **440**), the plugin plays that timeline directly on the **ridden mount
object** via `TimelineContainer.PlayActionTimeline(intro, loop)`:

- **single** → `PlayActionTimeline(15140, 0)`: the sweep plays once, then the loop slot
  falls back to base/idle.
- **loop** → `PlayActionTimeline(15140, 15140)`: the sweep slot repeats until you toggle
  off, dismount, or move.

No action request is sent to the server, so there's no grant/unlock that can fail — the
animation just plays on your client. The companion **Predatory Dash** is deliberately
*not* implemented: it's a displacement (position change), and there's no server-safe way
to author that purely client-side.

Everything that touches game memory runs on the framework thread, and the plugin drops
any running loop back to idle when it unloads.

## Building

```
dotnet build -c Release
```

Requires the Dalamud dev environment (the `Dalamud.NET.Sdk` resolves the game
references). The `Dalamud.NET.Sdk` version in the `.csproj` must match your installed
Dalamud (15.x here); the SDK generates the manifest and stamps the API level — there is
no hand-written `.json`. Load `bin/Release/HClawsweep.dll` as a dev plugin.

## Caveats

- Only works while you're mounted on the **Cosmic Predator**.
- The data values (`Mount` 440, `ActionTimeline` 15140) are game-data specific; if a
  patch shuffles them and the sweep stops playing, those are the first things to
  re-check.

## License

MIT — see [LICENSE](LICENSE).
