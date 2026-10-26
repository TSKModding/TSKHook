# TSKHook

## [中文](README_TC.md)

Twinkle Star Knights mod for DMM Game Player version

## Feature

1. Game speed (more fun if Ready Go is faster, is not?^^)
   ![3x speed](./img/3x.gif)
2. In-game Screenshot
3. FPS setting
4. [Translation](Translation.md) (with optional Simplified/Traditional Chinese conversion)
5. Game window size setting
6. Picture Book zoom ratio
7. UI text and image translation with independent resource updates and rollback
8. Optional text capture and local translation preview

## Requirement

1. Windows 10 or newer
2. Twinkle Star Knights DMM Game Player version

## Installation

Download and extract [Release](https://github.com/TSKModding/TSKHook/releases) zip to your Twinkle Star Knights install
location `C:\Users\<username>\Twinkle_StarKnightsX`

For in-game settings, install the [IL2CPP ConfigurationManager](https://github.com/anosu/BepInEx.ConfigurationManager/releases) (`ConfigurationManager.dll` and `BepInEx.KeyboardShortcut.dll`). UI resources are maintained in [TSKHook-UITranslations](https://github.com/Azure-Sora/TSKHook-UITranslations).

## Config

Press F1 to open ConfigurationManager when installed, or reload configuration when it is absent. You can also edit the generated configuration in `BepInEx/config` while the game is closed. Legacy `plugins/config.json` is imported once. Use Maintenance to reload configuration or check, reload and roll back resource packages.

| Name      | Default Value | Description                                                  |
|-----------|---------------|--------------------------------------------------------------|
| General/SpeedStep | 0.5           | Increase/Decrease game speed each step (per click)           |
| General/FPS | 60            | Override FPS setting, take effects when value bigger than 60 |
| Translation/Enabled | true          | Enable/Disable translation feature                           |
| General/WindowWidth | 1280          | Game window width                                            |
| General/WindowHeight | 720           | Game window height                                           |
| General/CharacterZoom | 1.0           | Character standing zoom in/out ratio                         |
| Translation/UIEnabled | true | Enable UI translation |
| Translation/ImagesEnabled | true | Enable UI image replacement |
| Translation/ChineseScript | Original | Keep translations unchanged, or convert to Simplified/Traditional Chinese |
| Capture/Enabled | false | Save captured text to `plugins/TSKHook/ui_capture` |
| Updates/Enabled | true | Check text and image packages once at game startup; retain local resources on failure |
| Updates/LocalPreview | false | Read `plugins/TSKHook/ui_preview.jsonl` and pause public updates |

## Key binding

| Key  | Type        | Description                                                                   |
|------|-------------|-------------------------------------------------------------------------------|
| F1   | Settings    | Open ConfigurationManager if installed; otherwise reload configuration       |
| F5   | Freeze      | Freeze game, mean set game speed to 0x                                        |
| F6   | Reset       | Reset game speed to 1x/normal                                                 | 
| F7   | Decrease    | Decrease game speed (2-0.5 etc), depends on your `General/SpeedStep` config               |
| F8   | Increase    | Increase game speed (1+0.5 etc), depends on your `General/SpeedStep` config               |
| F10  | Translation | Clear story cache and reload UI translations                                   |
| F11  | Translation | Enable/Disable translation feature                                            |
| F12  | Screenshot  | Screenshot current frame and save to Pictures(`C:\Users\<username>\Pictures`) |
| Ctrl | Skip text   | Skip text via Ctrl button, just like Galgame control system                   |

## Contributing

You're free to contribute to TSKHook as long as the features are useful, such as battle stats log, 360 stamina alert or
something else, except modifying battle data.

## Disclaimer

Using TSKHook violates Twinkle Star Knights and DMM's terms of service.

I will NOT be held responsible for any bans!
