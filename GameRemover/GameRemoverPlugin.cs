using System;
using System.Collections.Generic;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
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
public class GameRemoverPlugin : IBotCommand2
{
	private static readonly CompositeFormat ErrorIsInvalid = CompositeFormat.Parse(Strings.ErrorIsInvalid);
	private static readonly CompositeFormat ErrorObjectIsNull = CompositeFormat.Parse(Strings.ErrorObjectIsNull);
	private static readonly CompositeFormat BotNotFound = CompositeFormat.Parse(Strings.BotNotFound);

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

		return args[0].ToUpperInvariant() switch
		{
			"DELETEGAME" when args.Length > 2 => ResponseDeleteGame(steamID, access, args[1], Utilities.GetArgsAsText(args, 2, ",")),
			"DELETEGAME" when args.Length > 1 => ResponseDeleteGame(bot, access, args[1]),
			_ => Task.FromResult<string?>(null)
		};
	}

	private static async Task<string?> ResponseDeleteGame(Bot bot, EAccess access, string appIDsText)
	{
		if (access < EAccess.Master)
		{
			return null;
		}

		HashSet<uint> appIDs = new();
		var appIDTexts = appIDsText.Split(',', StringSplitOptions.RemoveEmptyEntries);
		foreach (var appIDText in appIDTexts)
		{
			if (!uint.TryParse(appIDText, out var appID) || (appID == 0) || !appIDs.Add(appID))
			{
				return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, ErrorIsInvalid, appIDText));
			}
		}

		ushort successCount = 0;
		foreach (var appID in appIDs)
		{
			Uri uriDeleteGamePage = new(ArchiWebHandler.SteamHelpURL, $"/en/wizard/HelpWithGameIssue/?appid={appID}&issueid=123");
			using var responseDeleteGamePage = (await bot.ArchiWebHandler.UrlGetToHtmlDocumentWithSession(uriDeleteGamePage).ConfigureAwait(false))?.Content;
			if (responseDeleteGamePage == null)
			{
				return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, ErrorObjectIsNull, nameof(responseDeleteGamePage)));
			}

			var node = responseDeleteGamePage.SelectSingleNode<IElement>("//input[@id='packageid']");
			if (node == null)
			{
				return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, ErrorObjectIsNull, nameof(node)));
			}

			if (!uint.TryParse(node.GetAttribute("value"), out var packageID) || (packageID == 0))
			{
				return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, ErrorIsInvalid, nameof(packageID)));
			}

			Dictionary<string, string> data = new(3)
			{
				{"packageid", packageID.ToString(CultureInfo.InvariantCulture)},
				{"appid", appIDsText}
			};

			const string RequestDeleteGame = "/en/wizard/AjaxDoPackageRemove";
			var responseDeleteGame = (await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<BooleanResponse>(new Uri(ArchiWebHandler.SteamHelpURL, RequestDeleteGame), data: data, referer: uriDeleteGamePage).ConfigureAwait(false))?.Content;

			if (responseDeleteGame?.Success == true)
				successCount++;
		}

		return bot.Commands.FormatBotResponse(successCount == appIDs.Count ? Strings.Success : $"{Strings.WarningFailed}: {successCount} / {appIDs.Count}");
	}

	private static async Task<string?> ResponseDeleteGame(ulong steamID, EAccess access, string botNames, string appIDsText)
	{
		var bots = Bot.GetBots(botNames);
		if ((bots == null) || (bots.Count == 0))
		{
			return access >= EAccess.Owner ? Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, BotNotFound, botNames)) : null;
		}

		var results = await Utilities.InParallel(bots.Select(bot => ResponseDeleteGame(bot, Commands.GetProxyAccess(bot, access, steamID), appIDsText))).ConfigureAwait(false);

		List<string> responses = new(results.Where(result => !string.IsNullOrEmpty(result))!);

		return responses.Count > 0 ? string.Join(Environment.NewLine, responses) : null;
	}
}
