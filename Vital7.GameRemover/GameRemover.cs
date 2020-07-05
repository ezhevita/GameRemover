using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using JetBrains.Annotations;

namespace Vital7.GameRemover {
	// ReSharper disable once UnusedMember.Global
	[Export(typeof(IPlugin))]
	[UsedImplicitly]
	public class GameRemover : IBotCommand {
		public void OnLoaded() {
			ASF.ArchiLogger.LogGenericInfo(nameof(GameRemover) + " is loaded!");
		}

		public string Name => nameof(Vital7.GameRemover);
		public Version Version => new Version(1,0,0,0);

		public async Task<string> OnBotCommand(Bot bot, ulong steamID, string message, string[] args) {
			if ((bot == null) || (steamID == 0) || string.IsNullOrEmpty(message) || (args.Length == 0) || (args[0] == null) || (args[0].ToUpperInvariant() != "DELETEGAME")) {
				return null;
			}

			return await (args.Length > 2 ? PluginCommands.ResponseDeleteGame(steamID, args[1], args[2]) : bot.ResponseDeleteGame(steamID, args[1])).ConfigureAwait(false);
		}
	}

	internal static class PluginCommands {
		internal static async Task<string> ResponseDeleteGame(this Bot bot, ulong steamID, string appIDText) {
			if ((bot == null) || (steamID == 0)) {
				return null;
			}

			if (!bot.HasPermission(steamID, BotConfig.EPermission.Master)) {
				return null;
			}
			
			if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
				return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(appID)));
			}
			
			const string requestDeleteGamePage = "/en/wizard/HelpWithGameIssue/?appid={0}&issueid=123";
			IDocument responseDeleteGamePage = await bot.ArchiWebHandler.UrlGetToHtmlDocumentWithSession(ArchiWebHandler.SteamHelpURL, string.Format(requestDeleteGamePage, appID), false).ConfigureAwait(false);
			if (responseDeleteGamePage == null) {
				return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorObjectIsNull, nameof(responseDeleteGamePage)));
			}
			
			IElement node = responseDeleteGamePage.SelectSingleNode("//input[@id='packageid']");
			if (node == null) {
				return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorObjectIsNull, nameof(node)));
			}

			if (!uint.TryParse(node.GetAttributeValue("value"), out var packageID) || (packageID == 0)) {
				return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(packageID)));
			}
			
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{"packageid", packageID.ToString()},
				{"appid", appIDText}
			};

			const string requestDeleteGame = "/en/wizard/AjaxDoPackageRemove";
			Steam.BooleanResponse responseDeleteGame = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<Steam.BooleanResponse>(ArchiWebHandler.SteamHelpURL, requestDeleteGame, data, $"https://help.steampowered.com/en/wizard/HelpWithGameIssue/?appid={appIDText}&issueid=123").ConfigureAwait(false);
			return bot.Commands.FormatBotResponse(responseDeleteGame?.Success == true ? Strings.Success : Strings.WarningFailed);
		}

		internal static async Task<string> ResponseDeleteGame(ulong steamID, string botNames, string appIDText) {
			if ((steamID == 0) || string.IsNullOrEmpty(botNames)) {
				ASF.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(botNames));
				return null;
			}

			HashSet<Bot> bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? Commands.FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string> results = await Utilities.InParallel(bots.Select(bot => Task.Run(() => bot.ResponseDeleteGame(steamID, appIDText)))).ConfigureAwait(false);

			List<string> responses = new List<string>(results.Where(result => !string.IsNullOrEmpty(result)));
			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}
	}
}
