/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 * New implementation for the Linux port. The behavior of the controller corresponds to the
 * CEF handlers of the Windows version (RequestHandler, ResourceHandler and DownloadHandler),
 * reimplemented on top of the Chrome DevTools Protocol. All locking behavior (request filter,
 * clipboard, print, zoom, kiosk, process and VM monitoring) has been removed: every request
 * is allowed, all tabs and downloads work and the browser runs in a normal window.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using System.Threading;
using System.Threading.Tasks;
using SafeExamBrowser.Linux.Browser.Cdp;
using SafeExamBrowser.Linux.Browser.Chromium;
using SafeExamBrowser.Linux.Browser.Content;

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
		/// The logger to be used, or null to use a console logger.
		/// </summary>
		public ILogger Logger { get; set; }
	}

	/// <summary>
	/// Controls a Chromium-based browser session according to the SEB configuration: injects the
	/// SEB JavaScript API and sends the browser exam key and configuration key headers. The
	/// session is unlocked: no requests are filtered, all tabs, downloads and input work.
	/// </summary>
	public sealed class BrowserController : IAsyncDisposable
	{
		private const string START_PAGE_FILE_EXTENSION = ".seb";
		private const string START_PAGE_MIME_TYPE = "application/seb";

		private readonly BrowserConfiguration configuration;
		private readonly ContentLoader contentLoader = new ContentLoader();
		private readonly Dictionary<string, DownloadInfo> downloads = new Dictionary<string, DownloadInfo>();
		private readonly SemaphoreSlim eventLock = new SemaphoreSlim(1, 1);
		private readonly KeyGenerator keyGenerator;
		private readonly ILogger logger;

		private readonly Dictionary<string, TargetState> targets = new Dictionary<string, TargetState>();
		private readonly TaskCompletionSource<bool> browserExit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		private CdpConnection connection;
		private BrowserLauncher launcher;

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

			quitUrlPattern = string.IsNullOrWhiteSpace(settings.QuitUrl)
				? default(Regex)
				: new Regex($"^{Regex.Escape(settings.QuitUrl.TrimEnd('/'))}/?$", RegexOptions.IgnoreCase);

			profileIsEphemeral = settings.DeleteCacheOnShutdown || settings.DeleteCookiesOnShutdown || settings.DeleteCookiesOnStartup;

			launcher = new BrowserLauncher(logger);

			var result = await launcher.LaunchAsync(new BrowserLaunchOptions
			{
				ExecutablePath = configuration.ChromiumExecutablePath,
				ProfileDirectory = profileIsEphemeral ? default(string) : GetPersistentProfileDirectory(),
				LogFile = configuration.LogFile,
				Settings = settings
			}, cancellationToken);

			MonitorBrowserExit();

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

			await connection.SendAsync("Page.navigate", new { url = RewriteSebScheme(url) }, startPage.SessionId);
		}

		/// <summary>
		/// Waits until the browser process has exited. When the browser has not been launched at all,
		/// the wait completes once the controller has been disposed.
		/// </summary>
		public Task WaitForExitAsync(CancellationToken cancellationToken = default)
		{
			return browserExit.Task.WaitAsync(cancellationToken);
		}

		private void MonitorBrowserExit()
		{
			_ = Task.Run(async () =>
			{
				try
				{
					await launcher.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
				}
				catch (Exception)
				{
					// The wait can fail when the launcher is disposed during shutdown, then the browser has exited anyway.
				}

				browserExit.TrySetResult(true);
			});
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

				browserExit.TrySetResult(true);
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

		private async Task InitializeBrowserAsync(CancellationToken cancellationToken)
		{
			var settings = configuration.Settings;
			var downloadDirectory = GetDownloadDirectory();

			Directory.CreateDirectory(downloadDirectory);

			logger.Debug($"Download directory: {downloadDirectory}.");

			await connection.SendAsync("Browser.setDownloadBehavior", new
			{
				behavior = "allow",
				downloadPath = downloadDirectory,
				eventsEnabled = true
			});

			firstPageReady = new TaskCompletionSource<TargetState>(TaskCreationOptions.RunContinuationsAsynchronously);

			await connection.SendAsync("Target.setDiscoverTargets", new { discover = true });

			// All supported targets (pages, frames, workers) are attached automatically, so a new
			// tab is intercepted as soon as it exists. With 'waitForDebuggerOnStart', the first
			// document of a newly created page target is held — before its request is dispatched
			// — until the session is released via 'Runtime.runIfWaitingForDebugger' (see
			// InitializeSessionAsync). Enabling request interception for the session therefore
			// always wins the race, and every document (including the very first request of a
			// new tab) is loaded through request interception. Targets that already existed
			// when the browser was attached (e.g. the initial about:blank tab) are not held,
			// because their first document started before any debugger was attached.
			await connection.SendAsync("Target.setAutoAttach", new
			{
				autoAttach = true,
				flatten = true,
				waitForDebuggerOnStart = true,
				filter = new[]
				{
					new { type = "page" },
					new { type = "iframe" },
					new { type = "webview" },
					new { type = "worker" },
					new { type = "shared_worker" },
					new { type = "service_worker" }
				}
			});

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

			// The first document of a new page target is held until the session is released
			// (see InitializeBrowserAsync), and the renderer of a held target does not process
			// commands. Request interception is therefore enabled first (a browser-side command
			// that responds even while the target is held), and the document is released right
			// after. The remaining page domain commands are only written to the socket and their
			// responses are collected in the background (see PrepareScriptUpdateAsync); the
			// command queue guarantees that they are registered before the document loads. The
			// page's frame ID and URL are taken from the first navigation event (see
			// HandleFrameNavigated), not from a frame tree query.
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
				// Release the held first document; the request it dispatches is intercepted by
				// the just enabled Fetch domain.
				connection.Post("Runtime.runIfWaitingForDebugger", sessionId: state.SessionId);

				try
				{
					await PrepareScriptUpdateAsync(state, "about:blank");
				}
				catch (Exception e)
				{
					logger.Debug($"Could not inject scripts into target {state.TargetId}: {e.Message}");
				}

				connection.Post("Page.enable", sessionId: state.SessionId);
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

				EnsureInterceptedDocumentAsync(state, url);

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

		/// <summary>
		/// Safety net: if a document of a page target is ever loaded without interception
		/// (which cannot happen for new targets, because their first document is held until
		/// interception is enabled — see InitializeBrowserAsync — but can happen for a target
		/// attached after its document was already dispatched), the page is reloaded once. The
		/// reload is guaranteed to pass through interception, because it is enabled before the
		/// navigation event is processed.
		/// </summary>
		private void EnsureInterceptedDocumentAsync(TargetState state, string url)
		{
			if (state.Type != "page" || state.DocumentIntercepted || state.ReloadedOnce)
			{
				return;
			}

			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			{
				return;
			}

			state.ReloadedOnce = true;

			logger.Debug($"The document of target {state.TargetId} was loaded without interception, reloading it.");

			connection.Post("Page.reload", sessionId: state.SessionId);
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

			// The first document request of a new page target always belongs to its main frame
			// (no frame can exist before it). Its frame ID is adopted here so that the request
			// is already signed as a main frame request; the first navigation event confirms
			// the same frame.
			if (isDocument && target != default && target.Type == "page" && target.RootFrameId == default && !string.IsNullOrEmpty(frameId))
			{
				target.RootFrameId = frameId;
			}

			var isMainFrame = isDocument && target != default && frameId == target.RootFrameId;

			var rewritten = RewriteSebScheme(url);

			if (isMainFrame && IsQuitUrl(rewritten))
			{
				logger.Debug($"Detected quit URL '{rewritten}'.");

				await FailRequestAsync(requestId, sessionId, "Aborted");
				await HandleQuitUrlAsync(rewritten);
				return;
			}

			if (isMainFrame && configuration.Settings.AllowConfigurationDownloads && IsConfigurationFile(rewritten))
			{
				logger.Info($"Detected configuration file '{rewritten}', requesting configuration download...");

				await FailRequestAsync(requestId, sessionId, "Aborted");
				ConfigurationDownloadRequested?.Invoke(rewritten);
				return;
			}

			if (isDocument && target != default)
			{
				target.DocumentIntercepted = true;

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

			script.Append(contentLoader.LoadApi(browserExamKey, configurationKey, AppInfo.VersionIdentity));

			script.AppendLine();
			script.AppendLine($"window.SafeExamBrowser.security.updateKeys = (callback) => callback();");

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
			public bool DocumentIntercepted { get; set; }
			public bool ReloadedOnce { get; set; }
		}

		private sealed class DownloadInfo
		{
			public string Url { get; set; }
			public string SuggestedFilename { get; set; }
		}
	}
}
