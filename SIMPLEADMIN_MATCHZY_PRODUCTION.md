# SimpleAdmin-first MatchZy deployment

This fork treats CS2-SimpleAdmin as the only authority for administrators,
permissions, immunity, bans, warnings, kicks, gags, mutes, RCON, and general
server administration. MatchZy owns match, veto, ready, pause, backup, demo,
and practice workflows.

## Runtime baseline

- .NET 10
- CounterStrikeSharp 1.0.371 or newer
- CS2-SimpleAdmin 1.8.3-bethechamp.1
- BETHECHAMP Match Control 0.8.19-bethechamp.1
- MenuManagerCore, PlayerSettings, AnyBaseLib, and their shared API assemblies

Use MySQL with a dedicated least-privilege database account for production or
multi-server installations. Use SQLite only for a single server.

## Command ownership

| Command | Owner | Purpose |
| --- | --- | --- |
| `!admin <reason>` | BETHECHAMP | Private player-to-staff call |
| `!adminmenu` | SimpleAdmin | Administration menu |
| `!last` | SimpleAdmin | Recently disconnected players |
| `!lastnade` | MatchZy | Teleport to the last practice grenade |
| `!map` | SimpleAdmin | General map change |
| `!matchmap` | MatchZy | Match-aware map change |
| `!rr`, `!restart` | MatchZy | Restart/reset the managed match |
| `!sarestart` | SimpleAdmin | Raw `mp_restartgame` action |

The two plugins must have zero duplicate `ConsoleCommand`/SimpleAdmin alias
registrations. Do not restore old aliases in `Commands.json`.

## Required SimpleAdmin flags for MatchZy

SimpleAdmin loads flags into CounterStrikeSharp's `AdminManager`; MatchZy reads
only that authority for match commands.

- `@css/config`: match configuration, force start/end, restart, restore
- `@css/map`: match/practice mode and MatchZy map operations
- `@custom/prac`: optional practice access
- `@css/reservation`: reserved server slot
- `@css/root`: all SimpleAdmin and MatchZy access

Keep `@css/rcon`, `@css/cvar`, `@css/root`, and plugin-manager access restricted
to owners.

## Production settings

In `CS2-SimpleAdmin.json`:

```json
{
  "EnableMetrics": false,
  "EnableUpdateCheck": true,
  "ExposeDatabaseConnectionStringToModules": false,
  "OtherSettings": {
    "DisableDangerousCommands": true
  }
}
```

Also:

1. Set an explicit timezone.
2. Decide whether `MultiServerMode` should make penalties global.
3. Configure MySQL TLS according to the database topology.
4. Use a dedicated Discord webhook and database user.
5. Back up the SimpleAdmin database before upgrades.
6. Do not install the SimpleAdmin FunCommands module with MatchZy practice mode.
7. Keep `matchzy_everyone_is_admin` set to `false`.

SimpleAdmin no longer stores the RCON password in `sa_servers`; loading this
version clears a previously stored value for the current server record.

## Upgrade behavior

SimpleAdmin migrates `Commands.json` to version 2:

- `css_admin` becomes `css_adminmenu`
- SimpleAdmin's restart aliases become `css_sarestart`
- `css_last` remains assigned to SimpleAdmin

MatchZy remote website moderation commands are dispatched to SimpleAdmin.
BETHECHAMP `MUTE` maps to a SimpleAdmin text gag, while BETHECHAMP `GAG` maps
to a SimpleAdmin voice mute. A website receipt confirms that the server
dispatched the SimpleAdmin command; it is not a second database transaction
confirmation, so monitor both plugin logs for rejected commands.

MatchZy's legacy `cfg/MatchZy/admins.json`, admin-chat handler, and raw RCON
handler are no longer authority paths. Move every administrator to
SimpleAdmin before deploying this build.

After deployment, restart the server rather than hot-reloading both plugins
simultaneously. Verify `css_plugins list`, then test the commands in the table
on a staging server before opening public slots.
