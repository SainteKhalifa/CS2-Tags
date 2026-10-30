using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Text;

namespace CS2_Tags;

[MinimumApiVersion(159)]
public class CS2_Tags : BasePlugin
{
	private HashSet<string> GaggedIds = new HashSet<string>();
	// Par slot : le tag de clan d'origine du joueur (groupe Steam), et le tag de clan que le plugin lui a donné
	private readonly Dictionary<int, (string Clan, uint ClanId)> OriginalClans = new();
	private readonly Dictionary<int, string> AppliedClans = new();
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
		AddCommandListener("say", OnPlayerChat);
		AddCommandListener("say_team", OnPlayerChatTeam);

		// Joueurs déjà connectés (rechargement du plugin en cours de partie)
		AddTimer(1.0f, () => Utilities.GetPlayers().ForEach(ApplyClanTag));

		// Le jeu peut remettre le tag de clan Steam à tout moment (données Steam du joueur reçues en différé)
		AddTimer(2.0f, () => Utilities.GetPlayers().ForEach(EnforceClanTag), TimerFlags.REPEAT);
	}

	public override void Unload(bool hotReload)
	{
		if (hotReload) return;

		// Plugin retiré : rendre leur tag de clan d'origine aux joueurs
		Utilities.GetPlayers().ForEach(RestoreOriginalClan);
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
		Utilities.GetPlayers().ForEach(ApplyClanTag);

		Server.PrintToConsole("[CS2-Tags] Config reloaded!");
	}

	[ConsoleCommand("css_tags_debug")]
	[CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
	public void OnTagsDebugCommand(CCSPlayerController? caller, CommandInfo command)
	{
		Server.PrintToConsole($"[CS2-Tags] sv_clantags_enabled={ClanTagsEnabled()}");

		foreach (var player in Utilities.GetPlayers())
		{
			string original = OriginalClans.TryGetValue(player.Slot, out var entry) ? $"'{entry.Clan}' ({entry.ClanId})" : "-";
			Server.PrintToConsole($"[CS2-Tags] #{player.Slot} {(player.IsBot ? "bot" : "joueur")} pseudo='{player.PlayerName}' clan='{player.Clan}' clanId={player.ClanId32bit} origine={original}");
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

		AddTimer(2.0f, () => ApplyClanTag(player));
	}

	private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;

		if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) return HookResult.Continue;

		AddTimer(2.0f, () => ApplyClanTag(player));

		return HookResult.Continue;
	}

	private void OnClientDisconnect(int playerSlot)
	{
		OriginalClans.Remove(playerSlot);
		AppliedClans.Remove(playerSlot);

		CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerSlot);

		if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) return;

		GaggedIds.Remove(player.SteamID.ToString()!);
	}

	private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;
		if (player == null || !player.IsValid) return HookResult.Continue;

		AddTimer(1.5f, () => ApplyClanTag(player));

		return HookResult.Continue;
	}

	private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;
		if (player == null || !player.IsValid) return HookResult.Continue;

		AddTimer(1.5f, () => ApplyClanTag(player));

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

				SendChat(player, ReplaceTags($" {deadIcon}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}: {messageColor}{info.GetArg(1)}", player.TeamNum), Utilities.GetPlayers());

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

							SendChat(player, ReplaceTags($" {deadIcon}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}: {messageColor}{info.GetArg(1)}", player.TeamNum), Utilities.GetPlayers());

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

							SendChat(player, ReplaceTags($" {deadIcon}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}: {messageColor}{info.GetArg(1)}", player.TeamNum), Utilities.GetPlayers());

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

				SendChat(player, ReplaceTags($" {deadIcon}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}: {messageColor}{info.GetArg(1)}", player.TeamNum), Utilities.GetPlayers());

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
				p.PrintToChat($" {ChatColors.Lime}(ADMIN) {ChatColors.Default}{player.PlayerName}: {info.GetArg(1).Remove(0, 1)}");
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
					string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}: {messageColor}{info.GetArg(1)}";
					SendChat(player, $" {ReplaceTags(messageToSend, p.TeamNum)}", new[] { p });
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
							string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}: {messageColor}{info.GetArg(1)}";
							SendChat(player, $" {ReplaceTags(messageToSend, p.TeamNum)}", new[] { p });
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
							string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}: {messageColor}{info.GetArg(1)}";
							SendChat(player, $" {ReplaceTags(messageToSend, p.TeamNum)}", new[] { p });
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
					string messageToSend = $"{deadIcon}{TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}: {messageColor}{info.GetArg(1)}";
					SendChat(player, $" {ReplaceTags(messageToSend, p.TeamNum)}", new[] { p });
				}
				//p.PrintToChat(ReplaceTags($" {TeamName(player.TeamNum)} {ChatColors.Default}{prefix}{nickColor}{player.PlayerName}{ChatColors.Default}: {messageColor}{info.GetArg(1)}", p.TeamNum));

				return HookResult.Handled;
			}
		}
		return HookResult.Continue;
	}

	// Depuis la MAJ CS2 du 22/09/2026, le tag de clan s'affiche à côté du pseudo (scoreboard et killfeed) : le tag "scoreboard" y est placé
	private void ApplyClanTag(CCSPlayerController? player)
	{
		if (player == null || !player.IsValid || player.IsHLTV || (!player.IsBot && player.AuthorizedSteamID == null)) return;

		SaveOriginalClan(player);

		string tag = TruncateUtf8(GetScoreboardTag(player), 31); // m_szClan = char[32]

		// Pas de tag pour ce joueur : il garde (ou retrouve) le tag de son groupe Steam
		if (string.IsNullOrEmpty(tag))
		{
			RestoreOriginalClan(player);
			AppliedClans.Remove(player.Slot);
			return;
		}

		AppliedClans[player.Slot] = tag;
		SetClan(player, tag, 0);
	}

	// Remet le tag du plugin si le jeu l'a remplacé par le tag du groupe Steam du joueur
	private void EnforceClanTag(CCSPlayerController player)
	{
		if (!player.IsValid || player.IsHLTV || !AppliedClans.TryGetValue(player.Slot, out var tag)) return;

		SaveOriginalClan(player);
		SetClan(player, tag, 0);
	}

	// Un tag de clan différent de celui du plugin vient du jeu : c'est le tag d'origine du joueur
	private void SaveOriginalClan(CCSPlayerController player)
	{
		if (!AppliedClans.TryGetValue(player.Slot, out var applied) || player.Clan != applied)
			OriginalClans[player.Slot] = (player.Clan, player.ClanId32bit);
	}

	private void RestoreOriginalClan(CCSPlayerController player)
	{
		if (!player.IsValid || !AppliedClans.ContainsKey(player.Slot) || !OriginalClans.TryGetValue(player.Slot, out var original)) return;

		SetClan(player, original.Clan, original.ClanId);
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

	// Le texte du tag est affiché par le jeu ; l'id de groupe est mis à 0 pour que ce ne soit pas celui d'un groupe Steam.
	// L'étiquette « BOT » des bots est affichée par le jeu lui-même, elle ne vient pas du tag de clan.
	private static void SetClan(CCSPlayerController player, string clan, uint clanId)
	{
		if (player.Clan != clan)
		{
			player.Clan = clan;
			Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");
		}

		if (player.ClanId32bit != clanId)
		{
			player.ClanId32bit = clanId;
			Utilities.SetStateChanged(player, "CCSPlayerController", "m_unClanId32bit");
		}
	}

	// sv_clantags_enabled 0 (ajouté par la MAJ du 26/09/2026) masque tous les tags de clan, ceux du plugin compris
	private static string ClanTagsEnabled()
	{
		try
		{
			var convar = ConVar.Find("sv_clantags_enabled");
			return convar == null ? "introuvable" : convar.GetPrimitiveValue<bool>().ToString();
		}
		catch (Exception)
		{
			return "illisible";
		}
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
				teamName = $"{TeamColor(teamNum)}(T)";
				break;

			case 3:
				teamName = $"{TeamColor(teamNum)}(CT)";
				break;
		}

		return teamName;
	}

	// "game_team_colors" (tags.json, true par défaut) : messages envoyés comme ceux du jeu, avec ses vraies couleurs d'équipe
	private static bool GameTeamColors => JsonTags?["game_team_colors"]?.Type != JTokenType.Boolean || JsonTags["game_team_colors"]!.Value<bool>();

	// Envoie le message au nom du joueur (SayText2, comme le chat du jeu) : le jeu colore alors \x03 avec la couleur de son équipe
	private static void SendChat(CCSPlayerController sender, string message, IEnumerable<CCSPlayerController> recipients)
	{
		var players = recipients.Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToArray();
		if (players.Length == 0) return;

		if (!GameTeamColors)
		{
			foreach (var p in players)
				p.PrintToChat(message);
			return;
		}

		using var sayText = UserMessage.FromPartialName("SayText2");
		sayText.SetUInt("entityindex", sender.Index);
		sayText.SetBool("chat", true);
		sayText.SetString("messagename", message);
		sayText.SetString("param1", "");
		sayText.SetString("param2", "");
		sayText.SetString("param3", "");
		sayText.SetString("param4", "");
		sayText.Send(new RecipientFilter(players));
	}

	// Avec game_team_colors : \x03, coloré par le jeu. Sinon, couleurs les plus proches de la palette du chat : jaune clair (T), bleu clair (CT)
	private string TeamColor(int teamNum)
	{
		if (GameTeamColors && (teamNum == 2 || teamNum == 3))
			return "\x03";

		string teamColor;

		switch (teamNum)
		{
			case 2:
				teamColor = $"{ChatColors.LightYellow}";
				break;

			case 3:
				teamColor = $"{ChatColors.BlueGrey}";
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