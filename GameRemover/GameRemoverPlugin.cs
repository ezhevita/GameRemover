using System;
using System.Collections.Generic;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Steam.Interaction;
using JetBrains.Annotations;

namespace GameRemover;

// ReSharper disable once UnusedMember.Global
[Export(typeof(IPlugin))]
[UsedImplicitly]
public class GameRemoverPlugin : IBotCommand2 {
	public Task OnLoaded()
	{
		ASF.ArchiLogger.LogGenericInfo($"{Name} by ezhevita | Support & source code: https://github.com/ezhevita/{Name}");

		return Task.CompletedTask;
	}

	public string Name => nameof(GameRemover);
	public Version Version => Assembly.GetExecutingAssembly().GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	[CLSCompliant(false)]
	public Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0)
	{
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(args);

		return args[0].ToUpperInvariant() switch {
			"DELETEGAME" when args.Length > 2 => ResponseDeleteGame(steamID, access, args[1], args[2]),
			"DELETEGAME" when args.Length > 1 => ResponseDeleteGame(bot, access, args[1]),
			_ => Task.FromResult<string?>(null)
		};
	}

	private static async Task<string?> ResponseDeleteGame(Bot bot, EAccess access, string appIDText) {
		if (access < EAccess.Master) {
			return null;
		}
			
		if (!uint.TryParse(appIDText, out uint appID) || (appID == 0)) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(appID)));
		}
			
		const string requestDeleteGamePage = "/en/wizard/HelpWithGameIssue/?appid={0}&issueid=123";
		Uri uriDeleteGamePage = new(ArchiWebHandler.SteamHelpURL, string.Format(CultureInfo.InvariantCulture, requestDeleteGamePage, appID));
		using IDocument? responseDeleteGamePage = (await bot.ArchiWebHandler.UrlGetToHtmlDocumentWithSession(uriDeleteGamePage).ConfigureAwait(false))?.Content;
		if (responseDeleteGamePage == null) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(responseDeleteGamePage)));
		}
			
		IElement? node = responseDeleteGamePage.SelectSingleNode("//input[@id='packageid']");
		if (node == null) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.ErrorObjectIsNull, nameof(node)));
		}

		if (!uint.TryParse(node.GetAttribute("value"), out uint packageID) || (packageID == 0)) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(packageID)));
		}
			
		Dictionary<string, string> data = new(3) {
			{"packageid", packageID.ToString(CultureInfo.InvariantCulture)},
			{"appid", appIDText}
		};

		const string requestDeleteGame = "/en/wizard/AjaxDoPackageRemove";
		BooleanResponse? responseDeleteGame = (await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<BooleanResponse>(new Uri(ArchiWebHandler.SteamHelpURL, requestDeleteGame), data: data, referer: uriDeleteGamePage).ConfigureAwait(false))?.Content;

		return bot.Commands.FormatBotResponse(responseDeleteGame?.Success == true ? Strings.Success : Strings.WarningFailed);
	}

	private static async Task<string?> ResponseDeleteGame(ulong steamID, EAccess access, string botNames, string appIDText) {
		HashSet<Bot>? bots = Bot.GetBots(botNames);
		if ((bots == null) || (bots.Count == 0)) {
			return ASF.IsOwner(steamID) ? Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, Strings.BotNotFound, botNames)) : null;
		}

		IList<string?> results = await Utilities.InParallel(bots.Select(bot => ResponseDeleteGame(bot, Commands.GetProxyAccess(bot, access, steamID), appIDText))).ConfigureAwait(false);

		List<string> responses = new(results.Where(result => !string.IsNullOrEmpty(result))!);
		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}
}