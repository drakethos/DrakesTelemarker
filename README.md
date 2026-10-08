
# DrakesTelemarker

Per-world console bookmarks (10 slots) to save and recall your position. Marks are stored per world under BepInEx config.

**Requires devcommands** — run `devcommands` in the console (or use a mod such as Server Devcommands on dedicated servers) before using teleport commands.

## Commands

Open the in-game console (F5) and type `telemark` for help. Common usage:

| Command | Description |
|--------|-------------|
| `telemark set mark <1-10>` | Save current position to slot |
| `telemark recall mark <1-10>` | Teleport to saved slot |
| `telemark list` | List marks for this world |
| `telemark clear mark <n>` | Clear one slot |
| `telemark clear all` | Clear all slots (confirmation UI) |

Aliases: `setmark`, `recallmark`.

Saved data: `BepInEx/config/DrakesTelemarker/TelemarkerMarks.json`

## Dependencies

- [BepInEx Pack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)
- **Soft:** [Server Devcommands](https://thunderstore.io/c/valheim/p/JereKuusela/Server_devcommands/) (recommended for dedicated servers)

## Install

1. Install with Gale, r2modman, or Thunderstore Mod Manager ("Import local mod" with the release zip), or extract it into:
   ```
   BepInEx/plugins/DrakeMods-DrakesTelemarker/
   ```
2. Ensure `DrakesTelemarker.dll`, `README.md`, `CHANGELOG.md`, and `icon.png` are in that folder.


Created per request on commission (if you would like to commission a mod hit me up info below, I have no price, just give me a donation you think is fair.)

Contact me:

    Want to drop a line tell me how I'm doing. -Report a bug (THATS NOT IN THE KNOWN ISSUES ALREADY), or a request for new features.

    I cannot guarantee the request will be met but if there's a high enough demand and the ask isn't too difficult I may take it into consideration. Email: Drakethos@gmail.com Discord: Drakethos

    discord server: https://discord.gg/cQegN9fB6r

    buy me a coffee ☕ https://paypal.me/Drakethos?country.x=US&locale.x=en_US

Credits: Used Cursor AI assitance to complete desired features Big sh