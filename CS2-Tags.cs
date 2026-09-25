using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Text;

namespace CS2_Tags;

[MinimumApiVersion(159)]
public class CS2_Tags : BasePlugin
{
	private HashSet<string> GaggedIds = new HashSet<string>();
	// Par slot : le vrai pseudo du joueur, et le pseudo (tag compris) que le plugin lui a donné
	private readonly Dictionary<int, string> RealNames = new();
	private readonly Dictionary<int, string> AppliedNames = new();
	// Par slot : dernier tag de clan effacé et nombre d'effacements (affichés par css_tags_debug)
	private readonly Dictionary<int, (string Clan, int Count)> ClearedClans = new();
	public static JObject? JsonTags { get; private set; }
	public override string ModuleName => "CS2-Tags";
	public override string ModuleDescription => "Add player tags easily in cs2 game";
	public override string ModuleAuthor => "daffyy";
	// Version définie dans CS2-Tags.csproj (VersionBase + numéro de compilation)
	public override string ModuleVersion => typeof(CS2_Tags).Assembly.GetName().Version!.ToString(3);

	public override void Load(bool hotReload)
	{
		CreateOrLoadJsonFile(ModuleDirectory + "/tags.json");

		RegisterListener<Listeners.OnMapStart>(OnMapStart);
		RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
		RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
		RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
		RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
		RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
		RegisterEventHandler<EventPlayerChangename>(OnPlayerChangeName);
		AddCommandListener("say", OnPlayerChat);
		AddCommandListener("say_team", OnPlayerChatTeam);

		// Joueurs déjà connectés (rechargement du plugin en cours de partie)
		AddTimer(1.0f, () => Utilities.GetPlayers().ForEach(ApplyScoreboardTag));

		// Le jeu peut remettre le tag de clan Steam à tout moment (données Steam du joueur reçues en différé)
		AddTimer(2.0f, () => Utilities.GetPlayers().ForEach(HideClanTag), TimerFlags.REPEAT);
	}

	public override void Unload(bool hotReload)
	{
		if (hotReload) return;

		// Plugin retiré : rendre leur vrai pseudo aux joueurs
		foreach (var player in Utilities.GetPlayers())
		{
			if (AppliedNames.TryGetValue(player.Slot, out var appliedName) && appliedName == player.PlayerName && RealNames.TryGetValue(player.Slot, out var realName))
				SetName(player, realName);
		}
	}

	private void OnMapStart(string mapName)
	{
		GaggedIds.Clear();
	}

	private static void CreateOrLoadJsonFile(string filepath)
	{
		if (!File.Exists(filepath))
		{
			var templateData = new JObject
			{
				["tags"] = new JObject
				{
					["#css/admin"] = new JObject
					{
						["prefix"] = "{RED}[ADMIN]",
						["nick_color"] = "{RED}",
						["message_color"] = "{GOLD}",
						["scoreboard"] = "[ADMIN]"
					},
					["@css/chat"] = new JObject
					{
						["prefix"] = "{GREEN}[CHAT]",
						["nick_color"] = "{RED}",
						["message_color"] = "{GOLD}",
						["scoreboard"] = "[CHAT]"
					},
					["76561197961430531"] = new JObject
					{
						["prefix"] = "{RED}[ADMIN]",
						["nick_color"] = "{RED}",
						["message_color"] = "{GOLD}",
						["scoreboard"] = "[ADMIN]"
					},
					["everyone"] = new JObject
					{
						["team_chat"] = false,
						["prefix"] = "{Grey}[Player]",
						["nick_color"] = "",
						["message_color"] = "",
						["scoreboard"] = "[Player]"
					},
				}
			};

			File.WriteAllText(filepath, templateData.ToString());
			var jsonData = File.ReadAllText(filepath);
			JsonTags = JObject.Parse(jsonData);
		}
		else
		{
			var jsonData = File.ReadAllText(filepath);
			JsonTags = JObject.Parse(jsonData);
		}
	}

	[ConsoleCommand("css_tags_reload")]
	public void OnReloadConfig(CCSPlayerController? player, CommandInfo info)
	{
		if (player != null) return;
		CreateOrLoadJsonFile(ModuleDirectory + "/tags.json");
		Utilities.GetPlayers().ForEach(ApplyScoreboardTag);

		Server.PrintToConsole("[CS2-Tags] Config reloaded!");
	}

	[ConsoleCommand("css_tags_debug")]
	[CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
	public void OnTagsDebugCommand(CCSPlayerController? caller, CommandInfo command)
	{
		foreach (var player in Utilities.GetPlayers())
		{
			string cleared = ClearedClans.TryGetValue(player.Slot, out var entry) ? $"'{entry.Clan}' x{entry.Count}" : "-";
			Server.PrintToConsole($"[CS2-Tags] #{player.Slot} {(player.IsBot ? "bot" : "joueur")} pseudo='{player.PlayerName}' clan='{player.Clan}' clanId={player.ClanId32bit} effacé={cleared}");
		}
	}

	[ConsoleCommand("css_tag_mute")]
	[CommandHelper(minArgs: 1, usage: "<SteamID>", whoCanExecute: CommandUsage.SERVER_ONLY)]
	public void OnTagMuteCommand(CCSPlayerController? caller, CommandInfo command)
	{
		string? steamid = command.GetArg(1);

		if (steamid.Length == 17)
		{
			if (!GaggedIds.Contains(steamid))
				GaggedIds.Add(steamid);
		}
	}

	[ConsoleCommand("css_tag_unmute")]
	[CommandHelper(minArgs: 1, usage: "<SteamID>", whoCanExecute: CommandUsage.SERVER_ONLY)]
	public void OnTagUnMuteCommand(CCSPlayerController? caller, CommandInfo command)
	{
		string? steamid = command.GetArg(1);

		if (steamid.Length == 17)
		{
			if (GaggedIds.Contains(steamid))
				GaggedIds.Remove(steamid);
		}
	}

	private void OnClientAuthorized(int playerSlot, SteamID steamId)
	{
		CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerSlot);

		if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) return;

		AddTimer(2.0f, () => ApplyScoreboardTag(player));
	}

	private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;

		if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) return HookResult.Continue;

		AddTimer(2.0f, () => ApplyScoreboardTag(player));

		return HookResult.Continue;
	}

	private void OnClientDisconnect(int playerSlot)
	{
		RealNames.Remove(playerSlot);
		AppliedNames.Remove(playerSlot);
		ClearedClans.Remove(playerSlot);

		CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerSlot);

		if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) return;

		GaggedIds.Remove(player.SteamID.ToString()!);
	}

	private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;
		if (player == null || !player.IsValid) return HookResult.Continue;

		AddTimer(1.5f, () => ApplyScoreboardTag(player));

		return HookResult.Continue;
	}

	private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;
		if (player == null || !player.IsValid) return HookResult.Continue;

		AddTimer(1.5f, () => ApplyScoreboardTag(player));

		return HookResult.Continue;
	}

	private HookResult OnPlayerChangeName(EventPlayerChangename @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;
		if (player == null || !player.IsValid) return HookResult.Continue;

		AddTimer(1.0f, () => ApplyScoreboardTag(player));

		return HookResult.Continue;
	}

	private HookResult OnPlayerChat(CCSPlayerController? player, CommandInfo info)
	{
		if (player == null || !player.IsValid || info.GetArg(1).Length == 0 || player.AuthorizedSteamID == null) return HookResult.Continue;
		string steamid = player.AuthorizedSteamID.SteamId64.ToString();

		if (player.SteamID.ToString() != "" && GaggedIds.Contains(player.SteamID.ToString())) return HookResult.Handled;

		if (info.GetArg(1).StartsWith("!") || info.GetArg(1).StartsWith("@") || info.GetArg(1).StartsWith("/") || info.GetArg(1).StartsWith(".") || info.GetArg(1) == "rtv") return HookResult.Continue;

		if (JsonTags != null && JsonTags.TryGetValue("tags", out var tags) && tags is JObject tagsObject)
		{
			string deadIcon = !player.PawnIsAlive ? $"{ChatColors.White}☠ {ChatColors.Default}" : "";

			if (tagsObject.TryGetValue(steamid, out var playerTag) && playerTag is JObject)
			{
				string prefix = playerTag["prefix"]?.ToString() ?? "";
				string nickColor = playerTag?["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
				string messageColor = playerTag?["message_color"]?.ToString() ?? ChatColors.Default.ToString();

				Server.PrintToChatAll(ReplaceTags($" {deadIcon}{prefix}{nickColor}{RealName(player)}{ChatColors.Default}: {messageColor}{info.GetArg(1)}", player.TeamNum));

				return HookResult.Handled;
			}

			foreach (var tagKey in tagsObject.Properties())
			{
				if (tagKey.Name.StartsWith("#"))
				{
					string group = tagKey.Name;
					bool inGroup = AdminManager.PlayerInGroup(player, group);

					if (inGroup)
					{
						if (tagsObject.TryGetValue(group, out var groupTag) && groupTag is JObject)
						{
							string prefix = groupTag["prefix"]?.ToString() ?? "";
							string nickColor = groupTag?["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
							string messageColor = groupTag?["message_color"]?.ToString() ?? ChatColors.Default.ToString();

							Server.PrintToChatAll(ReplaceTags($" {deadIcon}{prefix}{nickColor}{RealName(player)}{ChatColors.Default}: {messageColor}{info.GetArg(1)}", player.TeamNum));

							return HookResult.Handled;
						}
					}
				}

				if (tagKey.Name.StartsWith("@"))
				{
					string permission = tagKey.Name;
					bool hasPermission = AdminManager.PlayerHasPermissions(player, permission);

					if (hasPermission)
					{
						if (tagsObject.TryGetValue(permission, out var permissionTag) && permissionTag is JObject)
						{
							string prefix = permissionTag["prefix"]?.ToString() ?? "";
							string nickColor = permissionTag?["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
							string messageColor = permissionTag?["message_color"]?.ToString() ?? ChatColors.Default.ToString();

							Server.PrintToChatAll(ReplaceTags($" {deadIcon}{prefix}{nickColor}{RealName(player)}{ChatColors.Default}: {messageColor}{info.GetArg(1)}", player.TeamNum));

							return HookResult.Handled;
						}
					}
				}
			}

			if (tagsObject.TryGetValue("everyone", out var everyoneTag) && everyoneTag is JObject && everyoneTag?["team_chat"]?.Value<bool>() == true)
			{
				string prefix = everyoneTag["prefix"]?.ToString() ?? "";
				string nickColor = everyoneTag?["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
				string messageColor = everyoneTag?["message_color"]?.ToString() ?? ChatColors.Default.ToString();

				Server.PrintToChatAll(ReplaceTags($" {deadIcon}{prefix}{nickColor}{RealName(player)}{ChatColors.Default}: {messageColor}{info.GetArg(1)}", player.TeamNum));

				return HookResult.Handled;
			}
		}

		return HookResult.Continue;
	}

	private HookResult OnPlayerChatTeam(CCSPlayerController? player, CommandInfo info)
	{
		if (player == null || !player.IsValid || info.GetArg(1).Length == 0 || player.AuthorizedSteamID == null) return HookResult.Continue;
		string steamid = player.AuthorizedSteamID.SteamId64.ToString();

		if (player.SteamID.ToString() != "" && GaggedIds.Contains(player.SteamID.ToString())) return HookResult.Handled;

		if (info.GetArg(1).StartsWith("@") && AdminManager.PlayerHasPermissions(player, "@css/chat"))
		{
			foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV && AdminManager.PlayerHasPermissions(p, "@css/chat")))
			{
				p.PrintToChat($" {ChatColors.Lime}(ADMIN) {ChatColors.Default}{RealName(player)}: {info.GetArg(1).Remove(0, 1)}");
			}

			return HookResult.Handled;
		}

		if (info.GetArg(1).StartsWith("!") || info.GetArg(1).StartsWith("@") || info.GetArg(1).StartsWith("/") || info.GetArg(1).StartsWith(".") || info.GetArg(1) == "rtv") return HookResult.Continue;

		if (JsonTags != null && JsonTags.TryGetValue("tags", out var tags) && tags is JObject tagsObject)
		{
			string deadIcon = !player.PawnIsAlive ? $"{ChatColors.White}☠ {ChatColors.Default}" : "";
			if (tagsObject.TryGetValue(steamid, out var playerTag) && playerTag is JObject)
			{
				string prefix = playerTag["prefix"]?.ToString() ?? "";
				string nickColor = playerTag?["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
				string messageColor = playerTag?["message_color"]?.ToString() ?? ChatColors.Default.ToString();

				foreach (var p in Utilities.GetPlayers().Where(p => p.TeamNum == player.TeamNum && p.IsValid && !p.IsBot))
				{
					string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{RealName(player)}{ChatColors.Default}: {messageColor}{info.GetArg(1)}";
					p.PrintToChat($" {ReplaceTags(messageToSend, p.TeamNum)}");
				}

				return HookResult.Handled;
			}

			foreach (var tagKey in tagsObject.Properties())
			{
				if (tagKey.Name.StartsWith("#"))
				{
					string group = tagKey.Name;
					bool inGroup = AdminManager.PlayerInGroup(player, group);

					if (inGroup && tagsObject.TryGetValue(group, out var groupTag) && groupTag is JObject)
					{
						string prefix = groupTag["prefix"]?.ToString() ?? "";
						string nickColor = groupTag["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
						string messageColor = groupTag["message_color"]?.ToString() ?? ChatColors.Default.ToString();

						foreach (var p in Utilities.GetPlayers().Where(p => p.TeamNum == player.TeamNum && p.IsValid && !p.IsBot))
						{
							string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{RealName(player)}{ChatColors.Default}: {messageColor}{info.GetArg(1)}";
							p.PrintToChat($" {ReplaceTags(messageToSend, p.TeamNum)}");
						}

						return HookResult.Handled;
					}
				}

				if (tagKey.Name.StartsWith("@"))
				{
					string permission = tagKey.Name;
					bool hasPermission = AdminManager.PlayerHasPermissions(player, permission);

					if (hasPermission && tagsObject.TryGetValue(permission, out var permissionTag) && permissionTag is JObject)
					{
						string prefix = permissionTag["prefix"]?.ToString() ?? "";
						string nickColor = permissionTag["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
						string messageColor = permissionTag["message_color"]?.ToString() ?? ChatColors.Default.ToString();

						foreach (var p in Utilities.GetPlayers().Where(p => p.TeamNum == player.TeamNum && p.IsValid && !p.IsBot))
						{
							string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{RealName(player)}{ChatColors.Default}: {messageColor}{info.GetArg(1)}";
							p.PrintToChat($" {ReplaceTags(messageToSend, p.TeamNum)}");
						}

						return HookResult.Handled;
					}
				}
			}

			if (tagsObject.TryGetValue("everyone", out var everyoneTag) && everyoneTag is JObject)
			{
				string prefix = everyoneTag["prefix"]?.ToString() ?? "";
				string nickColor = everyoneTag["nick_color"]?.ToString() ?? ChatColors.Default.ToString();
				string messageColor = everyoneTag["message_color"]?.ToString() ?? ChatColors.Default.ToString();

				foreach (var p in Utilities.GetPlayers().Where(p => p.TeamNum == player.TeamNum && p.IsValid && !p.IsBot))
				{
					string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{RealName(player)}{ChatColors.Default}: {messageColor}{info.GetArg(1)}";
					p.PrintToChat($" {ReplaceTags(messageToSend, p.TeamNum)}");
				}
				//p.PrintToChat(ReplaceTags($" {TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{RealName(player)}{ChatColors.Default}: {messageColor}{info.GetArg(1)}", p.TeamNum));

				return HookResult.Handled;
			}
		}
		return HookResult.Continue;
	}

	// Depuis la MAJ CS2 du 22/09/2026, le tag de clan a sa propre police : le tag "scoreboard" est donc mis devant le pseudo
	private void ApplyScoreboardTag(CCSPlayerController? player)
	{
		if (player == null || !player.IsValid || player.IsHLTV || (!player.IsBot && player.AuthorizedSteamID == null)) return;

		string currentName = player.PlayerName;

		// Premier passage, ou pseudo changé ailleurs (jeu, Steam, autre plugin) : c'est le nouveau vrai pseudo
		if (!AppliedNames.TryGetValue(player.Slot, out var appliedName) || appliedName != currentName)
			RealNames[player.Slot] = StripScoreboardTags(currentName);

		HideClanTag(player);

		string tag = GetScoreboardTag(player);
		SetName(player, string.IsNullOrEmpty(tag) ? RealNames[player.Slot] : WithSeparator(tag) + RealNames[player.Slot]);
		AppliedNames[player.Slot] = player.PlayerName;
	}

	private string GetScoreboardTag(CCSPlayerController player)
	{
		if (JsonTags == null || !JsonTags.TryGetValue("tags", out var tags) || tags is not JObject tagsObject) return "";

		// Les bots ont leur propre entrée "bot" (pas de tag si elle n'existe pas)
		if (player.IsBot)
			return tagsObject.TryGetValue("bot", out var botTag) && botTag is JObject ? botTag["scoreboard"]?.ToString() ?? "" : "";

		if (tagsObject.TryGetValue(player.SteamID.ToString(), out var playerTag) && playerTag is JObject && !string.IsNullOrEmpty(playerTag["scoreboard"]?.ToString()))
			return playerTag["scoreboard"]!.ToString();

		foreach (var tagKey in tagsObject.Properties())
		{
			bool matches = (tagKey.Name.StartsWith("#") && AdminManager.PlayerInGroup(player, tagKey.Name))
				|| (tagKey.Name.StartsWith("@") && AdminManager.PlayerHasPermissions(player, tagKey.Name));

			if (matches && tagKey.Value is JObject && !string.IsNullOrEmpty(tagKey.Value["scoreboard"]?.ToString()))
				return tagKey.Value["scoreboard"]!.ToString();
		}

		if (tagsObject.TryGetValue("everyone", out var everyone) && everyone is JObject)
			return everyone["scoreboard"]?.ToString() ?? "";

		return "";
	}

	private string RealName(CCSPlayerController player)
	{
		if (AppliedNames.TryGetValue(player.Slot, out var appliedName) && appliedName == player.PlayerName && RealNames.TryGetValue(player.Slot, out var realName))
			return realName;

		return StripScoreboardTags(player.PlayerName);
	}

	private List<string> ScoreboardTags()
	{
		if (JsonTags?["tags"] is not JObject tagsObject) return new List<string>();

		return tagsObject.Properties()
			.Select(tagKey => tagKey.Value is JObject ? tagKey.Value["scoreboard"]?.ToString() : null)
			.Where(tag => !string.IsNullOrWhiteSpace(tag))
			.Select(tag => tag!)
			.Distinct()
			.ToList();
	}

	// Masque le tag de clan de tous les joueurs et des bots (tag Steam, "BOT" du jeu, ancien tag du plugin) : texte et groupe
	private void HideClanTag(CCSPlayerController player)
	{
		if (!player.IsValid || player.IsHLTV) return;

		if (!string.IsNullOrEmpty(player.Clan))
		{
			ClearedClans[player.Slot] = (player.Clan, ClearedClans.TryGetValue(player.Slot, out var cleared) ? cleared.Count + 1 : 1);
			player.Clan = "";
			Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");
		}

		if (player.ClanId32bit != 0)
		{
			player.ClanId32bit = 0;
			Utilities.SetStateChanged(player, "CCSPlayerController", "m_unClanId32bit");
		}
	}

	// Enlève les tags du début d'un pseudo (ex. après un rechargement du plugin, les pseudos sont déjà tagués)
	private string StripScoreboardTags(string name)
	{
		var prefixes = ScoreboardTags().Select(WithSeparator).ToList();
		string stripped = name;
		string? prefix;

		while ((prefix = prefixes.FirstOrDefault(p => stripped.StartsWith(p, StringComparison.Ordinal))) != null)
			stripped = stripped.Substring(prefix.Length);

		return string.IsNullOrWhiteSpace(stripped) ? name : stripped;
	}

	private static string WithSeparator(string tag)
	{
		return tag.EndsWith(' ') ? tag : tag + " ";
	}

	private static void SetName(CCSPlayerController player, string name)
	{
		name = TruncateUtf8(name, 127); // m_iszPlayerName = char[128]
		if (player.PlayerName == name) return;

		player.PlayerName = name;
		Utilities.SetStateChanged(player, "CBasePlayerController", "m_iszPlayerName");
	}

	private static string TruncateUtf8(string value, int maxBytes)
	{
		if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;

		var result = new StringBuilder();
		int bytes = 0;

		foreach (var rune in value.EnumerateRunes())
		{
			bytes += rune.Utf8SequenceLength;
			if (bytes > maxBytes) break;
			result.Append(rune.ToString());
		}

		return result.ToString();
	}

	private string TeamName(int teamNum)
	{
		string teamName = "";

		switch (teamNum)
		{
			case 0:
				teamName = $"(NONE)";
				break;

			case 1:
				teamName = $"(SPEC)";
				break;

			case 2:
				teamName = $"{ChatColors.Yellow}(T)";
				break;

			case 3:
				teamName = $"{ChatColors.Blue}(CT)";
				break;
		}

		return teamName;
	}

	private string TeamColor(int teamNum)
	{
		string teamColor;

		switch (teamNum)
		{
			case 2:
				teamColor = $"{ChatColors.Gold}";
				break;

			case 3:
				teamColor = $"{ChatColors.Blue}";
				break;

			default:
				teamColor = "";
				break;
		}

		return teamColor;
	}

	private string ReplaceTags(string message, int teamNum = 0)
	{
		if (message.Contains('{'))
		{
			string modifiedValue = message;
			foreach (FieldInfo field in typeof(ChatColors).GetFields())
			{
				string pattern = $"{{{field.Name}}}";
				if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
				{
					modifiedValue = modifiedValue.Replace(pattern, field.GetValue(null)!.ToString(), StringComparison.OrdinalIgnoreCase);
				}
			}
			return modifiedValue.Replace("{TEAMCOLOR}", TeamColor(teamNum));
		}

		return message;
	}
}