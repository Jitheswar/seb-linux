/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port. The behavior of the controller corresponds to the
 * CEF handlers of the Windows version (RequestHandler, ResourceHandler, RenderProcessMessageHandler,
 * DownloadHandler and WindowHandlingResponsibility), reimplemented on top of the Chrome DevTools Protocol.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SafeExamBrowser.Linux.Browser.Cdp;
using SafeExamBrowser.Linux.Browser.Chromium;
using SafeExamBrowser.Linux.Browser.Content;
using SafeExamBrowser.Linux.Browser.Filtering;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Cryptography;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Browser
{
	/// <summary>
	/// Defines the configuration for a browser session.
	/// </summary>
	public sealed class BrowserConfiguration
	{
		public BrowserSettings Settings { get; set; } = new BrowserSettings();
		public SecuritySettings Security { get; set; } = new SecuritySettings();

		/// <summary>
		/// The Chromium executable to be used, or null to auto-detect.
		/// </summary>
		public string ChromiumExecutablePath { get; set; }

		/// <summary>
		/// The Chromium user data directory to be used, or null for a temporary directory.
		/// </summary>
		public string ProfileDirectory { get; set; }

		/// <summary>
		/// The directory for downloads, or null for the default download directory.
		/// </summary>
		public string DownloadDirectory { get; set; }

		/// <summary>
		/// The file to which browser output is to be logged, or null to discard it.
		/// </summary>
		public string LogFile { get; set; }

		/// <summary>
		/// Determines whether the browser is to be launched in kiosk mode, or null to use the window mode of the configuration.
		/// </summary>
		public bool? KioskMode { get; set; }

		/// <summary>
		/// The logger to be used, or null to use a console logger.
		/// </summary>
		public ILogger Logger { get; set; }
	}

	/// <summary>
	/// Controls a Chromium-based browser session according to the SEB configuration: injects the
	/// SEB JavaScript API, filters requests, sends the browser exam key and configuration key
	/// headers, handles the quit URL and downloads and restricts clipboard and zoom behavior.
	/// </summary>
	public sealed class BrowserController : IAsyncDisposable
	{
		private const string PDF_TOOLBAR_FRAGMENT = "#toolbar=0";
		private const string PRINT_NOT_ALLOWED_MESSAGE = "Printing is not allowed during the exam.";
		private const string START_PAGE_FILE_EXTENSION = ".seb";
		private const string START_PAGE_MIME_TYPE = "application/seb";

		private readonly BrowserConfiguration configuration;
		private readonly ContentLoader contentLoader = new ContentLoader();
		private readonly Dictionary<string, DownloadInfo> downloads = new Dictionary<string, DownloadInfo>();
		private readonly SemaphoreSlim eventLock = new SemaphoreSlim(1, 1);
		private readonly KeyGenerator keyGenerator;
		private readonly ILogger logger;
		private readonly RequestFilter requestFilter = new RequestFilter();
		private readonly Dictionary<string, TargetState> targets = new Dictionary<string, TargetState>();
		private readonly HashSet<string> attachedTargets = new HashSet<string>();

		private CdpConnection connection;
		private BrowserLauncher launcher;
		private string blockedContent;
		private string blockedPage;
		private bool filterEnabled;
		private TaskCompletionSource<TargetState> firstPageReady;
		private string profileDirectory;
		private bool profileIsEphemeral;
		private Regex quitUrlPattern;
		private TargetState startPage;
		private bool startUrlNavigation;
		private bool disposed;

		public BrowserController(BrowserConfiguration configuration)
		{
			this.configuration = configuration ?? new BrowserConfiguration();

			logger = this.configuration.Logger ?? new ConsoleLogger("Browser", debug: true);
			keyGenerator = new KeyGenerator(logger);
		}

		/// <summary>
		/// Invoked when a request has been blocked by the request filter.
		/// </summary>
		public event Action<string> RequestBlocked;

		/// <summary>
		/// Invoked when the start URL has been loaded successfully.
		/// </summary>
		public event Action<string> StartUrlLoaded;

		/// <summary>
		/// Invoked when the quit URL has been visited and termination has been confirmed.
		/// </summary>
		public event Action TerminationRequested;

		/// <summary>
		/// Invoked when the quit URL has been visited and the browser is to be reset (restarted with a clean session).
		/// </summary>
		public event Action ResetRequested;

		/// <summary>
		/// Invoked when a download has been completed, with the URL, file name and file path.
		/// </summary>
		public event Action<string, string, string> DownloadCompleted;

		/// <summary>
		/// Invoked when a navigation to a SEB configuration file has been detected. The navigation
		/// has been cancelled, the configuration is to be downloaded and processed externally.
		/// </summary>
		public event Action<string> ConfigurationDownloadRequested;

		/// <summary>
		/// The user agent used by the browser.
		/// </summary>
		public string UserAgent { get; private set; }

		/// <summary>
		/// The current URL of the main browser window.
		/// </summary>
		public string CurrentUrl => startPage?.PageUrl;

		/// <summary>
		/// Launches the browser and applies the SEB configuration, then loads the start URL.
		/// </summary>
		public async Task LaunchAsync(CancellationToken cancellationToken = default)
		{
			var settings = configuration.Settings;

			keyGenerator.UseCustomBrowserExamKey(settings.CustomBrowserExamKey);

			InitializeRequestFilter();
			InitializeAppConfigValues();

			quitUrlPattern = string.IsNullOrWhiteSpace(settings.QuitUrl)
				? default(Regex)
				: new Regex($"^{Regex.Escape(settings.QuitUrl.TrimEnd('/'))}/?$", RegexOptions.IgnoreCase);

			profileIsEphemeral = settings.DeleteCacheOnShutdown || settings.DeleteCookiesOnShutdown || settings.DeleteCookiesOnStartup;

			launcher = new BrowserLauncher(logger);

			var result = await launcher.LaunchAsync(new BrowserLaunchOptions
			{
				ExecutablePath = configuration.ChromiumExecutablePath,
				ProfileDirectory = profileIsEphemeral ? default(string) : GetPersistentProfileDirectory(),
				KioskMode = configuration.KioskMode ?? settings.MainWindow.WindowMode == WindowMode.Fullscreen,
				LogFile = configuration.LogFile,
				Settings = settings
			}, cancellationToken);

			profileDirectory = result.ProfileDirectory;
			UserAgent = result.UserAgent;

			connection = new CdpConnection();
			connection.EventReceived += OnEventReceived;
			await connection.ConnectAsync(result.WebSocketUrl, cancellationToken);

			await InitializeBrowserAsync(cancellationToken);
		}

		/// <summary>
		/// Navigates the main browser window to the given URL.
		/// </summary>
		public async Task NavigateAsync(string url)
		{
			if (startPage == default)
			{
				throw new InvalidOperationException("The browser has not been launched yet!");
			}

			await connection.SendAsync("Page.navigate", new { url }, startPage.SessionId);
		}

		/// <summary>
		/// Waits until the browser process has exited.
		/// </summary>
		public Task WaitForExitAsync(CancellationToken cancellationToken = default)
		{
			return launcher?.WaitForExitAsync(cancellationToken) ?? Task.CompletedTask;
		}

		/// <summary>
		/// Closes the browser.
		/// </summary>
		public async Task QuitAsync()
		{
			try
			{
				if (connection?.IsConnected == true)
				{
					logger.Info("Requesting browser shutdown...");

					using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
					{
						await connection.SendAsync("Browser.close", cancellationToken: timeout.Token);
					}
				}
			}
			catch (Exception e)
			{
				logger.Debug($"Browser did not shut down gracefully: {e.Message}");
				launcher?.Terminate();
			}
		}

		public async ValueTask DisposeAsync()
		{
			if (disposed)
			{
				return;
			}

			disposed = true;

			try
			{
				await QuitAsync();

				if (launcher != default)
				{
					try
					{
						await launcher.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
					}
					catch (OperationCanceledException)
					{
						launcher.Terminate();
					}

					launcher.Dispose();
				}
			}
			catch (Exception e)
			{
				logger.Error("Error while disposing the browser.", e);
			}

			connection?.Dispose();

			if (profileIsEphemeral)
			{
				BrowserLauncher.DeleteProfile(profileDirectory);
			}

			eventLock.Dispose();
		}

		private void InitializeAppConfigValues()
		{
			blockedContent = contentLoader.LoadBlockedContent("This content has been blocked by Safe Exam Browser.");
			blockedPage = contentLoader.LoadBlockedPage("Safe Exam Browser", "This page has been blocked by Safe Exam Browser.", "Back");
		}

		private void InitializeRequestFilter()
		{
			var settings = configuration.Settings;

			filterEnabled = settings.Filter.EnableContentRequestFilter || settings.Filter.EnableMainRequestFilter;

			if (!filterEnabled)
			{
				logger.Debug("Request filter is disabled.");
				return;
			}

			foreach (var rule in settings.Filter.Rules)
			{
				var filterRule = RuleFactory.CreateRule(rule.ExpressionIsRegex);

				filterRule.Initialize(rule.Expression, rule.Action ? FilterResult.Allow : FilterResult.Block);
				requestFilter.Load(filterRule);
			}

			logger.Debug($"Initialized request filter with {settings.Filter.Rules.Count} rule(s).");

			if (requestFilter.Process(new Request { Url = settings.StartUrl }) != FilterResult.Allow)
			{
				var rule = RuleFactory.CreateRule(isRegex: false);

				rule.Initialize(settings.StartUrl, FilterResult.Allow);
				requestFilter.Load(rule);

				logger.Debug($"Automatically created filter rule to allow the start URL '{settings.StartUrl}'.");
			}
		}

		private async Task InitializeBrowserAsync(CancellationToken cancellationToken)
		{
			var settings = configuration.Settings;
			var downloadDirectory = GetDownloadDirectory();

			Directory.CreateDirectory(downloadDirectory);

			logger.Debug($"Download directory: {downloadDirectory}.");

			await connection.SendAsync("Browser.setDownloadBehavior", new
			{
				behavior = settings.AllowDownloads ? "allow" : "deny",
				downloadPath = downloadDirectory,
				eventsEnabled = true
			});

			firstPageReady = new TaskCompletionSource<TargetState>(TaskCreationOptions.RunContinuationsAsynchronously);

			await connection.SendAsync("Target.setDiscoverTargets", new { discover = true });

			using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				timeout.CancelAfter(TimeSpan.FromSeconds(30));

				startPage = await firstPageReady.Task.WaitAsync(timeout.Token);
			}

			logger.Info($"Navigating to start URL '{settings.StartUrl}'.");

			startUrlNavigation = true;

			await NavigateAsync(settings.StartUrl);
		}

		private string GetPersistentProfileDirectory()
		{
			if (!string.IsNullOrWhiteSpace(configuration.ProfileDirectory))
			{
				return configuration.ProfileDirectory;
			}

			var baseDirectory = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

			if (string.IsNullOrWhiteSpace(baseDirectory))
			{
				baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
			}

			return Path.Combine(baseDirectory, "seb-linux", "chromium-profile");
		}

		private string GetDownloadDirectory()
		{
			if (!string.IsNullOrWhiteSpace(configuration.DownloadDirectory))
			{
				return configuration.DownloadDirectory;
			}

			if (configuration.Settings.UseTemporaryDownUploadDirectory)
			{
				return Path.Combine(Path.GetTempPath(), "seb-linux-downloads");
			}

			if (!string.IsNullOrWhiteSpace(configuration.Settings.DownloadDirectory))
			{
				return configuration.Settings.DownloadDirectory;
			}

			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
		}

		private void OnEventReceived(CdpEvent @event)
		{
			_ = ProcessEventAsync(@event).ContinueWith(task =>
			{
				if (task.Exception != default)
				{
					logger.Error($"Error while processing DevTools event '{@event.Method}'.", task.Exception.GetBaseException());
				}
			}, TaskContinuationOptions.OnlyOnFaulted);
		}

		private async Task ProcessEventAsync(CdpEvent @event)
		{
			await eventLock.WaitAsync();

			try
			{
				await HandleEventAsync(@event);
			}
			finally
			{
				eventLock.Release();
			}
		}

		private async Task HandleEventAsync(CdpEvent @event)
		{
			switch (@event.Method)
			{
				case "Target.targetCreated":
					await HandleTargetCreatedAsync(@event);
					break;
				case "Target.attachedToTarget":
					await HandleAttachedToTargetAsync(@event);
					break;
				case "Target.detachedFromTarget":
					HandleDetachedFromTarget(@event);
					break;
				case "Target.targetDestroyed":
					break;
				case "Page.frameNavigated":
					HandleFrameNavigated(@event);
					break;
				case "Page.fileChooserOpened":
					await HandleFileChooserOpenedAsync(@event);
					break;
				case "Fetch.requestPaused":
					await HandleRequestPausedAsync(@event);
					break;
				case "Browser.downloadWillBegin":
					HandleDownloadWillBegin(@event);
					break;
				case "Browser.downloadProgress":
					HandleDownloadProgress(@event);
					break;
			}
		}

		private async Task HandleTargetCreatedAsync(CdpEvent @event)
		{
			var targetInfo = @event.Parameters.GetProperty("targetInfo");
			var type = targetInfo.GetProperty("type").GetString();
			var targetId = targetInfo.GetProperty("targetId").GetString();

			if (type != "page" || attachedTargets.Contains(targetId))
			{
				return;
			}

			attachedTargets.Add(targetId);

			logger.Debug($"Attaching to new page target {targetId}.");

			var result = await connection.SendAsync("Target.attachToTarget", new { targetId, flatten = true });
			var sessionId = result.GetProperty("sessionId").GetString();

			await InitializeSessionAsync(new TargetState
			{
				SessionId = sessionId,
				TargetId = targetId,
				Type = type
			});
		}

		private async Task HandleAttachedToTargetAsync(CdpEvent @event)
		{
			var sessionId = @event.Parameters.GetProperty("sessionId").GetString();
			var targetInfo = @event.Parameters.GetProperty("targetInfo");

			await InitializeSessionAsync(new TargetState
			{
				SessionId = sessionId,
				TargetId = targetInfo.GetProperty("targetId").GetString(),
				Type = targetInfo.GetProperty("type").GetString(),
				ParentSessionId = @event.SessionId
			});
		}

		private void HandleDetachedFromTarget(CdpEvent @event)
		{
			var sessionId = @event.Parameters.GetProperty("sessionId").GetString();

			if (targets.TryGetValue(sessionId, out var state))
			{
				targets.Remove(sessionId);
				attachedTargets.Remove(state.TargetId);

				logger.Debug($"Target {state.TargetId} ({state.Type}) has been detached.");
			}
		}

		private async Task InitializeSessionAsync(TargetState state)
		{
			if (targets.ContainsKey(state.SessionId))
			{
				return;
			}

			targets[state.SessionId] = state;

			var hasPageDomain = state.Type == "page" || state.Type == "iframe" || state.Type == "webview";

			if (hasPageDomain)
			{
				try
				{
					await connection.SendAsync("Page.enable", sessionId: state.SessionId);

					var tree = await connection.SendAsync("Page.getFrameTree", sessionId: state.SessionId);

					if (tree.ValueKind == JsonValueKind.Object && tree.TryGetProperty("frameTree", out var frameTree) && frameTree.TryGetProperty("frame", out var frame))
					{
						state.RootFrameId = frame.GetProperty("id").GetString();
						state.PageUrl = frame.TryGetProperty("url", out var url) ? url.GetString() : default;
					}

					await connection.SendAsync("Page.setInterceptFileChooserDialog", new { enabled = !configuration.Settings.AllowUploads }, state.SessionId);
				}
				catch (Exception e)
				{
					logger.Debug($"Could not initialize page domain for target {state.TargetId}: {e.Message}");
				}
			}

			try
			{
				await connection.SendAsync("Fetch.enable", new
				{
					patterns = new[] { new { urlPattern = "*", requestStage = "Request" } }
				}, state.SessionId);
			}
			catch (Exception e)
			{
				logger.Debug($"Could not enable request interception for target {state.TargetId}: {e.Message}");
			}

			if (hasPageDomain)
			{
				try
				{
					await PrepareScriptUpdateAsync(state, state.PageUrl ?? "about:blank");
				}
				catch (Exception e)
				{
					logger.Debug($"Could not inject scripts into target {state.TargetId}: {e.Message}");
				}
			}

			try
			{
				await connection.SendAsync("Target.setAutoAttach", new
				{
					autoAttach = true,
					flatten = true,
					waitForDebuggerOnStart = false,
					filter = new[]
					{
						new { type = "iframe" },
						new { type = "worker" },
						new { type = "shared_worker" },
						new { type = "service_worker" }
					}
				}, state.SessionId);
			}
			catch (Exception e)
			{
				logger.Debug($"Could not enable auto-attach for target {state.TargetId}: {e.Message}");
			}

			if (state.Type == "page")
			{
				startPage ??= state;
				firstPageReady?.TrySetResult(state);
			}
		}

		private void HandleFrameNavigated(CdpEvent @event)
		{
			if (!targets.TryGetValue(@event.SessionId ?? "", out var state))
			{
				return;
			}

			var frame = @event.Parameters.GetProperty("frame");
			var frameId = frame.GetProperty("id").GetString();
			var hasParent = frame.TryGetProperty("parentId", out _);
			var url = frame.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : default;

			if (!hasParent || state.RootFrameId == frameId)
			{
				state.RootFrameId ??= frameId;
				state.PageUrl = url;

				if (state.Type == "page" && state == startPage)
				{
					logger.Debug($"Main frame navigated to '{url}'.");

					if (startUrlNavigation && !string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
					{
						startUrlNavigation = false;

						StartUrlLoaded?.Invoke(url);
					}
				}
			}
		}

		private async Task HandleFileChooserOpenedAsync(CdpEvent @event)
		{
			if (!configuration.Settings.AllowUploads)
			{
				logger.Info("Blocked file chooser dialog (uploads are disabled).");

				await connection.SendAsync("Page.handleFileChooser", new { action = "cancel" }, @event.SessionId);
			}
		}

		private void HandleDownloadWillBegin(CdpEvent @event)
		{
			var guid = @event.Parameters.GetProperty("guid").GetString();
			var url = @event.Parameters.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : default;
			var filename = @event.Parameters.TryGetProperty("suggestedFilename", out var filenameElement) ? filenameElement.GetString() : default;

			downloads[guid] = new DownloadInfo { Url = url, SuggestedFilename = filename };

			logger.Info($"Download started: '{filename}' ({url}).");
		}

		private void HandleDownloadProgress(CdpEvent @event)
		{
			var guid = @event.Parameters.GetProperty("guid").GetString();
			var state = @event.Parameters.GetProperty("state").GetString();

			if (state != "completed")
			{
				if (state != "inProgress" && downloads.ContainsKey(guid))
				{
					logger.Warn($"Download '{downloads[guid].Url}' has been {state}.");
				}

				return;
			}

			var info = downloads.TryGetValue(guid, out var download) ? download : new DownloadInfo();
			var filePath = @event.Parameters.TryGetProperty("filePath", out var pathElement) ? pathElement.GetString() : default;

			if (string.IsNullOrWhiteSpace(filePath))
			{
				filePath = Path.Combine(GetDownloadDirectory(), info.SuggestedFilename ?? guid);
			}

			logger.Info($"Download completed: '{filePath}'.");

			DownloadCompleted?.Invoke(info.Url, info.SuggestedFilename, filePath);

			downloads.Remove(guid);
		}

		private async Task HandleRequestPausedAsync(CdpEvent @event)
		{
			var sessionId = @event.SessionId;
			var requestId = @event.Parameters.TryGetProperty("requestId", out var idElement) ? idElement.GetString() : default;

			try
			{
				await HandleRequestPausedCoreAsync(@event);
			}
			catch (Exception e)
			{
				logger.Error($"Error while handling paused request '{requestId}'.", e);

				// As a last resort, continue the request without modifications so that the page
				// cannot remain blocked indefinitely.
				try
				{
					await connection.SendAsync("Fetch.continueRequest", new { requestId }, sessionId);
				}
				catch (Exception)
				{
				}
			}
		}

		private async Task HandleRequestPausedCoreAsync(CdpEvent @event)
		{
			var sessionId = @event.SessionId;
			var parameters = @event.Parameters;
			var requestId = parameters.GetProperty("requestId").GetString();
			var request = parameters.GetProperty("request");
			var url = request.GetProperty("url").GetString();
			var resourceType = parameters.TryGetProperty("resourceType", out var typeElement) ? typeElement.GetString() : "Other";
			var frameId = parameters.TryGetProperty("frameId", out var frameElement) ? frameElement.GetString() : default;

			targets.TryGetValue(sessionId ?? "", out var target);

			var isDocument = resourceType == "Document";
			var isMainFrame = isDocument && target != default && frameId == target.RootFrameId;

			if (IsMailtoUrl(url))
			{
				logger.Info($"Blocked mailto request '{url}'.");

				await FailRequestAsync(requestId, sessionId, "BlockedByClient");
				return;
			}

			var rewritten = RewriteSebScheme(url);

			if (isMainFrame && IsQuitUrl(rewritten))
			{
				logger.Debug($"Detected quit URL '{rewritten}'.");

				await FailRequestAsync(requestId, sessionId, "Aborted");
				await HandleQuitUrlAsync(rewritten);
				return;
			}

			if (ShouldBlockRequest(target, resourceType, isMainFrame, rewritten, out var blocked))
			{
				await FulfillBlockedRequestAsync(requestId, sessionId, isDocument, isMainFrame, blocked);
				return;
			}

			if (isMainFrame && configuration.Settings.AllowConfigurationDownloads && IsConfigurationFile(rewritten))
			{
				logger.Info($"Detected configuration file '{rewritten}', requesting configuration download...");

				await FailRequestAsync(requestId, sessionId, "Aborted");
				ConfigurationDownloadRequested?.Invoke(rewritten);
				return;
			}

			if (configuration.Settings.AllowPdfReader && !configuration.Settings.AllowPdfReaderToolbar && isMainFrame)
			{
				if (rewritten.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !rewritten.Contains(PDF_TOOLBAR_FRAGMENT))
				{
					rewritten = $"{rewritten}{PDF_TOOLBAR_FRAGMENT}";

					logger.Debug($"Adding fragment to disable the PDF reader toolbar for '{url}'.");
				}
			}

			if (isDocument && target != default)
			{
				try
				{
					// Note: the script must be prepared before the request is continued, but the
					// commands must not be awaited, as the renderer is blocked until then.
					await PrepareScriptUpdateAsync(target, rewritten);
				}
				catch (Exception e)
				{
					logger.Debug($"Could not update scripts for '{rewritten}': {e.Message}");
				}
			}

			var headers = AppendCustomHeaders(target, request, rewritten, isMainFrame);

			await connection.SendAsync("Fetch.continueRequest", new
			{
				requestId,
				url = rewritten != url ? rewritten : default(string),
				headers
			}, sessionId);
		}

		private bool ShouldBlockRequest(TargetState target, string resourceType, bool isMainFrame, string url, out string blockedUrl)
		{
			blockedUrl = default;

			if (!filterEnabled)
			{
				return false;
			}

			var decoded = WebUtility.UrlDecode(url);

			if (!Uri.TryCreate(decoded, UriKind.Absolute, out _))
			{
				logger.Warn($"Filter could not process request for '{url}' ({resourceType})!");

				return false;
			}

			var settings = configuration.Settings;
			var result = requestFilter.Process(new Request { Url = decoded });
			var block = false;

			if (result == FilterResult.Block)
			{
				// The Windows version blocks main frame requests if either the main or the content
				// filter is enabled (see RequestHandler and ResourceHandler).
				block = isMainFrame
					? settings.Filter.EnableMainRequestFilter || settings.Filter.EnableContentRequestFilter
					: settings.Filter.EnableContentRequestFilter;
			}

			if (block)
			{
				blockedUrl = decoded;

				logger.Info($"Blocked {(isMainFrame ? "main" : "content")} request for '{decoded}' ({resourceType}).");
			}

			return block;
		}

		private async Task FulfillBlockedRequestAsync(string requestId, string sessionId, bool isDocument, bool isMainFrame, string url)
		{
			// Frame requests are rendered with the blocked page, all other content requests with
			// the blocked content snippet (see ResourceHandler.ResourceHandlerFor).
			var html = isDocument ? blockedPage : blockedContent;

			if (isMainFrame)
			{
				RequestBlocked?.Invoke(url);
			}

			await connection.SendAsync("Fetch.fulfillRequest", new
			{
				requestId,
				responseCode = 200,
				responseHeaders = new[]
				{
					new { name = "Content-Type", value = "text/html; charset=utf-8" }
				},
				body = Convert.ToBase64String(Encoding.UTF8.GetBytes(html))
			}, sessionId);
		}

		private object[] AppendCustomHeaders(TargetState target, JsonElement request, string url, bool isMainFrame)
		{
			var settings = configuration.Settings;
			var headers = new List<KeyValuePair<string, string>>();

			if (request.TryGetProperty("headers", out var requestHeaders) && requestHeaders.ValueKind == JsonValueKind.Object)
			{
				foreach (var header in requestHeaders.EnumerateObject())
				{
					headers.Add(new KeyValuePair<string, string>(header.Name, header.Value.ValueKind == JsonValueKind.String ? header.Value.GetString() : header.Value.ToString()));
				}
			}

			var pageUrl = GetPageUrl(target);
			var pageHost = Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri) ? pageUri.Host : default;
			var requestHost = Uri.TryCreate(url, UriKind.Absolute, out var requestUri) ? requestUri.Host : default;
			var sign = isMainFrame || (pageHost != default && string.Equals(pageHost, requestHost, StringComparison.OrdinalIgnoreCase));

			if (sign)
			{
				if (settings.SendConfigurationKey)
				{
					SetHeader(headers, "X-SafeExamBrowser-ConfigKeyHash", keyGenerator.CalculateConfigurationKeyHash(settings.ConfigurationKey, url));
				}

				if (settings.SendBrowserExamKey)
				{
					SetHeader(headers, "X-SafeExamBrowser-RequestHash", keyGenerator.CalculateBrowserExamKeyHash(settings.ConfigurationKey, settings.BrowserExamKeySalt, url));
				}
			}

			return headers.Select(h => (object) new { name = h.Key, value = h.Value }).ToArray();
		}

		private static void SetHeader(List<KeyValuePair<string, string>> headers, string name, string value)
		{
			headers.RemoveAll(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase));
			headers.Add(new KeyValuePair<string, string>(name, value));
		}

		private string GetPageUrl(TargetState target)
		{
			var current = target;

			while (current != default && current.Type != "page" && current.ParentSessionId != default)
			{
				targets.TryGetValue(current.ParentSessionId, out current);
			}

			return current?.PageUrl;
		}

		private async Task PrepareScriptUpdateAsync(TargetState target, string url)
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				url = "about:blank";
			}

			var settings = configuration.Settings;
			var browserExamKey = keyGenerator.CalculateBrowserExamKeyHash(settings.ConfigurationKey, settings.BrowserExamKeySalt, url);
			var configurationKey = keyGenerator.CalculateConfigurationKeyHash(settings.ConfigurationKey, url);
			var script = new StringBuilder();

			script.Append(contentLoader.LoadApi(browserExamKey, configurationKey, AppInfo.ProgramBuildVersion));

			script.AppendLine();
			script.AppendLine($"window.SafeExamBrowser.security.updateKeys = (callback) => callback();");

			if (!settings.AllowPrint)
			{
				script.AppendLine($"window.print = function () {{ alert('{PRINT_NOT_ALLOWED_MESSAGE}'); }};");
			}

			if (!settings.AllowPageZoom)
			{
				script.AppendLine(contentLoader.LoadPageZoom());
			}

			var policy = configuration.Security.ClipboardPolicy;

			if (policy == ClipboardPolicy.Block)
			{
				script.AppendLine(contentLoader.LoadBlockClipboard());
			}
			else if (policy == ClipboardPolicy.Isolated)
			{
				script.AppendLine(contentLoader.LoadIsolatedClipboard());
			}

			if (target.LastScript == script.ToString())
			{
				return;
			}

			target.LastScript = script.ToString();

			// Any previously registered script is removed and the new script is added. The commands
			// are only written to the socket here (the responses are collected in the background),
			// because the renderer does not process them until the navigation is continued.
			foreach (var identifier in target.ScriptIdentifiers)
			{
				var removal = await connection.StartCommandAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier }, target.SessionId);

				Ignore(removal);
			}

			target.ScriptIdentifiers.Clear();

			var addition = await connection.StartCommandAsync("Page.addScriptToEvaluateOnNewDocument", new { source = script.ToString() }, target.SessionId);

			_ = TrackScriptIdentifierAsync(target, addition);
		}

		private async Task TrackScriptIdentifierAsync(TargetState target, Task<JsonElement> addition)
		{
			try
			{
				var result = await addition;

				if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("identifier", out var identifier))
				{
					target.ScriptIdentifiers.Add(identifier.GetString());
				}
			}
			catch (Exception)
			{
			}
		}

		private static void Ignore(Task task)
		{
			_ = task.ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
		}

		private async Task HandleQuitUrlAsync(string url)
		{
			var settings = configuration.Settings;

			if (settings.ResetOnQuitUrl)
			{
				logger.Info("Reset requested via quit URL.");

				ResetRequested?.Invoke();
				return;
			}

			if (settings.QuitUrlConfirmation && !await ConfirmQuitAsync())
			{
				logger.Info($"User aborted termination via quit URL '{url}'.");

				return;
			}

			logger.Info("Termination requested via quit URL.");

			TerminationRequested?.Invoke();
		}

		private async Task<bool> ConfirmQuitAsync()
		{
			if (startPage == default)
			{
				return true;
			}

			try
			{
				var message = JsonSerializer.Serialize("The exam will be terminated. Do you really want to quit?");
				var result = await connection.SendAsync("Runtime.evaluate", new
				{
					expression = $"confirm({message})",
					returnByValue = true
				}, startPage.SessionId);

				return result.TryGetProperty("result", out var evaluation) && evaluation.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.True;
			}
			catch (Exception e)
			{
				logger.Warn($"Could not ask for quit confirmation: {e.Message}");

				return true;
			}
		}

		private bool IsQuitUrl(string url)
		{
			return quitUrlPattern != default && quitUrlPattern.IsMatch(url);
		}

		private bool IsMailtoUrl(string url)
		{
			return url.StartsWith(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Determines whether the URL points to a SEB configuration file, either with the .seb file
		/// extension or as data URI with the application/seb media type (see RequestHandler.IsConfigurationFile).
		/// </summary>
		private bool IsConfigurationFile(string url)
		{
			if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
			{
				return url.Contains(START_PAGE_MIME_TYPE, StringComparison.OrdinalIgnoreCase);
			}

			return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.AbsolutePath.EndsWith(START_PAGE_FILE_EXTENSION, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Rewrites seb:// and sebs:// URLs to http:// and https:// respectively (see ResourceHandler.ReplaceSebScheme).
		/// </summary>
		private string RewriteSebScheme(string url)
		{
			if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
			{
				if (uri.Scheme == "seb")
				{
					return new UriBuilder(uri) { Scheme = Uri.UriSchemeHttp }.Uri.AbsoluteUri;
				}

				if (uri.Scheme == "sebs")
				{
					return new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps }.Uri.AbsoluteUri;
				}
			}

			return url;
		}

		private async Task FailRequestAsync(string requestId, string sessionId, string errorReason)
		{
			await connection.SendAsync("Fetch.failRequest", new { requestId, errorReason }, sessionId);
		}

		private sealed class TargetState
		{
			public string SessionId { get; set; }
			public string TargetId { get; set; }
			public string Type { get; set; }
			public string ParentSessionId { get; set; }
			public string RootFrameId { get; set; }
			public string PageUrl { get; set; }
			public List<string> ScriptIdentifiers { get; } = new List<string>();
			public string LastScript { get; set; }
		}

		private sealed class DownloadInfo
		{
			public string Url { get; set; }
			public string SuggestedFilename { get; set; }
		}
	}
}
