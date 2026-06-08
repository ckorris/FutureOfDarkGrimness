# Sound assets

Presentation sound cues load from this folder at runtime. Each cue maps to a filename:

| File          | Cue            | Beat                |
|---------------|----------------|---------------------|
| `gunshot.wav` | ranged shot    | `AttackBeat` (ranged) |
| `melee.wav`   | melee clash    | `AttackBeat` (melee)  |
| `dice.wav`    | dice clatter   | `DiceRolledBeat`    |
| `save.wav`    | deflection ping| `SaveBeat`          |
| `wound.wav`   | hurt grunt/thud| `ModelWoundedBeat`  |
| `death.wav`   | death thud     | `ModelDiedBeat`     |
| `banner.wav`  | stage sting    | `BannerBeat`        |
| `move.wav`    | footsteps      | `UnitMovedBeat`     |

Any file that's missing falls back to a built-in placeholder tone (see `AudioManager`), so the
pipeline is audible before real assets exist. Drop a `.wav` here with the matching name and it takes
over automatically — no code change. The mapping lives in `PresentationSoundCues.CueFor`.

`.wav` is safest with Raylib; `.ogg`/`.mp3` also load but the cue keys assume `.wav` filenames
(adjust `PresentationSoundCues.LoadInto` if you use another extension).
