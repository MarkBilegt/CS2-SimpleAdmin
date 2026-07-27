using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CS2_SimpleAdmin.Database;
using CS2_SimpleAdmin.Managers;
using CS2_SimpleAdmin.Menus;
using CS2_SimpleAdminApi;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Text.Json;

namespace CS2_SimpleAdmin;

[MinimumApiVersion(371)]
public partial class CS2_SimpleAdmin : BasePlugin, IPluginConfig<CS2_SimpleAdminConfig>
{
    internal static CS2_SimpleAdmin Instance { get; private set; } = new();

    public override string ModuleName => "CS2-SimpleAdmin" + (Helper.IsDebugBuild ? " (DEBUG)" : " (RELEASE)");
    public override string ModuleDescription => "Simple admin plugin for Counter-Strike 2 :)";
    public override string ModuleAuthor => "daffyy";
    public override string ModuleVersion => "1.8.3-bethechamp.2";
    
    public override void Load(bool hotReload)
    {
        Instance = this;
        if (hotReload)
        {
            ServerLoaded = false;
            _serverLoading = false;
            
            CacheManager?.Dispose();
            CacheManager = new CacheManager();
            
            // OnGameServerSteamAPIActivated();
            OnMapStart(string.Empty);

            AddTimer(6.0f, () =>
            {
                if (DatabaseProvider == null) return;
                
                PlayersInfo.Clear();
                CachedPlayers.Clear();
                BotPlayers.Clear();
                
                foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && p is { Connected: PlayerConnectedState.Connected, IsHLTV: false }).ToArray()) 
                {
                    if (!player.IsBot)
                        PlayerManager.LoadPlayerData(player, true);
                    else
                        BotPlayers.Add(player);
                };
            });
            
        }
        _cBasePlayerControllerSetPawnFunc = new MemoryFunctionVoid<CBasePlayerController, CCSPlayerPawn, bool, bool>(GameData.GetSignature("CBasePlayerController_SetPawn"));

        SimpleAdminApi = new Api.CS2_SimpleAdminApi();
        Capabilities.RegisterPluginCapability(ICS2_SimpleAdminApi.PluginCapability, () => SimpleAdminApi);

        PlayersTimer?.Kill();
        PlayersTimer = null;
        PlayerManager.CheckPlayersTimer();
        
        Menus.MenuManager.Instance.InitializeDefaultCategories();
        BasicMenu.Initialize();
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        try
        {
            MenuApi = MenuCapability.Get();
        }
        catch (Exception ex)
        {
            Logger.LogError("Unable to load required plugins ... \n{exception}", ex.Message);
            Unload(false);
        }

        RegisterEvents();
        AddTimer(0.5f, RegisterCommands.InitializeCommands);
        AddTimer(3.0f, () => ReloadAdmins(null));

        if (!CoreConfig.UnlockConCommands)
        {
            _logger?.LogError(
                $"⚠️ Warning: 'UnlockConCommands' is disabled in core.json. " +
                $"Players will not be automatically banned when kicked and will be able " +
                $"to rejoin the server for 60 seconds. " +
                $"To enable instant banning, set 'UnlockConCommands': true"
            );
        }
    }

    public void OnConfigParsed(CS2_SimpleAdminConfig config)
    {
        Helper.UpdateConfig(config);

        _logger = Logger;
        Config = config;

        bool missing = false;
        var cssPath = Path.Combine(Server.GameDirectory, "csgo", "addons", "counterstrikesharp");
        var pluginsPath = Path.Combine(cssPath, "plugins");
        var sharedPath = Path.Combine(cssPath, "shared");

        foreach (var plugin in _requiredPlugins)
        {
            var pluginDirPath = Path.Combine(pluginsPath, plugin);
            var pluginDllPath = Path.Combine(pluginDirPath, $"{plugin}.dll");

            if (!Directory.Exists(pluginDirPath))
            {
                _logger?.LogError(
                    $"❌ Plugin directory '{plugin}' missing at: {pluginDirPath}"
                );
                missing = true;
            }

            if (!File.Exists(pluginDllPath))
            {
                _logger?.LogError(
                    $"❌ Plugin DLL '{plugin}.dll' missing at: {pluginDllPath}"
                );
                missing = true;
            }
        }

        foreach (var shared in _requiredShared)
        {
            var sharedDirPath = Path.Combine(sharedPath, shared);
            var sharedDllPath = Path.Combine(sharedDirPath, $"{shared}.dll");

            if (!Directory.Exists(sharedDirPath))
            {
                _logger?.LogError(
                    $"❌ Shared library directory '{shared}' missing at: {sharedDirPath}"
                );
                missing = true;
            }

            if (!File.Exists(sharedDllPath))
            {
                _logger?.LogError(
                    $"❌ Shared library DLL '{shared}.dll' missing at: {sharedDllPath}"
                );
                missing = true;
            }
        }
        
        if (missing)
        {
            Server.ExecuteCommand($"css_plugins unload {ModuleDirectory}");
            return;
        }

        Instance = this;
        
    if (!Config.DatabaseConfig.DatabaseType.Equals("mysql", StringComparison.OrdinalIgnoreCase))
    {
        throw new Exception("[CS2-SimpleAdmin] This deployment requires DatabaseType=MySQL.");
    }

    ApplySharedDatabaseConfig(config.DatabaseConfig);
    if (string.IsNullOrWhiteSpace(config.DatabaseConfig.DatabaseHost) ||
        string.IsNullOrWhiteSpace(config.DatabaseConfig.DatabaseName) ||
        string.IsNullOrWhiteSpace(config.DatabaseConfig.DatabaseUser))
    {
        throw new Exception("[CS2-SimpleAdmin] You need to setup MySQL credentials in the shared database config!");
    }

    var builder = new MySqlConnectionStringBuilder()
    {
        Server = config.DatabaseConfig.DatabaseHost,
        Database = config.DatabaseConfig.DatabaseName,
        UserID = config.DatabaseConfig.DatabaseUser,
        Password = config.DatabaseConfig.DatabasePassword,
        Port = (uint)config.DatabaseConfig.DatabasePort,
        SslMode = Enum.TryParse(config.DatabaseConfig.DatabaseSSlMode, true, out MySqlSslMode sslMode)
                    ? sslMode
                    : MySqlSslMode.Preferred,
        Pooling = true,
    };

    DbConnectionString = builder.ConnectionString;
    DatabaseProvider = new MySqlDatabaseProvider(DbConnectionString);

    var (success, exception) = Task.Run(() => DatabaseProvider.CheckConnectionAsync()).GetAwaiter().GetResult();
    if (!success)
    {
        if (exception != null)
            Logger.LogError("Problem with database connection! \n{exception}", exception);

        Unload(false);
        return;
    }

        Task.Run(() => DatabaseProvider.DatabaseMigrationAsync());
        
        if (!Directory.Exists(ModuleDirectory + "/data"))
        {
            Directory.CreateDirectory(ModuleDirectory + "/data");
        }

        _localizer = Localizer;

        if (!string.IsNullOrEmpty(Config.Discord.DiscordLogWebhook))
            DiscordWebhookClientLog = new DiscordManager(Config.Discord.DiscordLogWebhook);

        if (Config.EnableUpdateCheck)
            Task.Run(async () => await PluginInfo.CheckVersion(ModuleVersion, Logger));
        
        PermissionManager = new PermissionManager(DatabaseProvider);
        BanManager = new BanManager(DatabaseProvider);
        MuteManager = new MuteManager(DatabaseProvider);
        WarnManager = new WarnManager(DatabaseProvider);
    }

    private sealed class SharedDatabaseConfig
    {
        public string DatabaseType { get; set; } = string.Empty;
        public string MySqlHost { get; set; } = string.Empty;
        public string MySqlDatabase { get; set; } = string.Empty;
        public string MySqlUsername { get; set; } = string.Empty;
        public string MySqlPassword { get; set; } = string.Empty;
        public int? MySqlPort { get; set; }
    }

    private static void ApplySharedDatabaseConfig(DatabaseConfig target)
    {
        string configuredPath = string.IsNullOrWhiteSpace(target.SharedDatabaseConfigPath)
            ? "csgo/cfg/MatchZy/database.json"
            : target.SharedDatabaseConfigPath.Trim();
        string path = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(Server.GameDirectory, configuredPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            throw new Exception($"[CS2-SimpleAdmin] Shared MySQL config was not found at '{path}'.");
        }

        SharedDatabaseConfig shared = JsonSerializer.Deserialize<SharedDatabaseConfig>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new Exception("[CS2-SimpleAdmin] Shared MySQL config is empty.");
        if (!string.Equals(shared.DatabaseType, "MySQL", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("[CS2-SimpleAdmin] Shared database requires DatabaseType=MySQL.");
        }

        if (string.IsNullOrWhiteSpace(target.DatabaseHost)) target.DatabaseHost = shared.MySqlHost;
        if (string.IsNullOrWhiteSpace(target.DatabaseName)) target.DatabaseName = shared.MySqlDatabase;
        if (string.IsNullOrWhiteSpace(target.DatabaseUser)) target.DatabaseUser = shared.MySqlUsername;
        if (string.IsNullOrWhiteSpace(target.DatabasePassword)) target.DatabasePassword = shared.MySqlPassword;
        if (shared.MySqlPort is > 0 and <= 65535) target.DatabasePort = shared.MySqlPort.Value;
    }

    internal static TargetResult? GetTarget(CommandInfo command, int argument = 1)
    {
        var matches = command.GetArgTargetResult(argument);

        if (!matches.Any())
        {
            command.ReplyToCommand($"Target {command.GetArg(argument)} not found.");
            return null;
        }

        if (command.GetArg(argument).StartsWith('@'))
            return matches;

        if (matches.Count() == 1)
            return matches;

        command.ReplyToCommand($"Multiple targets found for \"{command.GetArg(argument)}\".");
        return null;
    }

    public override void Unload(bool hotReload)
    {
        CacheManager?.Dispose();
        CacheManager = null;
        PlayersTimer?.Kill();
        PlayersTimer = null;
        
        UnregisterEvents();
        
        if (hotReload)
            PlayersInfo.Clear();
        else
            Server.ExecuteCommand($"css_plugins unload {ModuleDirectory}");
    }
}
