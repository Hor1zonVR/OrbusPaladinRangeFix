# OrbusPaladinRangeFix — Paladin & AutoMount diagnostics

This repository starts with an **investigation build**, not a gameplay patch. The final objective is to make a Paladin hammer thrown beyond its permitted teleport range resolve to the furthest valid point **inside the existing range**, while respecting the game's normal collision, terrain, cooldown and teleport restrictions. **No extra range** is intended.

We're also investigating the game's mount/summon and combat-exit lifecycle for a separate optional `OrbusAutoMount` mod. That proposed mod should only call the normal client-side mounting interaction where the game permits it.

## v0.1.0 — read-only diagnostic

This version **does not modify teleporting, hammer behaviour, mounting, combat, networking, or movement**.

- **F8** starts a 60-second Paladin hammer/teleport recording.
- **F9** starts a 90-second mount/combat recording.
- **F10** queues a local-rig hierarchy/component snapshot.
- **F11** records a precise manual event marker when you throw, teleport, mount or dismount.

Open and focus the PC game window when pressing a key. Captures are saved under:
`<your RM instance>\BepInEx\MechanicsDiagnostics\Mechanics_*.txt`

The diagnostic inspects the local `PlayerRigv4`, Paladin/avatar and controller transforms, and gradually walks the active scene looking for likely mechanic objects. It processes only a small number of nodes per frame with hard caps, specifically to avoid the full-scene hitch that occurred in an earlier mod investigation. It does not inspect private networking or write gameplay state. The scene walk may miss dynamically spawned objects or names not matching its heuristic: that is something to refine from the first capture.

## How to test

1. Build and publish the DLL with `release.ps1` (below).
2. In **RebornManager → Instance → Client Mods**, install **OrbusPaladinRangeFix** from the GitHub release.
3. Launch Orbus on the Paladin. Press **F8**, then test a short throw + teleport, a near-maximum-range throw + teleport and an out-of-range throw + teleport. Make notes of the sequence, and use F11 at important moments if convenient.
4. Press **F9**, enter combat, exit combat, mount, dismount, enter combat mounted, and exit combat again. Allow the recording to end.
5. Close the game. Run `inspect.ps1` and share the generated report alongside BepInEx LogOutput.log. Object names may identify character-related information; review your log before sharing it.

The two recordings are separate: starting F9 during F8 intentionally ends the earlier recording. Wait for the first capture to finish.

## Publish workflow

Clone once:
```powershell
$repo = "$env:USERPROFILE\OneDrive\Documents\OrbusPaladinRangeFix"
git clone https://github.com/Hor1zonVR/OrbusPaladinRangeFix.git $repo
cd $repo
.\release.ps1
```

For future versions:
```powershell
git pull --ff-only
.\release.ps1
```

The script auto-detects usable interop from RebornManager instances or accepts `-GameDir 'C:\path\to\instance'`. It builds using .NET 6 references and publishes the exact compiled `OrbusPaladinRangeFix.dll` to a new GitHub Release; **it never writes to the game installation**. Needs `dotnet`, `git` and authenticated `gh`. Increment the plugin version before publishing another release. `-Preview` publishes a prerelease, which RebornManager normally does not offer.

**Compatibility has not yet been verified on any specific game edition.** Test only in a disposable modding instance. The first actual teleport correction and the separate AutoMount feature remain unimplemented pending diagnostic evidence.
