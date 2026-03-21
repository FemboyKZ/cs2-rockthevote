# CS2 RockTheVote (RTV)

CS2KZ Specific fork of [cs2-rockthevote](https://github.com/M-archand/cs2-rockthevote)

Map voting plugin for Counter-Strike 2 Kreedz. Maps only change when players vote, no automatic end-of-map voting or time limits.

## Features

- Reads from a custom maplist
- RTV Command. Players use `!rtv` in chat to trigger a map vote
- Nominate command (`!nom` / `!nominate`). Nominate a map to appear in the vote. Partial name matching, and conflicting map name resolution (`kz_gro`, `kz_grotto`). Configurable limit per player.

![nominate](https://github.com/user-attachments/assets/6ac056bc-9842-4422-ac0d-c7cd814b3ba6)

- Supports workshop maps, and custom map names. E.g. "`kz_grotto (T3, linear)`"
- `"Don't Change Map"` option in the RTV vote
- Map Chooser command. `!mapmenu` opens a menu with your map list, selected map is changed to immediately (flag restricted)
- Chat vote countdown

![chatcountdown](https://github.com/user-attachments/assets/803826a1-665b-4ab7-9e38-fbb0e8d702be)

- ChatMenu/CenterHtmlMenu/WasdMenu/ConsoleMenu for map vote and !nominate

![wasdmenu](https://github.com/user-attachments/assets/1df185bf-4313-4010-81de-98111ae383dc)
![chatmenu](https://github.com/user-attachments/assets/8d7e9ee8-b26e-47b1-89d8-ced96b13a392)

- Maplist Validator. Send to error log or Discord when a map is no longer available on the workshop.

![MapDiscordWebhook](https://github.com/user-attachments/assets/eaf8d706-abd1-4258-a7a3-b9cb44500802)
![WorkshopMapLog](https://github.com/user-attachments/assets/2f65dd9d-1ee9-4217-a753-81358973df2e)

- `!maps` command. List all maps available in the console.
  
![mapscommand](https://github.com/user-attachments/assets/d4ab1377-0b29-45b6-bdaa-06b6a7664751)

- `!reloadmaps` command. Rebuild the map list mid game
- `!reloadrtv` command. Reload the rtv config mid game
- Map change failure detection with automatic state reset (players can re-RTV if the map change fails)

## Requirements

- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) (Tested on v363)
- [CS2MenuManager](https://github.com/schwarper/CS2MenuManager) (Tested on v42)

## Installation

- Download the [latest release](https://github.com/FemboyKZ/cs2-rockthevote/releases)
- Extract the `RockTheVote.zip` file into `/game/csgo/`
- Update the `maplist.txt` to include your desired maps.

## [ Configuration ]

- A config file will be created in `addons/counterstrikesharp/configs/plugins/RockTheVote` the first time you load the plugin.
- Changes in the config file will require you to reload the plugin, restart the server, or using `!reloadrtv` (changing the map won't work).
- Maps used in RTV/nominate are located in `cfg/maplist.txt`. This can be updated mid-game using the command `!reloadmap`

```json
{
  "ConfigVersion": 25,
  "Rtv": {
    "Enabled": true,
    "MapChangeDelay": 5,
    "MapsToShow": 6,
    "ReminderInterval": 60,
    "MapVoteDuration": 60,
    "CooldownDuration": 30,
    "MapStartDelay": 30,
    "VotePercentage": 51
  },
  "MapVote": {
    "Enabled": true,
    "EnableRevote": true,
    "MapsToShow": 6,
    "MenuType": "ChatMenu",
    "VoteDuration": 90,
    "CountdownInterval": 15,
    "ChatMapChoiceReminder": true,
    "ChatMapChoiceInterval": 15,
    "MinWinPercentage": 0,
    "RunoffEnabled": true
  },
  "Nominate": {
    "Enabled": true,
    "MenuType": "ChatMenu",
    "NominateLimit": 1,
    "Permission": "",
    "ExternalNominatePermission": "@css/changemap"
  },
  "MapChooser": {
    "Command": "mapmenu,mm",
    "MenuType": "ChatMenu",
    "Permission": "@css/changemap"
  },
  "General": {
    "AdminPermission": "@css/root",
    "IncludeSpectator": true,
    "EnableMapValidation": true,
    "SteamApiKey": "",
    "DiscordWebhook": "",
    "KzTierMode": "classic"
  }
}
```
  
## Adding maps

```txt
surf_beginner:3070321829
surf_nyx (T1, Linear):3129698096
de_dust2
```

## Translations

| Language             |
| -------------------- |
| English              |

![GitHub Downloads](https://img.shields.io/github/downloads/FemboyKZ/cs2-rockthevote/total?style=for-the-badge)
