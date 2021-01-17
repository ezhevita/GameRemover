using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using JetBrains.Annotations;

namespace GameRemover {
	// ReSharper disable once UnusedMember.Global
	[Export(typeof(IPlugin))]
	[UsedImplicitly]
	public class GameRemover : IBotCommand {
		public void OnLoaded() {
			ASF.ArchiLogger.LogGenericInfo(nameof(GameRemover) + " is loaded!");
		}

		public string Name => nameof(GameRemover);
		public Version Version => Assembly.GetExecutingAssembly().GetName().Version ?? throw new InvalidOperationException(nameof(Version));

		public async Task<string?> OnBotCommand(Bot bot, ulong steamID, string message, string[] args) {
			return args[0].ToUpperInvariant() switch {
				"DELETEGAME" when args.Length > 2 => await ResponseDeleteGame(steamID, args[1], args[2]),
				"DELETEGAME" when args.Length > 1 => await ResponseDeleteGame(bot, steamID, args[1]).ConfigureAwait(false),
				_ => null
			};
		}

		private static async Task<string?> ResponseDeleteGame(Bot bot, ulong steamID, string appIDText) {
			if (!bot.HasAccess(steamID, BotConfig.EAccess.Master)) {
				return null;
			}
			
			if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
				return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(appID)));
			}
			
			const string requestDeleteGamePage = "/en/wizard/HelpWithGameIssue/?appid={0}&issueid=123";
			using IDocument? responseDeleteGamePage = (await bot.ArchiWebHandler.UrlGetToHtmlDocumentWithSession(ArchiWebHandler.SteamHelpURL, string.Format(requestDeleteGamePage, appID)).ConfigureAwait(false))?.Content;
			if (responseDeleteGamePage == null) {
				return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorObjectIsNull, nameof(responseDeleteGamePage)));
			}
			
			IElement? node = responseDeleteGamePage.SelectSingleNode("//input[@id='packageid']");
			if (node == null) {
				return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorObjectIsNull, nameof(node)));
			}

			if (!uint.TryParse(node.GetAttribute("value"), out uint packageID) || (packageID == 0)) {
				return bot.Commands.FormatBotResponse(string.Format(Strings.ErrorIsInvalid, nameof(packageID)));
			}
			
			Dictionary<string, string> data = new(3) {
				{"packageid", packageID.ToString()},
				{"appid", appIDText}
			};

			const string requestDeleteGame = "/en/wizard/AjaxDoPackageRemove";
			Steam.BooleanResponse? responseDeleteGame = (await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<Steam.BooleanResponse>(ArchiWebHandler.SteamHelpURL, requestDeleteGame, data: data, referer: $"https://help.steampowered.com/en/wizard/HelpWithGameIssue/?appid={appID}&issueid=123").ConfigureAwait(false))?.Content;

			return bot.Commands.FormatBotResponse(responseDeleteGame?.Success == true ? Strings.Success : Strings.WarningFailed);
		}

		private static async Task<string?> ResponseDeleteGame(ulong steamID, string botNames, string appIDText) {
			HashSet<Bot>? bots = Bot.GetBots(botNames);
			if ((bots == null) || (bots.Count == 0)) {
				return ASF.IsOwner(steamID) ? Commands.FormatStaticResponse(string.Format(Strings.BotNotFound, botNames)) : null;
			}

			IList<string?> results = await Utilities.InParallel(bots.Select(bot => ResponseDeleteGame(bot, steamID, appIDText))).ConfigureAwait(false);

			List<string> responses = new(results.Where(result => !string.IsNullOrEmpty(result))!);
			return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
		}
	}
}
